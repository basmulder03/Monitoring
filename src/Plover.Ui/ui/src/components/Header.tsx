import type { Route } from "../state/useRoute";
import type { WhoAmI } from "../api/contracts";
import { useJson } from "../state/useJson";
import { Icon } from "./Icon";

/** The top bar: where the dashboard is, the links to its views and the switch between light and dark. */
export function Header({ route, onSwitchTheme }: { route: Route; onSwitchTheme(): void }) {
  const current = (name: Route["name"]) => (route.name === name ? "page" : undefined);
  return (
    <header class="bar">
      <h1>
        <a href="#/" id="home">Plover</a>
      </h1>
      <nav aria-label="Views">
        <a href="#/" aria-current={route.name === "overview" || route.name === "scope" ? "page" : undefined}>Scopes</a>
        <a href="#/dependencies" aria-current={current("dependencies")}>Dependencies</a>
        <a href="#/packages" aria-current={current("packages")}>Packages</a>
        <a href="#/system" aria-current={current("system")}>System</a>
      </nav>
      <SignedIn />
      <button type="button" id="theme-toggle" class="icon-button" aria-label="Switch between light and dark theme" onClick={onSwitchTheme}>
        <Icon name="theme" />
      </button>
    </header>
  );
}

/** Who the dashboard thinks is looking, when it asks at all; nothing is shown on a controller that is open to everyone. */
function SignedIn() {
  const who = useJson<WhoAmI>("/api/me", null);
  if (who.state !== "found" || !who.value.authenticationEnabled || who.value.name === "") return null;
  return (
    <span class="muted" id="signed-in">
      {who.value.name} ({who.value.role})
    </span>
  );
}
