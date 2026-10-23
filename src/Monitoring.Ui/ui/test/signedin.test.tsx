import { mount, overview, scope } from "./harness";

describe("who is signed in", () => {
  it("shows the name and role when the controller asks who is looking", async () => {
    const page = mount("/", (services) => {
      services.me = { authenticationEnabled: true, name: "wall", role: "viewer" };
    });
    page.services.send(overview([scope("shop", "Healthy")], []));
    await page.settle();

    expect(document.getElementById("signed-in")?.textContent).toBe("wall (viewer)");
  });

  it("shows nothing on a controller that is open to everyone", async () => {
    const page = mount("/", (services) => {
      services.me = { authenticationEnabled: false, name: "", role: "operator" };
    });
    page.services.send(overview([scope("shop", "Healthy")], []));
    await page.settle();

    expect(document.getElementById("signed-in")).toBeNull();
  });
});
