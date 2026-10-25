import { useMemo } from "preact/hooks";
import type { TopologyGraph } from "../../api/contracts";
import { HEALTH_LABELS, plural } from "../../format";
import { NODE_HEIGHT, NODE_WIDTH, layoutGraph, shapeOf } from "./graphLayout";
import { KIND_TEXT, edgeText, fit } from "./text";

interface GraphSvgProps {
  graph: TopologyGraph;
  selected: string | null;
  onSelect(id: string | null): void;
}

/** The graph as a picture: callers on the left, edges as thick as their call volume, failing edges marked in words. */
export function GraphSvg({ graph, selected, onSelect }: GraphSvgProps) {
  const shape = shapeOf(graph);
  const layout = useMemo(() => layoutGraph(graph), [shape]);
  const maxCalls = Math.max(1, ...graph.edges.map((edge) => edge.calls));
  const touches = (id: string) => graph.edges.some((edge) => (edge.from === selected && edge.to === id) || (edge.to === selected && edge.from === id));

  return (
    <svg class="graph" viewBox={`0 0 ${layout.width} ${layout.height}`} role="group" aria-label="Dependency graph">
      <title>Dependency graph</title>
      <defs>
        <marker id="arrow" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="7" markerHeight="7" orient="auto-start-reverse">
          <path d="M0 0 L10 5 L0 10 z" class="arrow" />
        </marker>
      </defs>

      {graph.edges.map((edge) => {
        const from = layout.positions.get(edge.from);
        const to = layout.positions.get(edge.to);
        if (!from || !to) return null;
        const x1 = from.x + NODE_WIDTH;
        const y1 = from.y + NODE_HEIGHT / 2;
        const x2 = to.x;
        const y2 = to.y + NODE_HEIGHT / 2;
        const bend = Math.max(40, Math.abs(x2 - x1) / 2);
        const failing = edge.errors > 0;
        const dim = selected !== null && edge.from !== selected && edge.to !== selected;
        return (
          <g key={`${edge.from}>${edge.to}>${edge.type}`} class={`edge${failing ? " failing" : ""}${dim ? " dim" : ""}`}>
            <path
              d={`M${x1} ${y1} C${x1 + bend} ${y1} ${x2 - bend} ${y2} ${x2} ${y2}`}
              stroke-width={1.5 + (5 * Math.log10(1 + edge.calls)) / Math.log10(1 + maxCalls)}
              marker-end="url(#arrow)"
              fill="none"
            >
              <title>{edgeText(edge, graph)}</title>
            </path>
            {failing && (
              <text x={(x1 + x2) / 2} y={(y1 + y2) / 2 - 6} class="edge-label" text-anchor="middle">
                {plural(edge.errors, "error", "errors")}
              </text>
            )}
          </g>
        );
      })}

      {graph.nodes.map((node) => {
        const at = layout.positions.get(node.id);
        if (!at) return null;
        const health = node.health ? ` — ${HEALTH_LABELS[node.health]}` : "";
        const isSelected = node.id === selected;
        const dim = selected !== null && !isSelected && !touches(node.id);
        const classes = ["node", node.kind.toLowerCase(), node.health?.toLowerCase(), isSelected && "selected", dim && "dim"].filter(Boolean).join(" ");
        const toggle = () => onSelect(isSelected ? null : node.id);
        return (
          <g
            key={node.id}
            class={classes}
            transform={`translate(${at.x} ${at.y})`}
            tabindex={0}
            role="button"
            aria-pressed={isSelected}
            aria-label={`${node.name}, ${KIND_TEXT[node.kind]}${health}`}
            onClick={toggle}
            onKeyDown={(event) => {
              if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                toggle();
              }
            }}
          >
            <rect width={NODE_WIDTH} height={NODE_HEIGHT} rx="6" />
            <use href={`#i-${node.kind === "Service" ? "service" : node.kind === "Database" ? "database" : node.kind === "Queue" ? "queue" : "external"}`} class="node-icon" x={NODE_WIDTH - 28} y="8" width="18" height="18" aria-hidden="true" />
            <text x="10" y="22" class="node-name">{fit(node.name, 24)}</text>
            <text x="10" y="40" class="node-kind">{KIND_TEXT[node.kind] + health}</text>
          </g>
        );
      })}
    </svg>
  );
}
