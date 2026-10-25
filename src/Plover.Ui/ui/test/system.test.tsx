import { act } from "preact/test-utils";
import type { SystemStatus } from "../src/api/contracts";
import { all, mount, overview, scope } from "./harness";

const status = (extra: Partial<SystemStatus> = {}): SystemStatus => ({
  replica: "plover-controller-0", uptimeSeconds: 600, scopes: 2, agentsConnected: 2, eventClients: 1, alertEvaluationMilliseconds: 3.2,
  memoryBytes: 41943040, healthy: true, replicas: [], unmonitored: [], framesPerSecond: 12.5, instances: 40, series: 1200, ...extra,
});

async function openSystem(data: SystemStatus | null) {
  const page = mount("/#/system", (services) => {
    services.system = data;
  });
  page.services.send(overview([scope("shop", "Healthy")], []));
  await page.settle();
  return page;
}

describe("the system view", () => {
  it("shows how busy the agents keep the replica and what it holds", async () => {
    const page = await openSystem(status());

    const text = page.main.textContent ?? "";
    expect(text).toContain("12.5 per second");
    expect(text).toContain("40 instances, 1200 metric series");
  });

  it("shows each replica with how many agents are connected and how far behind it is, and marks the one that answers", async () => {
    const page = await openSystem(status({
      replicas: [
        { address: "plover-controller-0.monitoring:9440", thisReplica: true, agentsReporting: 2, agentsConnected: 2, maxLag: 0 },
        { address: "plover-controller-1.monitoring:9440", thisReplica: false, agentsReporting: 2, agentsConnected: 1, maxLag: 12 },
      ],
      healthy: false,
    }));
    const rows = all(page.main, "table.instances")[1]!.querySelectorAll("tbody tr");
    expect(rows).toHaveLength(2);
    expect(rows[0]!.textContent).toContain("(this one)");
    expect(rows[1]!.textContent).toContain("1 of 2");
    expect(rows[1]!.textContent).toContain("12 changes");
    expect(page.main.textContent).toContain("Some agents are not connected to every controller replica.");
    expect(all(page.main, "td.violation")).toHaveLength(1);
  });

  it("lists the pods that run without a sidecar, each linking to its scope", async () => {
    const page = await openSystem(status({ unmonitored: [{ scope: "shop", service: "orders", instance: "orders-1" }] }));
    const link = page.main.querySelector("table.instances a") as HTMLAnchorElement;
    expect(link.getAttribute("href")).toBe("#/scopes/shop");
    expect(page.main.textContent).toContain("orders-1");
    expect(page.main.textContent).not.toContain("Every pod has one.");
  });

  it("says so when there is nothing to list", async () => {
    const page = await openSystem(status());
    expect(page.main.textContent).toContain("No agent has said which replicas it connects to yet.");
    expect(page.main.textContent).toContain("Every pod has one.");
    expect(page.main.textContent).toContain("Every scope has an agent connected to this controller, and every agent to every replica.");
  });

  it("names the scopes whose agent is not connected before anything else", async () => {
    const page = await openSystem(status({ agentsConnected: 1, healthy: false }));
    expect(page.main.textContent).toContain("1 scope has no agent connected to this controller.");
  });

  it("shows a tile on the overview that says how the monitoring system is and leads to the details", async () => {
    const page = mount("/", (services) => {
      services.system = status({ healthy: false, scopes: 3, agentsConnected: 2, replicas: [{ address: "c0:9440", thisReplica: true, agentsReporting: 3, agentsConnected: 2, maxLag: 0 }], unmonitored: [{ scope: "shop", service: "orders", instance: "orders-1" }] });
    });
    page.services.send(overview([scope("shop", "Healthy")], []));
    await page.settle();
    const tile = page.main.querySelector("a.system-tile") as HTMLAnchorElement;
    expect(tile.getAttribute("href")).toBe("#/system");
    expect(tile.classList.contains("degraded")).toBe(true);
    expect(all(page.main, "a.tile")).toHaveLength(1);
    expect(tile.textContent).toContain("1 scope has no agent connected to this controller");
    expect(tile.textContent).toContain("1 replica is not reached by every agent");
    expect(tile.textContent).toContain("1 pod runs without a sidecar");
  });

  it("shows no tile when the controller cannot say how the system is", async () => {
    const page = mount("/");
    page.services.send(overview([scope("shop", "Healthy")], []));
    await page.settle();
    expect(page.main.textContent).not.toContain("Plover system");
  });

  it("moves focus to the main content when another view is opened, and leaves it alone otherwise", async () => {
    const page = mount("/");
    page.services.send(overview([scope("shop", "Healthy")], []));
    await page.settle();
    (document.querySelector("a#home") as HTMLElement).focus();
    expect(document.activeElement?.id).toBe("home");

    act(() => { window.location.hash = "#/system"; });
    await page.settle();
    expect(document.activeElement?.id).toBe("app");
  });
});
