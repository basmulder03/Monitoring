import type { TopologyGraph } from "../../api/contracts";

export const NODE_WIDTH = 210;
export const NODE_HEIGHT = 52;
/** How far the line stops short of the node: the arrow head fills the rest, so the line meets the middle of its back and the tip touches the node. */
export const ARROW_LENGTH = 11;
const COLUMN_GAP = 130;
const ROW_GAP = 28;
const MARGIN = 20;
/** Distance between lines that leave or enter the same side of a node. */
const PORT_STEP = 9;
const SWEEPS = 4;

export interface Point {
  x: number;
  y: number;
}

export interface GraphLayout {
  positions: ReadonlyMap<string, Point>;
  /** The points each edge passes, by <see cref="edgeKey"/>; the curve goes through them with a horizontal tangent. */
  routes: ReadonlyMap<string, Point[]>;
  width: number;
  height: number;
}

type EdgeLike = { from: string; to: string; type: string };

export const edgeKey = (edge: EdgeLike): string => `${edge.from}>${edge.to}>${edge.type}`;

/**
 * Places each node in a column after everything that calls it. An edge that skips columns passes through an empty slot in each column it crosses,
 * so it goes around the nodes in between, and the slots are ordered so that as few lines as possible cross. The result depends only on
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

  // Each edge as a chain of slots, one per column it crosses; a slot that is not a node is a hole the line passes through.
  const chains = new Map<string, string[]>();
  const columns = new Map<number, string[]>();
  for (const node of graph.nodes) {
    const column = rank.get(node.id) ?? 0;
    columns.set(column, [...(columns.get(column) ?? []), node.id]);
  }
  for (const edge of graph.edges) {
    const from = rank.get(edge.from);
    const to = rank.get(edge.to);
    if (edge.from === edge.to || from === undefined || to === undefined) continue;
    const key = edgeKey(edge);
    const chain = [edge.from];
    for (let column = from + 1; column < to; column++) {
      const hole = `\u0000${key}#${column}`;
      chain.push(hole);
      columns.set(column, [...(columns.get(column) ?? []), hole]);
    }
    chain.push(edge.to);
    chains.set(key, chain);
  }

  const last = Math.max(0, ...columns.keys());
  for (const ids of columns.values()) ids.sort((a, b) => a.localeCompare(b));

  const before = new Map<string, string[]>();
  const after = new Map<string, string[]>();
  for (const chain of chains.values()) {
    for (let i = 1; i < chain.length; i++) {
      const [from, to] = [chain[i - 1]!, chain[i]!];
      if ((rank.get(to) ?? columnOf(columns, to)) <= (rank.get(from) ?? columnOf(columns, from))) continue;
      before.set(to, [...(before.get(to) ?? []), from]);
      after.set(from, [...(after.get(from) ?? []), to]);
    }
  }

  // Barycentre ordering: each slot moves towards the average place of the slots it connects to; the sort is stable, so slots with nothing to follow stay.
  const order = (column: number, neighbours: Map<string, string[]>, towards: number) => {
    const ids = columns.get(column);
    const other = columns.get(towards);
    if (!ids || !other) return;
    const index = (id: string) => other.indexOf(id);
    const centre = (id: string, own: number) => {
      const found = (neighbours.get(id) ?? []).map(index).filter((i) => i >= 0);
      return found.length === 0 ? own : found.reduce((a, b) => a + b, 0) / found.length;
    };
    const scored = ids.map((id, own) => ({ id, score: centre(id, own) }));
    scored.sort((a, b) => a.score - b.score);
    columns.set(column, scored.map((s) => s.id));
  };
  for (let sweep = 0; sweep < SWEEPS; sweep++) {
    for (let column = 1; column <= last; column++) order(column, before, column - 1);
    for (let column = last - 1; column >= 0; column--) order(column, after, column + 1);
  }

  const pitch = NODE_HEIGHT + ROW_GAP;
  const tallest = Math.max(0, ...[...columns.values()].map((ids) => ids.length * pitch - ROW_GAP));
  const height = Math.max(0, tallest) + MARGIN * 2;
  const slots = new Map<string, Point>();
  for (const [column, ids] of columns) {
    const top = MARGIN + (tallest - (ids.length * pitch - ROW_GAP)) / 2;
    ids.forEach((id, row) => slots.set(id, { x: MARGIN + column * (NODE_WIDTH + COLUMN_GAP), y: top + row * pitch }));
  }
  const positions = new Map<string, Point>(graph.nodes.flatMap((node) => (slots.has(node.id) ? [[node.id, slots.get(node.id)!] as const] : [])));
  const width = columns.size === 0 ? 0 : MARGIN * 2 + (last + 1) * (NODE_WIDTH + COLUMN_GAP) - COLUMN_GAP;

  const centreY = (slot: Point) => slot.y + NODE_HEIGHT / 2;
  const routes = new Map<string, Point[]>();
  for (const edge of graph.edges) {
    const key = edgeKey(edge);
    const from = positions.get(edge.from);
    const to = positions.get(edge.to);
    if (edge.from === edge.to || !from || !to) continue;
    const points: Point[] = [{ x: from.x + NODE_WIDTH, y: centreY(from) }];
    for (const hole of (chains.get(key) ?? []).slice(1, -1)) {
      const slot = slots.get(hole)!;
      points.push({ x: slot.x, y: centreY(slot) }, { x: slot.x + NODE_WIDTH, y: centreY(slot) });
    }
    points.push({ x: to.x - ARROW_LENGTH, y: centreY(to) });
    routes.set(key, points);
  }

  // Lines that leave or enter the same side of a node are spread along it, the one going up on top, so they neither merge nor cross at the node.
  spread(graph, routes, (edge) => edge.from, (points) => [points[0]!, points[1]!]);
  spread(graph, routes, (edge) => edge.to, (points) => [points.at(-1)!, points.at(-2)!]);

  return { positions, routes, width, height };
}

function columnOf(columns: Map<number, string[]>, id: string): number {
  for (const [column, ids] of columns) if (ids.includes(id)) return column;
  return 0;
}

/** Moves the end of each route that shares a node side, so the ends are PORT_STEP apart, ordered by where the line goes. */
function spread(graph: TopologyGraph, routes: Map<string, Point[]>, side: (edge: EdgeLike) => string, ends: (points: Point[]) => [Point, Point]) {
  const bySide = new Map<string, { key: string; end: Point; other: Point }[]>();
  for (const edge of graph.edges) {
    const key = edgeKey(edge);
    const points = routes.get(key);
    if (!points) continue;
    const [end, other] = ends(points);
    bySide.set(side(edge), [...(bySide.get(side(edge)) ?? []), { key, end, other }]);
  }
  for (const group of bySide.values()) {
    group.sort((a, b) => a.other.y - b.other.y || a.key.localeCompare(b.key));
    group.forEach(({ end }, i) => {
      end.y += (i - (group.length - 1) / 2) * PORT_STEP;
    });
  }
}

/** A key that changes only when the shape of the graph does, so a layout can be kept while the numbers change. */
export function shapeOf(graph: TopologyGraph): string {
  return `${graph.nodes.map((node) => node.id).join("|")}#${graph.edges.map((edge) => `${edge.from}>${edge.to}`).join("|")}`;
}
