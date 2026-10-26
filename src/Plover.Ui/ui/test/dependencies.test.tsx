import { act } from "preact/test-utils";
import type { TopologyGraph } from "../src/api/contracts";
import { ARROW_LENGTH, NODE_HEIGHT, NODE_WIDTH, edgeKey, layoutGraph } from "../src/views/dependencies/graphLayout";
import { all, mount, overview, scope } from "./harness";

const evil = "<img src=x onerror=window.__pwned=1>";

const graph: TopologyGraph = {
  nodes: [
    { id: "service:shop/orders", name: "orders", kind: "Service", scope: "shop", health: "Degraded" },
    { id: "service:shop/payments", name: "payments", kind: "Service", scope: "shop", health: "Healthy" },
    { id: "database:mssql://db1/orders", name: "mssql://db1/orders", kind: "Database", scope: null, health: null },
    { id: `external:${evil}`, name: evil, kind: "External", scope: null, health: null },
  ],
  edges: [
    { from: "service:shop/orders", to: "service:shop/payments", type: "HTTP", calls: 100, errors: 4, durationSumMs: 5000 },
    { from: "service:shop/payments", to: "database:mssql://db1/orders", type: "DB", calls: 90, errors: 0, durationSumMs: 900 },
    { from: "service:shop/orders", to: `external:${evil}`, type: "HTTP", calls: 3, errors: 0, durationSumMs: 30 },
  ],
};

async function openDependencies(topology: TopologyGraph = graph) {
  const page = mount("/#/dependencies", (services) => {
    services.topology = topology;
  });
  page.services.send(overview([scope("shop", "Healthy")], []));
  await page.settle();
  return page;
}

const node = (page: { main: HTMLElement }, name: string) =>
  all(page.main, "svg.graph g.node").find((n) => n.getAttribute("aria-label")!.startsWith(name))!;

describe("the dependencies view", () => {
  it("shows the kind and the health of each node in words", async () => {
    const page = await openDependencies();
    const labels = all(page.main, "svg.graph g.node").map((n) => n.getAttribute("aria-label"));
    expect(labels).toContain("orders, Service — Degraded");
    expect(labels).toContain("payments, Service — Healthy");
    expect(labels).toContain("mssql://db1/orders, Database");
  });

  it("draws one edge per relation and says in words which ones fail", async () => {
    const page = await openDependencies();
    expect(all(page.main, "svg.graph g.edge")).toHaveLength(3);
    expect(all(page.main, "svg.graph g.edge.failing")).toHaveLength(1);
    expect(page.main.textContent).toContain("4 errors");
  });

  it("lists the same edges as a table with the numbers", async () => {
    const page = await openDependencies();
    const rows = all(page.main, "table.instances tr").slice(1).map((row) => row.textContent!);
    expect(rows.some((row) => ["shop/orders", "shop/payments", "100", "50 ms"].every((part) => row.includes(part)))).toBe(true);
  });

  it("shows names from the graph as text, never as markup", async () => {
    const page = await openDependencies();
    expect(all(page.main, "svg.graph img")).toHaveLength(0);
    expect((window as unknown as { __pwned?: number }).__pwned).toBeUndefined();
    expect(page.main.textContent).toContain("<img src=x");
  });

  it("dims what a selected node has nothing to do with, and brings everything back when it is selected again", async () => {
    const page = await openDependencies();
    act(() => { node(page, "mssql").dispatchEvent(new Event("click", { bubbles: true })); });
    expect(node(page, "mssql").getAttribute("aria-pressed")).toBe("true");
    expect(node(page, "orders").classList.contains("dim")).toBe(true);
    expect(node(page, "payments").classList.contains("dim")).toBe(false);
    act(() => { node(page, "mssql").dispatchEvent(new Event("click", { bubbles: true })); });
    expect(all(page.main, "svg.graph .dim")).toHaveLength(0);
  });

  it("lets a node be selected with the keyboard", async () => {
    const page = await openDependencies();
    const first = all(page.main, "svg.graph g.node")[0]!;
    expect(first.getAttribute("tabindex")).toBe("0");
    act(() => { first.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true })); });
    expect(all(page.main, "svg.graph g.node[aria-pressed='true']")).toHaveLength(1);
  });

  it("says so when no calls have been reported", async () => {
    const page = await openDependencies({ nodes: [], edges: [] });
    expect(page.main.textContent).toContain("No calls between services");
  });

  it("marks the current view in the navigation", async () => {
    await openDependencies();
    const current = [...document.querySelectorAll("nav a[aria-current='page']")].map((a) => a.textContent);
    expect(current).toEqual(["Dependencies"]);
  });
});

