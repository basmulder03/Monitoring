import type { TopologyGraph } from "../../api/contracts";

export const NODE_WIDTH = 190;
export const NODE_HEIGHT = 52;
const COLUMN_GAP = 110;
const ROW_GAP = 28;
const MARGIN = 20;

export interface GraphLayout {
  positions: ReadonlyMap<string, { x: number; y: number }>;
  width: number;
  height: number;
}

/**
 * Places each node in a column after everything that calls it, and orders each column by id. The result depends only on
 * which nodes and edges exist, so the picture stays where it is while the numbers change and only moves when the topology does.
 */
export function layoutGraph(graph: TopologyGraph): GraphLayout {
  const rank = new Map<string, number>(graph.nodes.map((node) => [node.id, 0]));

  // Longest path from the callers; the pass limit stops a cycle (two services calling each other) from looping for ever.
  for (let pass = 0; pass < graph.nodes.length; pass++) {
    let changed = false;
    for (const edge of graph.edges) {
      const from = rank.get(edge.from);
      const to = rank.get(edge.to);
      if (edge.from === edge.to || from === undefined || to === undefined) continue;
      if (to < from + 1 && from + 1 < graph.nodes.length) {
        rank.set(edge.to, from + 1);
        changed = true;
      }
    }
    if (!changed) break;
  }

  const columns = new Map<number, string[]>();
  for (const node of graph.nodes) {
    const column = rank.get(node.id) ?? 0;
    columns.set(column, [...(columns.get(column) ?? []), node.id]);
  }

  const positions = new Map<string, { x: number; y: number }>();
  let width = 0;
  let height = 0;
  for (const [column, ids] of [...columns].sort(([a], [b]) => a - b)) {
    ids.sort((a, b) => a.localeCompare(b)).forEach((id, row) => {
      positions.set(id, { x: MARGIN + column * (NODE_WIDTH + COLUMN_GAP), y: MARGIN + row * (NODE_HEIGHT + ROW_GAP) });
    });
    height = Math.max(height, MARGIN * 2 + ids.length * (NODE_HEIGHT + ROW_GAP) - ROW_GAP);
    width = MARGIN * 2 + (column + 1) * (NODE_WIDTH + COLUMN_GAP) - COLUMN_GAP;
  }

  return { positions, width, height };
}

/** A key that changes only when the shape of the graph does, so a layout can be kept while the numbers change. */
export function shapeOf(graph: TopologyGraph): string {
  return `${graph.nodes.map((node) => node.id).join("|")}#${graph.edges.map((edge) => `${edge.from}>${edge.to}`).join("|")}`;
}
