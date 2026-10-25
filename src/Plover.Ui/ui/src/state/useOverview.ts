import { useEffect, useState } from "preact/hooks";
import type { HealthState, Overview } from "../api/contracts";
import { HEALTH_LABELS } from "../format";
import { useServices } from "../services/ServicesContext";

export interface OverviewState {
  /** The latest overview; null until the first one arrives. */
  overview: Overview | null;
  /** When something was last heard from the controller, even if only a keep-alive. */
  lastHeard: number;
  /** What changed since the previous overview, in words, for people who cannot see the page. */
  announcement: string;
}

/** Follows the live stream of overviews. */
export function useOverview(): OverviewState {
  const services = useServices();
  const [state, setState] = useState<OverviewState>(() => ({ overview: null, lastHeard: services.now(), announcement: "" }));

  useEffect(() => {
    let previous = new Map<string, HealthState>();
    return services.followOverview({
      onOverview(overview) {
        const now = new Map(overview.scopes.map((scope) => [scope.name, scope.health] as const));
        const changes = [...now].flatMap(([name, health]) => {
          const before = previous.get(name);
          return before !== undefined && before !== health ? [`${name} is now ${HEALTH_LABELS[health]}`] : [];
        });
        previous = now;
        setState((old) => ({ overview, lastHeard: services.now(), announcement: changes.length > 0 ? changes.join(". ") : old.announcement }));
      },
      onKeepAlive() {
        setState((old) => ({ ...old, lastHeard: services.now() }));
      },
    });
  }, [services]);

  return state;
}
