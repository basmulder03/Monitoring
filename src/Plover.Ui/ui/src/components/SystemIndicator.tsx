import type { SystemStatus } from "../api/contracts";
import { plural } from "../format";
import { Icon } from "./Icon";

/** What is wrong with the monitoring system itself, in words; empty when nothing is. */
export function systemProblems(status: SystemStatus): string[] {
  const missing = status.scopes - status.agentsConnected;
  const behind = status.replicas.filter((replica) => replica.agentsConnected < replica.agentsReporting).length;
  return [
    ...(missing > 0 ? [`${plural(missing, "scope has", "scopes have")} no agent connected to this controller`] : []),
    ...(behind > 0 ? [`${plural(behind, "replica is", "replicas are")} not reached by every agent`] : []),
    ...(status.unmonitored.length > 0 ? [`${plural(status.unmonitored.length, "pod runs", "pods run")} without a sidecar`] : []),
  ];
}

/** The state of the monitoring system as a small light in the header; the details show on hover or focus, and by themselves for as long as something is wrong. */
export function SystemIndicator({ status }: { status: SystemStatus | null }) {
  if (status === null) return null;

  const health = status.healthy ? "healthy" : "degraded";
  return (
    <div class={`system-indicator ${health}`} id="system-indicator">
      <a href="#/system" aria-describedby="system-detail">
        <Icon name={health} /> <span>{status.healthy ? "Plover" : "Plover system degraded"}</span>
      </a>
      <div class="system-detail" id="system-detail" role="tooltip">
        <strong>Plover system: {status.healthy ? "healthy" : "degraded"}</strong>
        <span>
          {plural(status.replicas.length, "controller replica", "controller replicas")}, {status.agentsConnected} of {status.scopes} agents connected here
        </span>
        {systemProblems(status).map((problem) => (
          <span class="disconnected" key={problem}>{problem}</span>
        ))}
        <span class="muted">Details on the System page</span>
      </div>
    </div>
  );
}
