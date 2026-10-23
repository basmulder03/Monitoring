import { useEffect, useState } from "preact/hooks";

export type Route =
  | { name: "overview" }
  | { name: "dependencies" }
  | { name: "packages" }
  | { name: "system" }
  | { name: "scope"; scope: string };

function read(): Route {
  const hash = window.location.hash;
  if (hash === "#/dependencies") return { name: "dependencies" };
  if (hash === "#/packages") return { name: "packages" };
  if (hash === "#/system") return { name: "system" };
  const match = /^#\/scopes\/(.+)$/.exec(hash);
  return match?.[1] === undefined ? { name: "overview" } : { name: "scope", scope: decodeURIComponent(match[1]) };
}

/** Which view the address asks for. */
export function useRoute(): Route {
  const [route, setRoute] = useState(read);
  useEffect(() => {
    const update = () => setRoute(read());
    window.addEventListener("hashchange", update);
    return () => window.removeEventListener("hashchange", update);
  }, []);
  return route;
}
