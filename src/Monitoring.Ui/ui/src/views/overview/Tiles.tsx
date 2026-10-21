import type { ScopeSummary } from "../../api/contracts";
import { Icon } from "../../components/Icon";
import { Status } from "../../components/Status";
import { age, plural } from "../../format";

/** One tile per scope. */
export function Tiles({ scopes, now }: { scopes: readonly ScopeSummary[]; now: number }) {
  return (
    <ul class="tiles">
      {scopes.map((scope) => (
        <li key={scope.name}>
          <a class={`tile ${scope.health.toLowerCase()}`} href={`#/scopes/${encodeURIComponent(scope.name)}`}>
            <span class="name">
              <Icon name="scope" /> {scope.name}
            </span>
            <Status health={scope.health} />
            <span class="muted">
              {plural(scope.services, "service", "services")}, {plural(scope.instances, "instance", "instances")}
              {scope.problems > 0 ? `, ${scope.problems} not healthy` : ""}
            </span>
            {!scope.agentConnected && (
              // Said in words as well as shown grey: the counts above are the last known, not current.
              <span class="disconnected">
                Agent not connected{scope.lastSeen ? `, last heard ${age(scope.lastSeen, now)} ago` : ""}
              </span>
            )}
          </a>
        </li>
      ))}
    </ul>
  );
}
