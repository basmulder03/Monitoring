import type { RulesReport } from "../../api/contracts";
import { useJson } from "../../state/useJson";
import { alertRuleDocs, alignmentRuleDocs } from "./docs";

function wait(seconds: number): string {
  if (seconds === 0) return "at once";
  if (seconds % 3600 === 0) return `after ${seconds / 3600} h`;
  if (seconds % 60 === 0) return `after ${seconds / 60} min`;
  return `after ${seconds} s`;
}

/** What the controller checks, each rule linking to its place in the documentation: the alert rules, and the packages that must be used in the same version. */
export function RulesView({ refresh }: { refresh: unknown }) {
  const loaded = useJson<RulesReport>("/api/rules", refresh);

  if (loaded.state === "loading") return <p class="muted">Loading…</p>;
  if (loaded.state !== "found") return <p class="muted">The rules could not be loaded.</p>;

  const { alerts, aligned } = loaded.value;
  return (
    <div>
      <h2>Rules</h2>
      <div class="blocks">
        <section>
          <h3>Packages that must be aligned</h3>
          {aligned.length === 0 ? (
            <p class="muted">No rule is configured, so different versions of a package are never a problem.</p>
          ) : (
            <table class="instances">
              <thead>
                <tr>
                  <th>Package</th>
                  <th>Rule</th>
                </tr>
              </thead>
              <tbody>
                {aligned.map((rule) => (
                  <tr key={rule.package}>
                    <td>
                      <a href={alignmentRuleDocs} target="_blank" rel="noopener">{rule.package}</a>
                    </td>
                    <td>
                      {rule.rule}
                      {rule.description && <span class="muted rule-why">{rule.description}</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
        <section>
          <h3>Alert rules</h3>
          <table class="instances">
            <thead>
              <tr>
                <th>Rule</th>
                <th>Severity</th>
                <th>Fires</th>
              </tr>
            </thead>
            <tbody>
              {alerts.map((rule) => (
                <tr key={rule.name}>
                  <td>
                    <a href={alertRuleDocs(rule.name)} target="_blank" rel="noopener">{rule.name}</a>
                  </td>
                  <td>{rule.severity}</td>
                  <td>{wait(rule.waitSeconds)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      </div>
    </div>
  );
}
