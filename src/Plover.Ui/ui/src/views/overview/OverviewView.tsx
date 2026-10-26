import type { Overview } from "../../api/contracts";
import { Alerts } from "./Alerts";
import { FilterControl } from "./FilterControl";
import { Groups } from "./Groups";
import { SystemTile } from "./SystemTile";
import { useFilters } from "./useFilters";

/** The first page: a tile per scope, grouped by the configured labels, and the alerts. */
export function OverviewView({ overview, now }: { overview: Overview | null; now: number }) {
  const { filters, set } = useFilters();
  const visible = overview?.scopes.filter((scope) => [...filters].every(([key, value]) => (scope.labels[key] ?? "") === value)) ?? [];

  return (
    <div>
      <h2>Scopes</h2>
      {overview !== null && visible.length > 0 && <FilterControl overview={overview} filters={filters} onChange={set} />}
      {(overview === null || visible.length === 0) && (
        <p class="muted">{overview !== null && overview.scopes.length > 0 ? "No scope matches the filter." : "No scope has reported yet."}</p>
      )}
      {/* The blocks of the scopes and the block of the system share one grid, so the page fills its width whatever the number of environments. */}
      <div class="blocks-grid">
        {overview !== null && visible.length > 0 && <Groups scopes={visible} keys={overview.groupBy} depth={1} now={now} />}
        <SystemTile refresh={overview} />
      </div>
      <Alerts alerts={overview?.alerts ?? []} now={now} />
    </div>
  );
}
