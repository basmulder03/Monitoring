import { useState } from "preact/hooks";
import type { TopologyGraph } from "../../api/contracts";
import { useJson } from "../../state/useJson";
import { GraphSvg } from "./GraphSvg";
import { averageMs, nameOf } from "./text";

/** Which services call what, as a picture and as a table with the same numbers. */
export function DependenciesView({ refresh }: { refresh: unknown }) {
  const loaded = useJson<TopologyGraph>("/api/topology", refresh);
  const [selected, select] = useState<string | null>(null);

  if (loaded.state === "loading") return <p class="muted">Loading…</p>;
  if (loaded.state !== "found") return <p class="muted">The dependencies could not be loaded.</p>;

  const graph = loaded.value;
  return (
    <div>
      <h2>Dependencies</h2>
      {graph.nodes.length === 0 ? (
        <p class="muted">No calls between services have been reported yet.</p>
      ) : (
        <>
          <div class="graph-frame">
            <GraphSvg graph={graph} selected={selected} onSelect={select} />
          </div>
          {/* The same facts as text, for screen readers and for anyone who wants the numbers. */}
          <table class="instances">
            <thead>
              <tr>
                {["From", "To", "Type", "Calls", "Errors", "Average"].map((title) => (
                  <th key={title}>{title}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {graph.edges.map((edge) => (
                <tr key={`${edge.from}>${edge.to}>${edge.type}`}>
                  <td>{nameOf(edge.from, graph)}</td>
                  <td>{nameOf(edge.to, graph)}</td>
                  <td>{edge.type}</td>
                  <td>{edge.calls}</td>
                  <td>{edge.errors}</td>
                  <td>{averageMs(edge)} ms</td>
                </tr>
              ))}
            </tbody>
          </table>
        </>
      )}
    </div>
  );
}
