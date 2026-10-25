import type { MetricHistory as History, SeriesHistory } from "../../api/contracts";
import { useJson } from "../../state/useJson";

const width = 120;
const height = 24;

/** The shape of the recent values of one series, scaled to its own range. */
function Sparkline({ series }: { series: SeriesHistory }) {
  const { values } = series;
  const low = Math.min(...values);
  const high = Math.max(...values);
  const span = high - low || 1;
  const points = values
    .map((value, index) => `${values.length === 1 ? width : (index / (values.length - 1)) * width},${height - 2 - ((value - low) / span) * (height - 4)}`)
    .join(" ");
  const label = `${series.name}: from ${low} to ${high}, now ${values[values.length - 1]}`;
  return (
    <svg class="sparkline" viewBox={`0 0 ${width} ${height}`} width={width} height={height} role="img" aria-label={label}>
      <polyline points={points} fill="none" stroke="currentColor" stroke-width="1.5" />
    </svg>
  );
}

const nameOf = (series: SeriesHistory) => {
  const tags = Object.entries(series.labels).map(([key, value]) => `${key}=${value}`);
  return tags.length === 0 ? series.name : `${series.name} {${tags.join(", ")}}`;
};

/** The recent values of the metrics of an instance, asked for from its agent only while the instance is open. */
export function MetricHistory({ scope, id, refresh }: { scope: string; id: string; refresh: unknown }) {
  const loaded = useJson<History>(`/api/scopes/${encodeURIComponent(scope)}/instances/${encodeURIComponent(id)}/history`, refresh);
  if (loaded.state !== "found" || loaded.value.series.length === 0) return null;

  const { intervalSeconds, series } = loaded.value;
  return (
    <>
      <h4>Metrics</h4>
      <table class="instances">
        <thead>
          <tr>
            <th>Metric</th>
            <th>Last {Math.round((intervalSeconds * Math.max(...series.map((s) => s.values.length))) / 60)} min</th>
            <th>Now</th>
          </tr>
        </thead>
        <tbody>
          {series.map((s) => (
            <tr key={nameOf(s)}>
              <td>{nameOf(s)}</td>
              <td>
                <Sparkline series={s} />
              </td>
              <td>{s.values[s.values.length - 1]}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </>
  );
}
