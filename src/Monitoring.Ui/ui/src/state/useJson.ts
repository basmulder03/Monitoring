import { useEffect, useState } from "preact/hooks";
import { useServices } from "../services/ServicesContext";

export type Loaded<T> =
  | { state: "loading" }
  | { state: "found"; value: T }
  | { state: "missing" }
  | { state: "failed" };

/**
 * Loads JSON from the controller, again whenever the address or the refresh key changes.
 * An answer to an earlier request that arrives late is ignored, and what is shown stays on screen while the next answer is awaited.
 */
export function useJson<T>(path: string, refresh: unknown): Loaded<T> {
  const services = useServices();
  const [loaded, setLoaded] = useState<{ path: string; result: Loaded<T> }>({ path, result: { state: "loading" } });

  useEffect(() => {
    let current = true;
    services
      .getJson<T>(path)
      .then(({ status, body }) => {
        if (!current) return;
        setLoaded({ path, result: status === 404 ? { state: "missing" } : body === null ? { state: "failed" } : { state: "found", value: body } });
      })
      .catch(() => {
        if (current) setLoaded({ path, result: { state: "failed" } });
      });
    return () => {
      current = false;
    };
  }, [services, path, refresh]);

  // What was loaded for another address is not an answer for this one.
  return loaded.path === path ? loaded.result : { state: "loading" };
}
