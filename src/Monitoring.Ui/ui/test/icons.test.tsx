import type { ScopeDetail } from "../src/api/contracts";
import { all, mount, overview, scope } from "./harness";

const detail: ScopeDetail = {
  name: "shop", health: "Healthy", labels: {}, agentConnected: true, lastSeen: null,
  configuration: { applied: null, expected: "v1", inSync: true },
  services: [{
    name: "orders", health: "Healthy", liveInstances: 1, healthyInstances: 1,
    instances: [{ id: "orders-1", lifecycle: "Running", health: "Healthy", labels: {}, version: null, checks: [], containers: [], metrics: [] }],
  }],
};

/** Every icon a page refers to must be in the sprite, or it shows as nothing at all. */
function missingIcons(root: HTMLElement) {
  return all(root, "use")
    .map((use) => use.getAttribute("href")!.slice(1))
    .filter((id) => id.startsWith("i-") && root.ownerDocument.getElementById(id) === null);
}

describe("the icons", () => {
  it("are all defined in the sprite on the overview, with an icon for the environment and the data center", async () => {
    const page = mount("/");
    const prod = { ...scope("shop", "Healthy"), labels: { environment: "prod", datacenter: "a" } };
    page.services.send({ ...overview([prod], []), groupBy: ["environment", "datacenter"] });
    await page.settle();
    expect(all(page.main, "h3 use, h4 use").map((use) => use.getAttribute("href"))).toEqual(expect.arrayContaining(["#i-environment", "#i-datacenter"]));
    expect(missingIcons(document.body)).toEqual([]);
  });

  it("are all defined in the sprite in the scope view, which shows a service and an instance with their own", async () => {
    const page = mount("/#/scopes/shop", (services) => {
      services.scopes.shop = detail;
    });
    page.services.send(overview([scope("shop", "Healthy")], []));
    await page.settle();
    expect(all(page.main, "use").map((use) => use.getAttribute("href"))).toEqual(expect.arrayContaining(["#i-service", "#i-instance"]));
    expect(missingIcons(document.body)).toEqual([]);
  });

  it("are all defined in the sprite in the graph, where each kind of node has its own", async () => {
    const page = mount("/#/dependencies", (services) => {
      services.topology = {
        nodes: [
          { id: "service:shop/orders", name: "orders", kind: "Service", scope: "shop", health: "Healthy" },
          { id: "database:db", name: "db", kind: "Database", scope: null, health: null },
          { id: "queue:q", name: "q", kind: "Queue", scope: null, health: null },
          { id: "external:x", name: "x", kind: "External", scope: null, health: null },
        ],
        edges: [{ from: "service:shop/orders", to: "database:db", type: "DB", calls: 3, errors: 0, durationSumMs: 30 }],
      };
    });
    page.services.send(overview([scope("shop", "Healthy")], []));
    await page.settle();
    const icons = all(page.main, "svg.graph use").map((use) => use.getAttribute("href"));
    expect(icons).toEqual(expect.arrayContaining(["#i-service", "#i-database", "#i-queue", "#i-external"]));
    expect(missingIcons(document.body)).toEqual([]);
  });
});
