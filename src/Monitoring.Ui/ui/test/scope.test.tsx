import { act } from "preact/test-utils";
import type { ContainerInfo, InstanceDetail, ScopeDetail } from "../src/api/contracts";
import { all, mount, overview, scope } from "./harness";

const container = (extra: Partial<ContainerInfo>): ContainerInfo => ({
  name: "orders", role: "App", phase: "Running", health: "Healthy", ready: true, restarts: 0, image: null, imageDigest: null,
  reason: null, message: null, exitCode: null, lastExitCode: null, lastReason: null, ...extra,
});

const instance = (extra: Partial<InstanceDetail> = {}): InstanceDetail => ({
  id: "orders-1", lifecycle: "Running", health: "Healthy", labels: {}, version: null, checks: [], containers: [], metrics: [], ...extra,
});

const detail = (extra: Partial<ScopeDetail> = {}): ScopeDetail => ({
  name: "shop", health: "Healthy", labels: {}, agentConnected: true, lastSeen: null, services: [], ...extra,
});

const withService = (inst: InstanceDetail, health: ScopeDetail["health"] = "Degraded"): ScopeDetail =>
  detail({ health, services: [{ name: "orders", health, liveInstances: 1, healthyInstances: 0, instances: [inst] }] });

async function openScope(scopeDetail: ScopeDetail | null, address = "/#/scopes/shop") {
  const page = mount(address, (services) => {
    if (scopeDetail) services.scopes[scopeDetail.name] = scopeDetail;
  });
  page.services.send(overview([scope("shop", "Healthy")], []));
  await page.settle();
  return page;
}

describe("the scope view", () => {
  it("shows a banner when the agent is not connected, with the last known instances", async () => {
    const gone = withService(instance(), "Unknown");
    const page = await openScope({ ...gone, agentConnected: false, lastSeen: new Date(Date.parse("2026-10-21T12:00:00Z") - 120000).toISOString() });
    expect(page.main.textContent).toContain("The agent of this scope is not connected (last heard 2 min ago)");
    expect(page.main.textContent).toContain("orders-1");
  });

  it("shows no banner when the agent is connected, and shows the labels of the scope", async () => {
    const page = await openScope(detail({ labels: { environment: "prod" } }));
    expect(page.main.textContent).not.toContain("not connected");
    expect(page.main.textContent).toContain("environment=prod");
  });

  it("opens from the overview and returns with the link back", async () => {
    const page = await openScope(detail({ services: [{ name: "orders", health: "Healthy", liveInstances: 0, healthyInstances: 0, instances: [] }] }), "/");
    act(() => { window.location.hash = "#/scopes/shop"; });
    await page.settle();
    expect(page.main.textContent).toContain("orders");
    act(() => { window.location.hash = "#/"; });
    await page.settle();
    expect(all(page.main, "a.tile")).toHaveLength(1);
  });

  it("expands the services that are not healthy", async () => {
    const page = await openScope(detail({
      health: "Degraded",
      services: [
        { name: "fine", health: "Healthy", liveInstances: 1, healthyInstances: 1, instances: [instance()] },
        { name: "broken", health: "Unhealthy", liveInstances: 1, healthyInstances: 0, instances: [instance()] },
      ],
    }));
    const open = all(page.main, "details.service").map((d) => [d.querySelector("strong")!.textContent, (d as HTMLDetailsElement).open]);
    expect(open).toEqual([["fine", false], ["broken", true]]);
  });

  it("says so when the scope in the address is not known", async () => {
    const page = await openScope(null, "/#/scopes/nowhere");
    expect(page.main.textContent).toContain("The scope 'nowhere' is not known.");
  });
});

describe("what an instance says about itself", () => {
  const pod = instance({
    version: "1.2.3",
    health: "Degraded",
    checks: [{ name: "database", health: "Degraded", description: "slow <b>query</b>" }],
    metrics: [{ name: "a", labels: {}, value: 1 }],
    containers: [
      container({ health: "Degraded", ready: false, restarts: 3, image: "orders:1", lastReason: "OOMKilled", lastExitCode: 137 }),
      container({ name: "monitoring-sidecar", role: "MonitoringSidecar", phase: "Waiting", health: "Unhealthy", ready: false, reason: "CrashLoopBackOff" }),
    ],
  });

  it("shows the version, checks and containers", async () => {
    const page = await openScope(withService(pod));
    const text = all(page.main, "details.instance")[0]!.textContent!;
    for (const expected of ["1.2.3", "database", "Monitoring sidecar", "Waiting: CrashLoopBackOff", "3 (last: OOMKilled, exit 137)", "1 metric reported"]) {
      expect(text).toContain(expected);
    }
  });

  it("does not show an exit code for a container that has none", async () => {
    const page = await openScope(withService(instance({ containers: [container({ phase: "Terminated", health: "Unhealthy", reason: "Error" })] })));
    expect(page.main.textContent).toContain("Terminated: Error");
    expect(page.main.textContent).not.toContain("exit null");
  });

  it("shows it as text, never as markup", async () => {
    const page = await openScope(withService(pod));
    expect(all(page.main, "details.instance b")).toHaveLength(0);
    expect(all(page.main, "details.instance")[0]!.textContent).toContain("slow <b>query</b>");
  });

  it("keeps an instance open while the page updates and when coming back to it", async () => {
    const page = await openScope(withService(pod));
    const first = all(page.main, "details.instance")[0] as HTMLDetailsElement;
    act(() => {
      first.open = true;
      first.dispatchEvent(new Event("toggle"));
    });
    act(() => { window.location.hash = "#/"; });
    await page.settle();
    act(() => { window.location.hash = "#/scopes/shop"; });
    await page.settle();
    expect((all(page.main, "details.instance")[0] as HTMLDetailsElement).open).toBe(true);
  });

  it("keeps it open when a new overview arrives", async () => {
    const page = await openScope(withService(pod));
    const first = all(page.main, "details.instance")[0] as HTMLDetailsElement;
    act(() => {
      first.open = true;
      first.dispatchEvent(new Event("toggle"));
    });
    page.services.send(overview([scope("shop", "Degraded")], []));
    await page.settle();
    expect((all(page.main, "details.instance")[0] as HTMLDetailsElement).open).toBe(true);
  });
});
