import { act } from "preact/test-utils";
import type { AlertSummary, MetricHistory, Overview, PackageCatalog, ScopeDetail, ScopeSummary, SystemStatus, TopologyGraph, VulnerabilityReport } from "../src/api/contracts";
import type { Services } from "../src/services/Services";
import { start } from "../src/start";

/** The browser as the tests control it: a clock, timers, the live stream and the answers of the controller. */
export class FakeServices implements Services {
  time = Date.parse("2026-10-21T12:00:00Z");
  scopes: Record<string, ScopeDetail> = {};
  topology: TopologyGraph = { nodes: [], edges: [] };
  packages: PackageCatalog = { packages: [], services: 0, servicesWithoutList: 0 };
  vulnerabilities: VulnerabilityReport = { findings: [], advisories: 0, loadedAt: null };
  streams = 0;
  histories: Record<string, MetricHistory> = {};
  system: SystemStatus | null = null;
  historyRequests: string[] = [];
  private handlers: { onOverview(overview: Overview): void; onKeepAlive(): void } | null = null;
  private readonly timers: { period: number; due: number; callback: () => void }[] = [];
  private readonly settings = new Map<string, string>();

  me: import("../src/api/contracts").WhoAmI | null = null;

  async getJson<T>(path: string) {
    if (path === "/api/me") return this.me ? { status: 200, body: this.me as T } : { status: 404, body: null };
    if (path === "/api/system") return this.system ? { status: 200, body: this.system as T } : { status: 404, body: null };
    if (path === "/api/topology") return { status: 200, body: this.topology as T };
    if (path === "/api/packages") return { status: 200, body: this.packages as T };
    if (path === "/api/vulnerabilities") return { status: 200, body: this.vulnerabilities as T };
    if (path.endsWith("/history")) {
      this.historyRequests.push(path);
      const history = this.histories[path];
      return history ? { status: 200, body: history as T } : { status: 204, body: null };
    }

    const scope = this.scopes[decodeURIComponent(path.replace("/api/scopes/", ""))];
    return scope ? { status: 200, body: scope as T } : { status: 404, body: null };
  }

  followOverview(handlers: { onOverview(overview: Overview): void; onKeepAlive(): void }) {
    this.streams++;
    this.handlers = handlers;
    return () => {
      this.handlers = null;
    };
  }

  now = () => this.time;

  every(milliseconds: number, callback: () => void) {
    const timer = { period: milliseconds, due: this.time + milliseconds, callback };
    this.timers.push(timer);
    return () => {
      this.timers.splice(this.timers.indexOf(timer), 1);
    };
  }

  recall = (key: string) => this.settings.get(key) ?? null;

  remember = (key: string, value: string) => {
    this.settings.set(key, value);
  };

  /** Sends an overview over the live stream, as the controller does. */
  send(overview: Overview) {
    act(() => this.handlers!.onOverview(overview));
  }

  keepAlive() {
    act(() => this.handlers!.onKeepAlive());
  }

  /** Lets time pass and runs each timer as often as its period fits in, as the real ones would have. */
  advance(milliseconds: number) {
    const end = this.time + milliseconds;
    act(() => {
      for (;;) {
        const next = this.timers.filter((timer) => timer.due <= end).sort((a, b) => a.due - b.due)[0];
        if (next === undefined) break;
        this.time = next.due;
        next.due += next.period;
        next.callback();
      }

      this.time = end;
    });
  }
}

export interface Mounted {
  services: FakeServices;
  /** The element the page puts its view in. */
  main: HTMLElement;
  /** Waits for what the view loaded from the controller to be shown. */
  settle(): Promise<void>;
}

/** Starts the real page at an address, against fakes. */
export function mount(address = "/", configure: (services: FakeServices) => void = () => {}): Mounted {
  // jsdom has no media queries; a light system is what the tests assume.
  window.matchMedia ??= (() => ({ matches: false })) as unknown as typeof window.matchMedia;
  document.body.className = "";
  document.documentElement.removeAttribute("data-theme");
  window.history.replaceState(null, "", address);
  document.body.innerHTML = '<div id="root"></div>';

  const services = new FakeServices();
  configure(services);
  act(() => start(services, document.getElementById("root")!));

  return {
    services,
    main: document.getElementById("app")!,
    async settle() {
      for (let i = 0; i < 4; i++) await act(() => new Promise<void>((resolve) => setTimeout(resolve, 0)));
    },
  };
}

export function scope(name: string, health: ScopeSummary["health"], labels: Record<string, string> = {}, extra: Partial<ScopeSummary> = {}): ScopeSummary {
  return { name, health, services: 1, instances: 2, problems: health === "Healthy" ? 0 : 1, labels, agentConnected: true, lastSeen: null, configurationInSync: true, ...extra };
}

export function overview(scopes: ScopeSummary[], groupBy: string[] = ["environment", "datacenter"], alerts: AlertSummary[] = []): Overview {
  return { scopes, alerts, groupBy };
}

export const all = (root: ParentNode, selector: string): HTMLElement[] => [...root.querySelectorAll<HTMLElement>(selector)];
