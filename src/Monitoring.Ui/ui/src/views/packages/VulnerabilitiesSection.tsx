import type { VulnerabilityReport } from "../../api/contracts";
import { plural } from "../../format";
import { useJson } from "../../state/useJson";

const SEVERITY_TEXT: Record<string, string> = { CRITICAL: "Critical", HIGH: "High", MODERATE: "Moderate", LOW: "Low", UNKNOWN: "Severity unknown" };

/** The package versions in use that known advisories say are vulnerable, the most severe first. */
export function VulnerabilitiesSection({ refresh }: { refresh: unknown }) {
  const loaded = useJson<VulnerabilityReport>("/api/vulnerabilities", refresh);
  if (loaded.state !== "found") return null;

  const report = loaded.value;
  return (
    <section>
      <h3>Vulnerabilities</h3>
      {report.advisories === 0 ? (
        <p class="muted">No advisories are loaded, so nothing was checked.</p>
      ) : report.findings.length === 0 ? (
        <p class="muted">No package in use is affected by the {plural(report.advisories, "advisory", "advisories")} that are loaded.</p>
      ) : (
        <ul class="findings">
          {report.findings.map((finding) => (
            <li key={`${finding.advisory}/${finding.package}/${finding.version}`} class="finding">
              <span class={`severity ${finding.severity === "CRITICAL" || finding.severity === "HIGH" ? "critical" : "warning"}`}>
                {SEVERITY_TEXT[finding.severity] ?? finding.severity}
              </span>
              <strong>
                {finding.package} {finding.version}
              </strong>
              <span>
                {finding.advisory}
                {finding.aliases.length > 0 ? ` (${finding.aliases.join(", ")})` : ""}
              </span>
              {finding.summary && <span class="muted">{finding.summary}</span>}
              <span>{finding.fixedIn ? `Fixed in ${finding.fixedIn}.` : "No fix is known."}</span>
              <span class="muted">Used by {finding.usedBy.map((usage) => `${usage.scope}/${usage.service}`).join(", ")}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
