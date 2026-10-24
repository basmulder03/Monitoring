import { act } from "preact/test-utils";
import { all, mount, overview, scope } from "./harness";

describe("the overview", () => {
  it("connects to the live stream", () => {
    const { services } = mount();
    expect(services.streams).toBe(1);
  });

  it("shows a message before any scope has reported", () => {
    const { services, main } = mount();
    services.send(overview([]));
    expect(main.textContent).toContain("No scope has reported yet.");
  });

  it("groups scopes by environment and then datacenter, unhealthiest group first", () => {
    const { services, main } = mount();
    services.send(overview([
      scope("alpha", "Healthy", { environment: "prod", datacenter: "a" }),
      scope("bravo", "Unhealthy", { environment: "test", datacenter: "b" }),
      scope("charlie", "Healthy", { environment: "prod", datacenter: "b" }),
    ]));
    const text = main.textContent!;
    expect(text.indexOf("environment: test")).toBeLessThan(text.indexOf("environment: prod"));
    expect(text).toContain("datacenter: a");
    expect(text).toContain("datacenter: b");
    expect(main.querySelector("h3")).not.toBeNull();
  });

  it("puts scopes without the label in a group called 'not set', last", () => {
    const { services, main } = mount();
    services.send(overview([scope("alpha", "Healthy", { environment: "prod" }), scope("loose", "Unhealthy")], ["environment"]));
    const text = main.textContent!;
    expect(text).toContain("environment: not set");
    expect(text.indexOf("environment: prod")).toBeLessThan(text.indexOf("environment: not set"));
  });

  it("shows flat tiles when grouping is off", () => {
    const { services, main } = mount();
    services.send(overview([scope("alpha", "Healthy"), scope("bravo", "Degraded")], []));
    expect(all(main, "a.tile")).toHaveLength(2);
    expect(all(main, "h3, h4, h5, h6")).toHaveLength(0);
  });

  it("shows each status as an icon and a word", () => {
    const { services, main } = mount();
    services.send(overview([scope("alpha", "Degraded")], []));
    const status = main.querySelector(".status")!;
    expect(status.textContent).toContain("Degraded");
    expect(status.querySelectorAll("svg use")).toHaveLength(1);
    expect(status.querySelector("use")!.getAttribute("href")).toBe("#i-degraded");
  });

  it("narrows the scopes to the filter in the address", () => {
    const { services, main } = mount("/?filter=environment:prod");
    services.send(overview([scope("alpha", "Healthy", { environment: "prod" }), scope("bravo", "Healthy", { environment: "test" })], ["environment"]));
    expect(main.textContent).toContain("alpha");
    expect(main.textContent).not.toContain("bravo");
  });

  it("says so when the filter matches nothing", () => {
    const { services, main } = mount("/?filter=environment:nope");
    services.send(overview([scope("alpha", "Healthy", { environment: "prod" })], ["environment"]));
    expect(main.textContent).toContain("No scope matches the filter.");
  });

  it("offers a choice of the outermost label when there are several values", () => {
    const { services, main } = mount();
    services.send(overview([scope("alpha", "Healthy", { environment: "prod" }), scope("bravo", "Healthy", { environment: "test" })], ["environment"]));
    const select = main.querySelector("select")!;
    expect([...select.options].map((option) => option.textContent)).toEqual(["All", "prod", "test"]);
    expect(main.querySelector("label")!.htmlFor).toBe(select.id);
  });

  it("narrows the tiles and the address when the selector is switched", () => {
    const { services, main } = mount();
    services.send(overview([scope("alpha", "Healthy", { environment: "prod" }), scope("bravo", "Healthy", { environment: "test" })], ["environment"]));
    const select = main.querySelector("select")!;
    act(() => {
      select.value = "test";
      select.dispatchEvent(new Event("change", { bubbles: true }));
    });
    expect(all(main, "a.tile").map((tile) => tile.textContent!.includes("bravo"))).toEqual([true]);
    expect(window.location.search).toContain("filter=environment%3Atest");
    expect(main.querySelector("select")!.value).toBe("test");
  });

  it("offers no choice when there is only one value", () => {
    const { services, main } = mount();
    services.send(overview([scope("alpha", "Healthy", { environment: "prod" })], ["environment"]));
    expect(main.querySelectorAll("select")).toHaveLength(0);
  });

  it("shows names from agents as text, never as markup", () => {
    const evil = '<img src=x onerror="alert(1)">';
    const { services, main } = mount();
    services.send(overview([scope(evil, "Healthy", { environment: evil })], ["environment"]));
    expect(main.textContent).toContain(evil);
    expect(main.querySelectorAll("img")).toHaveLength(0);
  });

  it("links a tile to the scope view with an escaped name", () => {
    const { services, main } = mount();
    services.send(overview([scope("orders platform", "Healthy")], []));
    expect(main.querySelector("a.tile")!.getAttribute("href")).toBe("#/scopes/orders%20platform");
  });

  it("marks a tile whose agent is not connected, in words", () => {
    const { services, main } = mount();
    const gone = scope("alpha", "Unknown", {}, { agentConnected: false, lastSeen: new Date(services.time - 300000).toISOString() });
    services.send(overview([gone], []));
    expect(main.textContent).toContain("Agent not connected, last heard 5 min ago");
    expect(main.textContent).toContain("Unknown");
  });

  it("says on the tile when the configuration of a scope is not yet up to date", () => {
    const { services, main } = mount();
    services.send(overview([scope("alpha", "Healthy", {}, { configurationInSync: false })], []));
    expect(main.textContent).toContain("Configuration not yet up to date");
  });

  it("says nothing about the agent of a scope whose agent is connected", () => {
    const { services, main } = mount();
    services.send(overview([scope("alpha", "Healthy")], []));
    expect(main.textContent).not.toContain("not connected");
  });
});

