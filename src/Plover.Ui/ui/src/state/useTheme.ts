import { useCallback } from "preact/hooks";
import type { Services } from "../services/Services";
import { useServices } from "../services/ServicesContext";

type Theme = "light" | "dark";

const isTheme = (value: string | null): value is Theme => value === "light" || value === "dark";

/** Applies the theme before the first paint: forced by ?theme=, else the remembered one, else the system setting is left to the style sheet. */
export function applyTheme(services: Services): void {
  const forced = new URLSearchParams(window.location.search).get("theme");
  const theme = isTheme(forced) ? forced : services.recall("theme");
  if (isTheme(theme)) document.documentElement.dataset["theme"] = theme;
}

/** Returns a function that switches between the light and the dark theme and remembers the choice. */
export function useThemeSwitch(): () => void {
  const services = useServices();
  return useCallback(() => {
    const current = document.documentElement.dataset["theme"];
    const dark = current ? current === "dark" : window.matchMedia("(prefers-color-scheme: dark)").matches;
    const next: Theme = dark ? "light" : "dark";
    document.documentElement.dataset["theme"] = next;
    services.remember("theme", next);
  }, [services]);
}
