import { Header } from "./components/Header";
import { Sprite } from "./components/Sprite";
import { StaleBanner } from "./components/StaleBanner";
import { OpenStateProvider } from "./state/OpenState";
import { useClock } from "./state/useClock";
import { useOverview } from "./state/useOverview";
import { useRoute } from "./state/useRoute";
import { useThemeSwitch } from "./state/useTheme";
import { DependenciesView } from "./views/dependencies/DependenciesView";
import { PackagesView } from "./views/packages/PackagesView";
import { OverviewView } from "./views/overview/OverviewView";
import { ScopeView } from "./views/scope/ScopeView";

/** The whole page: header, warnings about old data, and the view the address asks for. */
export function App() {
  const route = useRoute();
  const switchTheme = useThemeSwitch();
  const { overview, lastHeard, announcement } = useOverview();
  const seconds = useClock(1000);
  const halfMinutes = useClock(30000);

  return (
    <OpenStateProvider>
      <Sprite />
      <Header route={route} onSwitchTheme={switchTheme} />
      <StaleBanner lastHeard={lastHeard} now={seconds} />
      <div id="announcer" class="visually-hidden" role="status" aria-live="polite">
        {announcement}
      </div>
      <main id="app" tabindex={-1}>
        {route.name === "overview" && <OverviewView overview={overview} now={halfMinutes} />}
        {route.name === "scope" && <ScopeView name={route.scope} refresh={overview} now={halfMinutes} />}
        {route.name === "dependencies" && <DependenciesView refresh={overview} />}
        {route.name === "packages" && <PackagesView refresh={overview} />}
      </main>
    </OpenStateProvider>
  );
}
