import type { ContainerInfo, ContainerRole } from "../../api/contracts";
import { Status } from "../../components/Status";

const ROLE_TEXT: Record<ContainerRole, string> = {
  App: "Application",
  MonitoringSidecar: "Monitoring sidecar",
  OtherSidecar: "Sidecar",
  Init: "Init",
};

function stateText(container: ContainerInfo): string {
  const reason = container.reason ? `: ${container.reason}` : "";
  const exit = container.phase === "Terminated" && container.exitCode !== null ? ` (exit ${container.exitCode})` : "";
  return container.phase + reason + exit;
}

function restartText(container: ContainerInfo): string {
  if (container.restarts === 0 || !container.lastReason) return String(container.restarts);
  const exit = container.lastExitCode !== null ? `, exit ${container.lastExitCode}` : "";
  return `${container.restarts} (last: ${container.lastReason}${exit})`;
}

/** The containers of a pod, so a problem can be pinned on the application, the monitoring sidecar or another sidecar. */
export function ContainersTable({ containers }: { containers: readonly ContainerInfo[] }) {
  return (
    <table class="containers">
      <thead>
        <tr>
          {["Container", "Role", "State", "Health", "Restarts", "Image"].map((title) => (
            <th key={title}>{title}</th>
          ))}
        </tr>
      </thead>
      <tbody>
        {containers.map((container) => (
          <tr key={container.name}>
            <td>{container.name}</td>
            <td>{ROLE_TEXT[container.role]}</td>
            <td>{stateText(container)}</td>
            <td>
              <Status health={container.health} />
            </td>
            <td>{restartText(container)}</td>
            <td class="muted">{container.image ?? ""}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
