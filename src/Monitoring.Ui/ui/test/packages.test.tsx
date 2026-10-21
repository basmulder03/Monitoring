import { act } from "preact/test-utils";
import type { PackageCatalog } from "../src/api/contracts";
import { all, mount, overview, scope } from "./harness";

const catalog: PackageCatalog = {
  services: 3,
  servicesWithoutList: 1,
  packages: [
    {
      name: "Microsoft.Extensions.Hosting",
      versions: [{ version: "10.0.0", usedBy: [{ scope: "shop", service: "orders", instances: 2, direct: true }] }],
    },
    {
      name: "Serilog",
      versions: [
        { version: "4.0.0", usedBy: [{ scope: "shop", service: "orders", instances: 2, direct: true }] },
        { version: "3.1.0", usedBy: [{ scope: "billing", service: "invoices", instances: 1, direct: false }] },
      ],
    },
    { name: "<img src=x onerror=window.__pwned=1>", versions: [{ version: "1.0.0", usedBy: [] }] },
  ],
};

async function openPackages(data: PackageCatalog = catalog) {
  const page = mount("/#/packages", (services) => {
    services.packages = data;
  });
  page.services.send(overview([scope("shop", "Healthy")], []));
  await page.settle();
  return page;
}

describe("the packages view", () => {
  it("lists the packages and says how many services have not reported theirs", async () => {
    const page = await openPackages();
    expect(all(page.main, "details.package")).toHaveLength(3);
    expect(page.main.textContent).toContain("3 packages in 3 services");
    expect(page.main.textContent).toContain("1 service has not reported its packages");
  });

  it("opens a package that is used in several versions and says so in words", async () => {
    const page = await openPackages();
    const serilog = all(page.main, "details.package").find((d) => d.textContent!.includes("Serilog"))! as HTMLDetailsElement;
    const single = all(page.main, "details.package").find((d) => d.textContent!.includes("Microsoft.Extensions.Hosting"))! as HTMLDetailsElement;
    expect(serilog.open).toBe(true);
    expect(serilog.textContent).toContain("2 versions in use");
    expect(single.open).toBe(false);
  });

  it("shows who uses each version, marking what comes in indirectly", async () => {
    const page = await openPackages();
    const text = all(page.main, "details.package").find((d) => d.textContent!.includes("Serilog"))!.textContent!;
    expect(text).toContain("shop/orders (2 instances)");
    expect(text).toContain("billing/invoices (1 instance, indirect)");
  });

  it("narrows the list as the search is typed", async () => {
    const page = await openPackages();
    const input = page.main.querySelector<HTMLInputElement>("#package-search")!;
    act(() => {
      input.value = "seri";
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
    expect(all(page.main, "details.package")).toHaveLength(1);
    expect(page.main.querySelector("label")!.htmlFor).toBe("package-search");
  });

  it("says so when nothing matches the search", async () => {
    const page = await openPackages();
    const input = page.main.querySelector<HTMLInputElement>("#package-search")!;
    act(() => {
      input.value = "zzz";
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
    expect(page.main.textContent).toContain("No package matches the search.");
  });

  it("offers the bill of materials for download", async () => {
    const page = await openPackages();
    const link = page.main.querySelector<HTMLAnchorElement>("a[href='/api/sbom']")!;
    expect(link.textContent).toContain("CycloneDX");
    expect(link.hasAttribute("download")).toBe(true);
  });

  it("shows names as text, never as markup", async () => {
    const page = await openPackages();
    expect(all(page.main, "details.package img")).toHaveLength(0);
    expect(page.main.textContent).toContain("<img src=x");
  });

  it("says so when no service has reported its packages", async () => {
    const page = await openPackages({ packages: [], services: 0, servicesWithoutList: 2 });
    expect(page.main.textContent).toContain("No service has reported what it is built from yet.");
  });

  it("marks the current view in the navigation", async () => {
    await openPackages();
    const current = [...document.querySelectorAll("nav a[aria-current='page']")].map((a) => a.textContent);
    expect(current).toEqual(["Packages"]);
  });
});
