import { act } from "preact/test-utils";
import type { AlertSummary } from "../src/api/contracts";
import { all, mount, overview, scope } from "./harness";

const alert = (extra: Partial<AlertSummary> = {}): AlertSummary => ({
  key: "service-down:shop/orders",
  rule: "service-down",
  subject: "shop/orders",
  severity: "Critical",
  since: new Date(Date.parse("2026-10-21T12:00:00Z") - 90000).toISOString(),
  acknowledged: null,
  ...extra,
});

const operator = { authenticationEnabled: true, name: "ann", role: "operator" };
const buttons = (main: HTMLElement) => all(main, "button").map((b) => b.textContent);

describe("changing alerts", () => {
  it("shows no buttons to a viewer, nor on a dashboard that needs no sign-in", async () => {
    for (const me of [{ authenticationEnabled: true, name: "bob", role: "viewer" }, { authenticationEnabled: false, name: "", role: "operator" }, null]) {
      const { services, main, settle } = mount("/", (s) => (s.me = me));
      services.send(overview([scope("alpha", "Healthy")], [], [alert()]));
      await settle();
      expect(buttons(main)).not.toContain("Acknowledge");
      expect(buttons(main).join()).not.toContain("Silence");
    }
  });

  it("lets an operator acknowledge an alert, and says who did", async () => {
    const { services, main, settle } = mount("/", (s) => (s.me = operator));
    services.send(overview([scope("alpha", "Healthy")], [], [alert()]));
    await settle();

    await act(async () => all(main, "button").find((b) => b.textContent === "Acknowledge")!.click());
    expect(services.changes).toEqual([{ method: "POST", path: "/api/alerts/acknowledge", body: { key: "service-down:shop/orders" } }]);

    services.send(overview([scope("alpha", "Healthy")], [], [alert({ acknowledged: { key: "service-down:shop/orders", by: "ann", at: new Date(services.time - 30000).toISOString(), note: "restarting it", removed: false } })]));
    expect(main.textContent).toContain("Acknowledged by ann, 30 s ago: restarting it");
    expect(buttons(main)).toContain("Take back acknowledgement");
  });

  it("takes an acknowledgement back", async () => {
    const { services, main, settle } = mount("/", (s) => (s.me = operator));
    services.send(overview([scope("alpha", "Healthy")], [], [alert({ acknowledged: { key: "service-down:shop/orders", by: "ann", at: new Date(services.time).toISOString(), note: null, removed: false } })]));
    await settle();

    await act(async () => all(main, "button").find((b) => b.textContent === "Take back acknowledgement")!.click());

    expect(services.changes).toEqual([{ method: "DELETE", path: "/api/alerts/acknowledge?key=service-down%3Ashop%2Forders", body: undefined }]);
  });

  it("silences an alert for the time and for the reason that are asked", async () => {
    const { services, main, settle } = mount("/", (s) => (s.me = operator));
    services.send(overview([scope("alpha", "Healthy")], [], [alert()]));
    await settle();
    await act(async () => all(main, "button").find((b) => b.textContent === "Silence…")!.click());

    const select = main.querySelector<HTMLSelectElement>(".silence-form select")!;
    const reason = main.querySelector<HTMLInputElement>(".silence-form input")!;
    await act(async () => {
      select.value = "240";
      select.dispatchEvent(new Event("change", { bubbles: true }));
      reason.value = "  planned maintenance ";
      reason.dispatchEvent(new Event("input", { bubbles: true }));
    });
    await act(async () => {
      main.querySelector<HTMLFormElement>(".silence-form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
    });
    await settle();

    expect(services.changes).toEqual([{ method: "POST", path: "/api/silences", body: { rule: "service-down", subjectPrefix: "shop/orders", minutes: 240, reason: "planned maintenance" } }]);
    expect(main.querySelector(".silence-form")).toBeNull();
  });

  it("says why when the controller refuses, and keeps the form", async () => {
    const { services, main, settle } = mount("/", (s) => {
      s.me = operator;
      s.changeAnswer = { status: 403, error: "Only an operator can change alerts." };
    });
    services.send(overview([scope("alpha", "Healthy")], [], [alert()]));
    await settle();
    await act(async () => all(main, "button").find((b) => b.textContent === "Silence…")!.click());

    await act(async () => {
      main.querySelector<HTMLFormElement>(".silence-form")!.dispatchEvent(new Event("submit", { bubbles: true, cancelable: true }));
    });
    await settle();

    expect(main.querySelector("[role=alert]")!.textContent).toBe("Only an operator can change alerts.");
    expect(main.querySelector(".silence-form")).not.toBeNull();
  });

  it("lists the silences in force, and an operator can take one back", async () => {
    const { services, main, settle } = mount("/", (s) => {
      s.me = operator;
      s.silences = [{ id: "abc", rule: "*", subjectPrefix: "shop/", until: new Date(Date.parse("2026-10-21T14:00:00Z")).toISOString(), reason: "maintenance", by: "ann", updatedAt: "2026-10-21T12:00:00Z", removed: false }];
    });
    services.send(overview([scope("alpha", "Healthy")], [], []));
    await settle();

    expect(main.textContent).toContain("Silences in force");
    expect(main.textContent).toContain("Every rule, subjects starting with shop/");
    expect(main.textContent).toContain("by ann: maintenance");
    await act(async () => all(main, "button").find((b) => b.textContent === "Take back")!.click());
    expect(services.changes).toEqual([{ method: "DELETE", path: "/api/silences/abc", body: undefined }]);
  });

  it("lists what fired lately, with how long it lasted", async () => {
    const { services, main, settle } = mount("/", (s) => {
      const hour = 3600_000;
      s.history = [
        { key: "a", rule: "service-down", subject: "shop/orders", severity: "Critical", started: new Date(Date.parse("2026-10-21T12:00:00Z") - 2 * hour).toISOString(), ended: new Date(Date.parse("2026-10-21T12:00:00Z") - 2 * hour + 14 * 60_000).toISOString(), endedBy: "cleared" },
        { key: "b", rule: "restart-loop", subject: "shop/payments-1", severity: "Warning", started: new Date(Date.parse("2026-10-21T12:00:00Z") - 60_000).toISOString(), ended: null, endedBy: null },
        { key: "c", rule: "scope-unknown", subject: "billing", severity: "Warning", started: new Date(Date.parse("2026-10-21T12:00:00Z") - 3 * hour).toISOString(), ended: new Date(Date.parse("2026-10-21T12:00:00Z") - hour).toISOString(), endedBy: "restart" },
      ];
    });
    services.send(overview([scope("alpha", "Healthy")], [], []));
    await settle();

    const text = main.querySelector(".history")!.textContent!;
    expect(text).toContain("shop/orders");
    expect(text).toContain("lasted 14 min");
    expect(text).toContain("still firing");
    expect(text).toContain("ended unknown (the controller restarted)");
  });
});
