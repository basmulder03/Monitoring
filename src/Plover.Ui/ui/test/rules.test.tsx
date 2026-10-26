import { all, mount, overview, scope } from "./harness";

async function open(rules: { alerts: { name: string; severity: string; waitSeconds: number }[]; aligned: { package: string; rule: string; description?: string | null }[] }) {
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
  });

  it("says when no alignment rule is configured", async () => {
    const page = await open({ alerts: [], aligned: [] });
    expect(page.main.textContent).toContain("No rule is configured");
  });
});
