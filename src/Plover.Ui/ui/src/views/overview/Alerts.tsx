import { useState } from "preact/hooks";
import type { AlertOccurrence, AlertSummary, RuntimeSilence, WhoAmI } from "../../api/contracts";
import { Icon } from "../../components/Icon";
import { age } from "../../format";
import { useServices } from "../../services/ServicesContext";
import { useJson } from "../../state/useJson";
import { SilenceForm } from "./SilenceForm";

/** The alerts that are firing, and for an operator the ways to say someone is on it or to silence it. */
export function Alerts({ alerts, now }: { alerts: readonly AlertSummary[]; now: number }) {
  const services = useServices();
  const me = useJson<WhoAmI>("/api/me", null);
  // Only a person who signed in as an operator can change anything; the controller refuses anyone else, so this only spares the others buttons that would not work.
  const canChange = me.state === "found" && me.value.authenticationEnabled && me.value.role === "operator";
  const [version, setVersion] = useState(0);
  const [problem, setProblem] = useState<string | null>(null);
  const [silencing, setSilencing] = useState<string | null>(null);

  async function change(method: "POST" | "DELETE", path: string, body?: unknown) {
    const result = await services.change(method, path, body);
    setProblem(result.error);
    setVersion((v) => v + 1);
    return result.error === null;
  }

  return (
    <section>
      <h2>
        <Icon name="bell" /> Alerts
      </h2>
      {problem !== null && (
        <p class="notice" role="alert">
          {problem}
        </p>
      )}
      {alerts.length === 0 ? (
        <p class="muted">No alerts are firing.</p>
      ) : (
        <ul class="alerts">
          {alerts.map((alert) => (
            <li class={`alert ${alert.severity.toLowerCase()}`} key={alert.key}>
              <span class={`severity ${alert.severity.toLowerCase()}`}>{alert.severity}</span>
              <span>{alert.subject}</span>
              <span class="muted">
                {alert.rule}, for {age(alert.since, now)}
              </span>
              {alert.acknowledged && (
                <span class="muted">
                  Acknowledged by {alert.acknowledged.by}, {age(alert.acknowledged.at, now)} ago{alert.acknowledged.note ? `: ${alert.acknowledged.note}` : ""}
                </span>
              )}
              {canChange && (
                <span class="actions">
                  {alert.acknowledged ? (
                    <button type="button" class="action" onClick={() => change("DELETE", `/api/alerts/acknowledge?key=${encodeURIComponent(alert.key)}`)}>
                      Take back acknowledgement
                    </button>
                  ) : (
                    <button type="button" class="action" onClick={() => change("POST", "/api/alerts/acknowledge", { key: alert.key })}>
                      Acknowledge
                    </button>
                  )}
                  <button type="button" class="action" aria-expanded={silencing === alert.key} onClick={() => setSilencing(silencing === alert.key ? null : alert.key)}>
                    Silence…
                  </button>
                </span>
              )}
              {canChange && silencing === alert.key && (
                <SilenceForm
                  rule={alert.rule}
                  subject={alert.subject}
                  onSilence={async (body) => {
                    const done = await change("POST", "/api/silences", body);
                    if (done) setSilencing(null);
                  }}
                />
              )}
            </li>
          ))}
        </ul>
      )}
      <RecentAlerts refresh={alerts} now={now} />
      <Silences canChange={canChange} refresh={[alerts, version]} now={now} onTakeBack={(id) => change("DELETE", `/api/silences/${id}`)} />
    </section>
  );
}

/** The silences in force, so what is hidden is never a secret. */
function Silences({ canChange, refresh, now, onTakeBack }: { canChange: boolean; refresh: unknown; now: number; onTakeBack(id: string): void }) {
  const loaded = useJson<RuntimeSilence[]>("/api/silences", refresh);
  if (loaded.state !== "found" || loaded.value.length === 0) return null;
  return (
    <div>
      <h3>Silences in force</h3>
      <ul class="alerts">
        {loaded.value.map((silence) => (
          <li class="silence" key={silence.id}>
            <span>
              {silence.rule === "*" ? "Every rule" : silence.rule}, {silence.subjectPrefix === "" ? "every subject" : `subjects starting with ${silence.subjectPrefix}`}
            </span>
            <span class="muted">
              until {new Date(silence.until).toLocaleString()} ({age(new Date(now).toISOString(), new Date(silence.until).getTime())} left), by {silence.by}
              {silence.reason ? `: ${silence.reason}` : ""}
            </span>
            {canChange && (
              <button type="button" class="action" onClick={() => onTakeBack(silence.id)}>
                Take back
              </button>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}

/** What fired lately, also what has stopped since, so a problem that came and went at night can still be seen. */
function RecentAlerts({ refresh, now }: { refresh: unknown; now: number }) {
  const loaded = useJson<AlertOccurrence[]>("/api/alerts/history?limit=20", refresh);
  if (loaded.state !== "found" || loaded.value.length === 0) return null;
  return (
    <details class="history">
      <summary>Recent alerts</summary>
      <ul class="alerts">
        {loaded.value.map((o) => (
          <li class="occurrence" key={`${o.key}@${o.started}`}>
            <span class={`severity ${o.severity.toLowerCase()}`}>{o.severity}</span>
            <span>{o.subject}</span>
            <span class="muted">
              {o.rule}, started {age(o.started, now)} ago,{" "}
              {o.ended === null ? "still firing" : o.endedBy === "restart" ? "ended unknown (the controller restarted)" : `lasted ${age(o.started, new Date(o.ended).getTime())}`}
            </span>
          </li>
        ))}
      </ul>
    </details>
  );
}
