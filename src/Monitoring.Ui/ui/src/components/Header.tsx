import type { Route } from "../state/useRoute";
import { Icon } from "./Icon";

export function Header({ route, onSwitchTheme }: { route: Route; onSwitchTheme(): void }) {
  const onDependencies = route.name === "dependencies";
  return (
    <header class="bar">
      <h1>
        <a href="#/" id="home">Monitoring</a>
      </h1>
      <nav aria-label="Views">
        <a href="#/" aria-current={onDependencies ? undefined : "page"}>Scopes</a>
        <a href="#/dependencies" aria-current={onDependencies ? "page" : undefined}>Dependencies</a>
      </nav>
      <button type="button" id="theme-toggle" class="icon-button" aria-label="Switch between light and dark theme" onClick={onSwitchTheme}>
        <Icon name="theme" />
      </button>
    </header>
  );
}
