import type { NodeKind, TopologyEdge, TopologyGraph } from "../../api/contracts";
import { plural } from "../../format";

export const KIND_TEXT: Record<NodeKind, string> = {
  Service: "Service",
  Database: "Database",
  Queue: "Queue",
  External: "External system",
};

export function nameOf(id: string, graph: TopologyGraph): string {
  const node = graph.nodes.find((candidate) => candidate.id === id);
  return node ? (node.scope ? `${node.scope}/${node.name}` : node.name) : id;
}

export function averageMs(edge: TopologyEdge): number {
  return edge.calls > 0 ? Math.round(edge.durationSumMs / edge.calls) : 0;
}

export function edgeText(edge: TopologyEdge, graph: TopologyGraph): string {
  return `${nameOf(edge.from, graph)} → ${nameOf(edge.to, graph)} (${edge.type}): ${plural(edge.calls, "call", "calls")}, ${plural(edge.errors, "error", "errors")}, ${averageMs(edge)} ms on average`;
}

export function fit(text: string, max: number): string {
  return text.length <= max ? text : `${text.slice(0, max - 1)}…`;
}
