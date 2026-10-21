import type { AlertSummary } from "../../api/contracts";
import { Icon } from "../../components/Icon";
import { age } from "../../format";

/** The alerts that are firing. */
export function Alerts({ alerts, now }: { alerts: readonly AlertSummary[]; now: number }) {
  return (
    <section>
      <h2>
        <Icon name="bell" /> Alerts
      </h2>
      {alerts.length === 0 ? (
        <p class="muted">No alerts are firing.</p>
      ) : (
        <ul class="alerts">
          {alerts.map((alert) => (
            <li class="alert" key={alert.key}>
              <span class={`severity ${alert.severity.toLowerCase()}`}>{alert.severity}</span>
              <span>{alert.subject}</span>
              <span class="muted">
                {alert.rule}, for {age(alert.since, now)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
