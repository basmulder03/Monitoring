import { hashOf, readWallConfig } from "../src/state/wallConfig";
import { mount, overview, scope } from "./harness";

describe("reading how a wall display behaves from its address", () => {
  it("is not a wall display on the normal page", () => {
    expect(readWallConfig("/", "?rotate=10")).toBeNull();
  });

  it("rotates between the overview and the dependencies every minute by default", () => {
    expect(readWallConfig("/tv", "")).toEqual({ views: ["overview", "dependencies"], rotateSeconds: 60, focusSeconds: 300, drift: false });
  });

  it("reads the views, the times and the drift", () => {
    expect(readWallConfig("/tv", "?rotate=15&views=packages,overview&focus=90&drift=1")).toEqual({
      views: ["packages", "overview"], rotateSeconds: 15, focusSeconds: 90, drift: true,
    });
  });

  it("leaves out views that do not exist and views listed twice", () => {
    expect(readWallConfig("/tv", "?views=overview,nonsense,overview,dependencies")?.views).toEqual(["overview", "dependencies"]);
  });

  it("falls back to the default views when none of the listed ones exist", () => {
    expect(readWallConfig("/tv", "?views=nonsense")?.views).toEqual(["overview", "dependencies"]);
  });

  it.each([["abc"], ["-5"], [""]])("falls back to the default time for %j", (value) => {
    expect(readWallConfig("/tv", `?rotate=${value}`)?.rotateSeconds).toBe(60);
  });

  it("turns rotation off with zero seconds", () => {
    expect(readWallConfig("/tv", "?rotate=0")?.rotateSeconds).toBe(0);
  });

  it("gives every view an address", () => {
    expect([hashOf("overview"), hashOf("dependencies"), hashOf("packages")]).toEqual(["#/", "#/dependencies", "#/packages"]);
  });
});

describe("a wall display", () => {
  it("goes to the next view when a view has been shown for the rotation time, and round again", () => {
    const { services } = mount("/tv?rotate=10&views=overview,dependencies");
    services.send(overview([scope("shop", "Healthy")], []));

    services.advance(9000);
    expect(window.location.hash).toBe("");
    services.advance(1000);
    expect(window.location.hash).toBe("#/dependencies");
    services.advance(10000);
    expect(window.location.hash).toBe("#/");
  });

  it("shows the view it rotated to", async () => {
    const page = mount("/tv?rotate=10&views=overview,dependencies");
    page.services.send(overview([scope("shop", "Healthy")], []));

    page.services.advance(10000);
    await page.settle();

    expect(page.main.textContent).toContain("Dependencies");
  });

  it("does not rotate on the normal page", () => {
    const { services } = mount("/");
    services.send(overview([scope("shop", "Healthy")], []));

    services.advance(600000);

    expect(window.location.hash).toBe("");
  });

  it("does not rotate when rotation is turned off", () => {
    const { services } = mount("/tv?rotate=0");
    services.send(overview([scope("shop", "Healthy")], []));

    services.advance(600000);

    expect(window.location.hash).toBe("");
  });

  it("opens a scope that turns unhealthy and stops rotating while it is shown", () => {
    const { services } = mount("/tv?rotate=10&views=overview,dependencies");
    services.send(overview([scope("shop", "Healthy")], []));

    services.send(overview([scope("shop", "Unhealthy")], []));
    services.advance(60000);

    expect(window.location.hash).toBe("#/scopes/shop");
  });

  it("goes back to the rotation once the problem has cleared", () => {
    const { services } = mount("/tv?rotate=10&views=overview,dependencies");
    services.send(overview([scope("shop", "Unhealthy")], []));
    expect(window.location.hash).toBe("#/scopes/shop");

    services.send(overview([scope("shop", "Healthy")], []));

    expect(window.location.hash).toBe("#/");
  });

  it("goes on with the rotation when the focus time has passed, and does not come back to the same problem", () => {
    const { services } = mount("/tv?rotate=10&views=overview,dependencies&focus=30");
    services.send(overview([scope("shop", "Unhealthy")], []));

    services.advance(30000);
    services.send(overview([scope("shop", "Unhealthy")], []));

    expect(window.location.hash).toBe("#/");
  });

  it("shows a scope that fails again after it had recovered", () => {
    const { services } = mount("/tv?rotate=10&views=overview,dependencies&focus=30");
    services.send(overview([scope("shop", "Unhealthy")], []));
    services.advance(30000);
    services.send(overview([scope("shop", "Healthy")], []));

    services.send(overview([scope("shop", "Unhealthy")], []));

    expect(window.location.hash).toBe("#/scopes/shop");
  });

  it("moves on to another failing scope when it has given up on the first", () => {
    const { services } = mount("/tv?rotate=10&focus=30");
    services.send(overview([scope("alpha", "Unhealthy"), scope("bravo", "Unhealthy")], []));
    expect(window.location.hash).toBe("#/scopes/alpha");

    services.advance(30000);
    services.send(overview([scope("alpha", "Unhealthy"), scope("bravo", "Unhealthy")], []));

    expect(window.location.hash).toBe("#/scopes/bravo");
  });

  it("does not react to a scope that is only degraded", () => {
    const { services } = mount("/tv?rotate=10&views=overview,dependencies");
    services.send(overview([scope("shop", "Degraded")], []));

    expect(window.location.hash).toBe("");
  });

  it("lets the layout drift when asked to", () => {
    mount("/tv?drift=1");
    expect(document.body.classList.contains("drift")).toBe(true);
  });

  it("does not drift by default", () => {
    mount("/tv");
    document.body.classList.remove("drift");
    mount("/tv");
    expect(document.body.classList.contains("drift")).toBe(false);
  });
});
