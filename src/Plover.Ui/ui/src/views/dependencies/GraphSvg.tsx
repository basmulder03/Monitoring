import { useMemo } from "preact/hooks";
import type { TopologyGraph } from "../../api/contracts";
import { HEALTH_LABELS, plural } from "../../format";
import { ARROW_LENGTH, NODE_HEIGHT, NODE_WIDTH, edgeKey, layoutGraph, shapeOf, type Point } from "./graphLayout";
import { KIND_TEXT, edgeText, fit } from "./text";

interface GraphSvgProps {
  graph: TopologyGraph;
  selected: string | null;
  onSelect(id: string | null): void;
}

/** A smooth curve through the points, horizontal where it leaves and enters each of them. */
function curve(points: readonly Point[]): string {
  return points
    .slice(1)
    .map((to, i) => {
      const from = points[i]!;
      const bend = (to.x - from.x) / 2;
      return `C${from.x + bend} ${from.y} ${to.x - bend} ${to.y} ${to.x} ${to.y}`;
    })
    .reduce((path, part) => `${path} ${part}`, `M${points[0]!.x} ${points[0]!.y}`);
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
        {/* The same size whatever the line is thick; the line ends where the head starts (refX 0), so it meets the middle of the back of the head. One head per colour, since a head does not take the colour of its line. */}
        {["arrow", "arrow-failing"].map((id) => (
          <marker key={id} id={id} viewBox={`0 0 ${ARROW_LENGTH} 10`} refX="0" refY="5" markerUnits="userSpaceOnUse" markerWidth={ARROW_LENGTH} markerHeight="10" orient="auto">
            <path d={`M0 0 L${ARROW_LENGTH} 5 L0 10 z`} class={id} />
          </marker>
        ))}
      </defs>

      {graph.edges.map((edge) => {
        const points = layout.routes.get(edgeKey(edge));
        if (!points) return null;
        const middle = [points[Math.floor((points.length - 1) / 2)]!, points[Math.ceil((points.length - 1) / 2)]!];
        const failing = edge.errors > 0;
        const dim = selected !== null && edge.from !== selected && edge.to !== selected;
        return (
          <g key={`${edge.from}>${edge.to}>${edge.type}`} class={`edge${failing ? " failing" : ""}${dim ? " dim" : ""}`}>
            <path
              d={curve(points)}
              stroke-width={1.5 + (2.5 * Math.log10(1 + edge.calls)) / Math.log10(1 + maxCalls)}
              marker-end={`url(#${failing ? "arrow-failing" : "arrow"})`}
              fill="none"
            >
              <title>{edgeText(edge, graph)}</title>
            </path>
            {failing && (
              <text x={(middle[0]!.x + middle[1]!.x) / 2} y={(middle[0]!.y + middle[1]!.y) / 2 - 8} class="edge-label" text-anchor="middle">
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
            <text x="10" y="22" class="node-name">{fit(node.name, 19)}</text>
            <text x="10" y="40" class="node-kind">{fit(KIND_TEXT[node.kind] + (node.scope ? ` · ${node.scope}` : "") + (node.health && node.health !== "Healthy" ? health : ""), 30)}</text>
          </g>
        );
      })}
    </svg>
  );
}
