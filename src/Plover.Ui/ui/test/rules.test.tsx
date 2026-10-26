import type { RulesReport } from "../src/api/contracts";
import { all, mount, overview, scope } from "./harness";

async function open(rules: RulesReport) {
  const page = mount("/#/rules", (services) => {
    services.rules = rules;
  });
  page.services.send(overview([scope("shop", "Healthy")], []));
  await page.settle();
  return page;
}

describe("the rules view", () => {
  it("lists the alignment rules and the alert rules with when they fire", async () => {
    const page = await open({
      alerts: [{ name: "package-drift", severity: "Warning", waitSeconds: 3600 }, { name: "service-down", severity: "Critical", waitSeconds: 0 }],
      aligned: [
        { package: "Polly.*", rule: "Polly.* must have the same major and minor version", description: "Retries behave differently between minors." },
        { package: "Serilog", rule: "Serilog must have the same major version", description: null },
      ],
    });
    const text = page.main.textContent!;
    expect(text).toContain("Polly.* must have the same major and minor version");
    expect(text).toContain("after 1 h");
    expect(text).toContain("at once");
    expect(text).toContain("Retries behave differently between minors.");
    expect(all(page.main, ".rule-why")).toHaveLength(1);
    expect(all(page.main, "tbody tr")).toHaveLength(4);
    const links = all(page.main, "tbody a").map((a) => [a.textContent, a.getAttribute("href")]);
    expect(links).toContainEqual(["package-drift", "https://basmulder03.github.io/Plover/configuration#aligned-packages"]);
    expect(links).toContainEqual(["service-down", "https://basmulder03.github.io/Plover/design#alerting"]);
    expect(links).toContainEqual(["Polly.*", "https://basmulder03.github.io/Plover/configuration#aligned-packages"]);
  });

  it("says when no alignment rule is configured", async () => {
    const page = await open({ alerts: [], aligned: [] });
    expect(page.main.textContent).toContain("No rule is configured");
  });
});