describe("the layout of the graph", () => {
  it("puts callers to the left of what they call", () => {
    const { positions } = layoutGraph(graph);
    const x = (id: string) => positions.get(id)!.x;
    expect(x("service:shop/orders")).toBeLessThan(x("service:shop/payments"));
    expect(x("service:shop/payments")).toBeLessThan(x("database:mssql://db1/orders"));
  });

  it("gives every node its own place", () => {
    const { positions } = layoutGraph(graph);
    const places = new Set([...positions.values()].map((p) => `${p.x},${p.y}`));
    expect(places.size).toBe(graph.nodes.length);
  });

  it("does not loop for ever when two services call each other", () => {
    const cycle: TopologyGraph = {
      nodes: [
        { id: "a", name: "a", kind: "Service", scope: "s", health: "Healthy" },
        { id: "b", name: "b", kind: "Service", scope: "s", health: "Healthy" },
      ],
      edges: [
        { from: "a", to: "b", type: "HTTP", calls: 1, errors: 0, durationSumMs: 1 },
        { from: "b", to: "a", type: "HTTP", calls: 1, errors: 0, durationSumMs: 1 },
      ],
    };
    expect(layoutGraph(cycle).positions.size).toBe(2);
  });

  it("stays where it is when only the numbers change", () => {
    const changed: TopologyGraph = { ...graph, edges: graph.edges.map((edge) => ({ ...edge, calls: edge.calls + 10 })) };
    expect([...layoutGraph(changed).positions]).toEqual([...layoutGraph(graph).positions]);
  });

  const call = (from: string, to: string) => ({ from, to, type: "HTTP", calls: 1, errors: 0, durationSumMs: 1 });
  const node = (id: string) => ({ id, name: id, kind: "Service" as const, scope: "s", health: "Healthy" as const });

  it("ends each line a head's length before the node, so the tip touches it", () => {
    const { positions, routes } = layoutGraph(graph);
    const edge = graph.edges[0]!;
    const end = routes.get(edgeKey(edge))!.at(-1)!;
    expect(end.x + ARROW_LENGTH).toBe(positions.get(edge.to)!.x);
  });

  it("takes a line that skips a column around the nodes in between", () => {
    // a calls b and c, b calls c: a to c would run straight through b.
    const skip: TopologyGraph = { nodes: [node("a"), node("b"), node("c")], edges: [call("a", "b"), call("b", "c"), call("a", "c")] };
    const { positions, routes } = layoutGraph(skip);
    const b = positions.get("b")!;
    const through = routes.get(edgeKey(call("a", "c")))!.filter((point) => point.x > b.x && point.x < b.x + NODE_WIDTH + 1);
    expect(through.length).toBeGreaterThan(0);
    for (const point of through) expect(Math.abs(point.y - (b.y + NODE_HEIGHT / 2))).toBeGreaterThan(NODE_HEIGHT);
  });

  it("keeps lines that enter one node apart", () => {
    const merge: TopologyGraph = { nodes: [node("a"), node("b"), node("c")], edges: [call("a", "c"), call("b", "c")] };
    const { routes } = layoutGraph(merge);
    const ends = merge.edges.map((edge) => routes.get(edgeKey(edge))!.at(-1)!.y);
    expect(new Set(ends).size).toBe(2);
  });
});