describe("alerts", () => {
  it("lists the alerts that are firing", () => {
    const { services, main } = mount();
    const since = new Date(services.time - 90000).toISOString();
    services.send(overview([scope("alpha", "Healthy")], [], [{ key: "k", rule: "service-down", subject: "shop/orders", severity: "Critical", since, acknowledged: null }]));
    const text = main.textContent!;
    for (const expected of ["shop/orders", "Critical", "service-down", "for 1 min"]) expect(text).toContain(expected);
  });

  it("says when no alert is firing", () => {
    const { services, main } = mount();
    services.send(overview([scope("alpha", "Healthy")], []));
    expect(main.textContent).toContain("No alerts are firing.");
  });
});

describe("announcements and old data", () => {
  it("announces a scope whose health changed", () => {
    const { services } = mount();
    services.send(overview([scope("alpha", "Healthy")], []));
    services.send(overview([scope("alpha", "Unhealthy")], []));
    expect(document.getElementById("announcer")!.textContent).toBe("alpha is now Unhealthy");
  });

  it("announces nothing for the first overview", () => {
    const { services } = mount();
    services.send(overview([scope("alpha", "Unhealthy")], []));
    expect(document.getElementById("announcer")!.textContent).toBe("");
  });

  it("is not stale after a keep-alive", () => {
    const { services } = mount();
    services.send(overview([], []));
    services.keepAlive();
    expect(document.getElementById("stale")!.hidden).toBe(true);
  });

  it("warns when the data is old", () => {
    const { services } = mount();
    services.send(overview([], []));
    services.advance(20000);
    const banner = document.getElementById("stale")!;
    expect(banner.hidden).toBe(false);
    expect(banner.textContent).toMatch(/The data is \d+ seconds old/);
  });

  it("stops warning once something is heard again", () => {
    const { services } = mount();
    services.send(overview([], []));
    services.advance(20000);
    services.keepAlive();
    expect(document.getElementById("stale")!.hidden).toBe(true);
  });
});

describe("themes and the wall display", () => {
  it("switches between the light and the dark theme with the button", () => {
    mount();
    const button = document.getElementById("theme-toggle")!;
    act(() => button.click());
    expect(document.documentElement.dataset["theme"]).toBe("dark");
    act(() => button.click());
    expect(document.documentElement.dataset["theme"]).toBe("light");
  });

  it("lets the address force the theme", () => {
    mount("/?theme=dark");
    expect(document.documentElement.dataset["theme"]).toBe("dark");
  });

  it("remembers the chosen theme", () => {
    const { services } = mount();
    act(() => document.getElementById("theme-toggle")!.click());
    expect(services.recall("theme")).toBe("dark");
  });

  it("uses the remembered theme", () => {
    mount("/", (services) => services.remember("theme", "dark"));
    expect(document.documentElement.dataset["theme"]).toBe("dark");
  });
});
