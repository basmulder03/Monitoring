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
        {status.healthy
          ? "Every scope has an agent connected to this controller."
          : `${plural(status.scopes - status.agentsConnected, "scope has", "scopes have")} no agent connected to this controller.`}
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
    </div>
  );
}
