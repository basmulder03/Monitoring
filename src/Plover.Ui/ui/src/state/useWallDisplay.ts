import { useEffect, useMemo, useRef } from "preact/hooks";
import type { Overview } from "../api/contracts";
import { useServices } from "../services/ServicesContext";
import { hashOf, readWallConfig } from "./wallConfig";

/**
 * Makes the page behave as a wall display under /tv: it goes through the chosen views on a timer, and when a scope turns Unhealthy
 * it stops and shows that scope until the problem is gone or the focus time has passed. A scope that is left because the time ran out
 * is not shown again until it has recovered, so one lasting problem does not hold the screen for ever.
 */
export function useWallDisplay(overview: Overview | null): void {
  const services = useServices();
  const config = useMemo(() => readWallConfig(window.location.pathname, window.location.search), []);
  const state = useRef({ index: 0, focused: null as string | null, focusSince: 0, lastRotation: services.now(), dismissed: new Set<string>() });

  useEffect(() => {
    if (config?.drift) document.body.classList.add("drift");
    if (config === null) return undefined;

    const resume = () => {
      const current = state.current;
      current.focused = null;
      current.lastRotation = services.now();
      window.location.hash = hashOf(config.views[current.index] ?? "overview");
    };

    return services.every(1000, () => {
      const current = state.current;
      const now = services.now();
      if (current.focused !== null) {
        if (config.focusSeconds > 0 && now - current.focusSince >= config.focusSeconds * 1000) {
          current.dismissed.add(current.focused);
          resume();
        }
      } else if (config.rotateSeconds > 0 && config.views.length > 1 && now - current.lastRotation >= config.rotateSeconds * 1000) {
        current.index = (current.index + 1) % config.views.length;
        current.lastRotation = now;
        window.location.hash = hashOf(config.views[current.index] ?? "overview");
      }
    });
  }, [services, config]);

  useEffect(() => {
    if (config === null || overview === null) return;

    const current = state.current;
    const failing = overview.scopes.filter((scope) => scope.health === "Unhealthy").map((scope) => scope.name);

    // A scope that has recovered may be shown again the next time it fails.
    for (const name of [...current.dismissed]) {
      if (!failing.includes(name)) current.dismissed.delete(name);
    }

    if (current.focused !== null) {
      if (!failing.includes(current.focused)) {
        current.focused = null;
        current.lastRotation = services.now();
        window.location.hash = hashOf(config.views[current.index] ?? "overview");
      }
      return;
    }

    const next = failing.find((name) => !current.dismissed.has(name));
    if (next !== undefined) {
      current.focused = next;
      current.focusSince = services.now();
      window.location.hash = `#/scopes/${encodeURIComponent(next)}`;
    }
  }, [overview, services, config]);
}
