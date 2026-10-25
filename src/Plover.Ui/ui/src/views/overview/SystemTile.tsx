import type { SystemStatus } from "../../api/contracts";
import { Icon } from "../../components/Icon";
import { Status } from "../../components/Status";
import { plural } from "../../format";
import { useJson } from "../../state/useJson";

/** The state of the monitoring system itself, as one tile that leads to the details. */
export function SystemTile({ refresh }: { refresh: unknown }) {
  const loaded = useJson<SystemStatus>("/api/system", refresh);
  if (loaded.state !== "found") return null;

  const status = loaded.value;
  const behind = status.replicas.filter((replica) => replica.agentsConnected < replica.agentsReporting).length;
  const missing = status.scopes - status.agentsConnected;
  const health = status.healthy ? "Healthy" : "Degraded";
  return (
    <ul class="tiles">
      <li>
        <a class={`system-tile ${health.toLowerCase()}`} href="#/system">
          <span class="name">
            <Icon name="scope" /> Plover system
          </span>
          <Status health={health} />
          <span class="muted">
            {plural(status.replicas.length, "controller replica", "controller replicas")}, {status.agentsConnected} of {status.scopes} agents connected here
          </span>
          {missing > 0 && <span class="disconnected">{plural(missing, "scope has", "scopes have")} no agent connected to this controller</span>}
          {behind > 0 && <span class="disconnected">{plural(behind, "replica is", "replicas are")} not reached by every agent</span>}
          {status.unmonitored.length > 0 && <span class="muted">{plural(status.unmonitored.length, "pod runs", "pods run")} without a sidecar</span>}
        </a>
      </li>
    </ul>
  );
}
