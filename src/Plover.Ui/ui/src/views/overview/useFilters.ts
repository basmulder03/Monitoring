import { useCallback, useState } from "preact/hooks";

/** The scopes to show are narrowed by ?filter=key:value (repeatable), which also keeps a wall display on one environment. */
export function useFilters(): { filters: ReadonlyMap<string, string>; set(key: string, value: string): void } {
  const [filters, setFilters] = useState<ReadonlyMap<string, string>>(() => {
    const map = new Map<string, string>();
    for (const entry of new URLSearchParams(window.location.search).getAll("filter")) {
      const colon = entry.indexOf(":");
      if (colon > 0) map.set(entry.slice(0, colon), entry.slice(colon + 1));
    }
    return map;
  });

  const set = useCallback((key: string, value: string) => {
    setFilters((old) => {
      const next = new Map(old);
      if (value) next.set(key, value);
      else next.delete(key);

      const url = new URL(window.location.href);
      url.searchParams.delete("filter");
      for (const [k, v] of next) url.searchParams.append("filter", `${k}:${v}`);
      history.replaceState(null, "", url);
      return next;
    });
  }, []);

  return { filters, set };
}
