import type { Route } from "../state/useRoute";
import { Icon } from "./Icon";

export function Header({ route, onSwitchTheme }: { route: Route; onSwitchTheme(): void }) {
  const current = (name: Route["name"]) => (route.name === name ? "page" : undefined);
  return (
    <header class="bar">
      <h1>
        <a href="#/" id="home">Monitoring</a>
      </h1>
      <nav aria-label="Views">
        <a href="#/" aria-current={route.name === "overview" || route.name === "scope" ? "page" : undefined}>Scopes</a>
        <a href="#/dependencies" aria-current={current("dependencies")}>Dependencies</a>
        <a href="#/packages" aria-current={current("packages")}>Packages</a>
      </nav>
      <button type="button" id="theme-toggle" class="icon-button" aria-label="Switch between light and dark theme" onClick={onSwitchTheme}>
        <Icon name="theme" />
      </button>
    </header>
  );
}
