import type { SystemStatus } from "../../api/contracts";
import { plural } from "../../format";
import { useJson } from "../../state/useJson";

/** How this controller replica is doing: the agents it hears, the pages it feeds and what it uses. */
export function SystemView({ refresh }: { refresh: unknown }) {
  const loaded = useJson<SystemStatus>("/api/system", refresh);

  if (loaded.state === "loading") return <p class="muted">Loading…</p>;
  if (loaded.state !== "found") return <p class="muted">The state of the controller could not be loaded.</p>;

  const status = loaded.value;
  return (
    <div>
      <h2>System</h2>
      <p class={status.healthy ? "muted" : "violation"}>
        {status.scopes > status.agentsConnected
          ? `${plural(status.scopes - status.agentsConnected, "scope has", "scopes have")} no agent connected to this controller.`
          : status.healthy
            ? "Every scope has an agent connected to this controller, and every agent to every replica."
            : "Some agents are not connected to every controller replica."}
      </p>
      <table class="instances">
        <tbody>
          <tr><th scope="row">Replica</th><td>{status.replica}</td></tr>
          <tr><th scope="row">Running for</th><td>{plural(Math.floor(status.uptimeSeconds / 60), "minute", "minutes")}</td></tr>
          <tr><th scope="row">Agents connected</th><td>{status.agentsConnected} of {status.scopes}</td></tr>
          <tr><th scope="row">Open dashboards</th><td>{status.eventClients}</td></tr>
          <tr><th scope="row">Alert evaluation</th><td>{status.alertEvaluationMilliseconds === null ? "not yet" : `${status.alertEvaluationMilliseconds.toFixed(1)} ms`}</td></tr>
          <tr><th scope="row">Memory</th><td>{(status.memoryBytes / 1048576).toFixed(0)} MB</td></tr>
        </tbody>
      </table>

      <h3>Controller replicas</h3>
      {status.replicas.length === 0 ? (
        <p class="muted">No agent has said which replicas it connects to yet.</p>
      ) : (
        <table class="instances">
          <thead>
            <tr>
              <th>Replica</th>
              <th>Agents connected</th>
              <th>Behind by</th>
            </tr>
          </thead>
          <tbody>
            {status.replicas.map((replica) => (
              <tr key={replica.address}>
                <td>
                  {replica.address}
                  {replica.thisReplica && <span class="muted"> (this one)</span>}
                </td>
                <td class={replica.agentsConnected < replica.agentsReporting ? "violation" : undefined}>
                  {replica.agentsConnected} of {replica.agentsReporting}
                </td>
                <td>{plural(replica.maxLag, "change", "changes")}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h3>Pods without a monitoring sidecar</h3>
      {status.unmonitored.length === 0 ? (
        <p class="muted">Every pod has one.</p>
      ) : (
        <table class="instances">
          <thead>
            <tr>
              <th>Scope</th>
              <th>Service</th>
              <th>Pod</th>
            </tr>
          </thead>
          <tbody>
            {status.unmonitored.map((pod) => (
              <tr key={`${pod.scope}/${pod.instance}`}>
                <td><a href={`#/scopes/${encodeURIComponent(pod.scope)}`}>{pod.scope}</a></td>
                <td>{pod.service}</td>
                <td>{pod.instance}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  );
}
