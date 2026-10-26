import type { Overview } from "../../api/contracts";
import { Alerts } from "./Alerts";
import { FilterControl } from "./FilterControl";
import { Groups } from "./Groups";
import { useFilters } from "./useFilters";

/** The first page: the alerts that fire, then a tile per scope, grouped by the configured labels. */
export function OverviewView({ overview, now }: { overview: Overview | null; now: number }) {
  const { filters, set } = useFilters();
  const visible = overview?.scopes.filter((scope) => [...filters].every(([key, value]) => (scope.labels[key] ?? "") === value)) ?? [];

  const firing = (overview?.alerts.length ?? 0) > 0;
  const alerts = <Alerts alerts={overview?.alerts ?? []} now={now} />;

  return (
    <div>
      {/* Alerts that fire come first, above everything else; with none, they are a quiet section at the end. */}
      {firing && alerts}
      <h2>Scopes</h2>
      {overview !== null && visible.length > 0 && <FilterControl overview={overview} filters={filters} onChange={set} />}
      {(overview === null || visible.length === 0) && (
        <p class="muted">{overview !== null && overview.scopes.length > 0 ? "No scope matches the filter." : "No scope has reported yet."}</p>
      )}
      {/* The blocks of the environments run in as many columns as fit, so the page fills its width whatever the number of environments. */}
      <div class="blocks-grid">
        {overview !== null && visible.length > 0 && <Groups scopes={visible} keys={overview.groupBy} depth={1} now={now} />}
      </div>
      {!firing && alerts}
    </div>
  );
}
