import type { Overview } from "../../api/contracts";

interface FilterControlProps {
  overview: Overview;
  filters: ReadonlyMap<string, string>;
  onChange(key: string, value: string): void;
}

/** Lets the reader pick one value of the outermost label, for example a single environment, or all of them. */
export function FilterControl({ overview, filters, onChange }: FilterControlProps) {
  const key = overview.groupBy[0];
  if (key === undefined) return null;

  const values = [...new Set(overview.scopes.map((scope) => scope.labels[key]).filter((value): value is string => !!value))].sort();
  if (values.length < 2) return null;

  return (
    <div class="filter">
      <label for="filter-select">Show {key} </label>
      <select id="filter-select" value={filters.get(key) ?? ""} onChange={(event) => onChange(key, event.currentTarget.value)}>
        <option value="">All</option>
        {values.map((value) => (
          <option value={value} key={value}>
            {value}
          </option>
        ))}
      </select>
    </div>
  );
}
