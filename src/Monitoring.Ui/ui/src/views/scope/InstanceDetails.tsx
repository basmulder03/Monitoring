import type { InstanceDetail } from "../../api/contracts";
import { Status } from "../../components/Status";
import { plural } from "../../format";
import { useDisclosure } from "../../state/OpenState";
import { ContainersTable } from "./ContainersTable";

/** What one instance says about itself: version, health checks, containers and how many metrics it reports. */
export function InstanceDetails({ scope, instance }: { scope: string; instance: InstanceDetail }) {
  const disclosure = useDisclosure(`instance:${scope}/${instance.id}`, false);

  return (
    <details class="instance" {...disclosure}>
      <summary>
        {instance.id}
        {instance.version && <span class="muted"> {instance.version}</span>}
      </summary>

      {instance.checks.length > 0 && (
        <>
          <h4>Health checks</h4>
          <ul class="checks">
            {instance.checks.map((check) => (
              <li key={check.name}>
                <Status health={check.health} />
                <strong> {check.name}</strong>
                {check.description && <span class="muted"> {check.description}</span>}
              </li>
            ))}
          </ul>
        </>
      )}

      {instance.containers.length > 0 && (
        <>
          <h4>Containers</h4>
          <ContainersTable containers={instance.containers} />
        </>
      )}

      {instance.metrics.length > 0 && <p class="muted">{plural(instance.metrics.length, "metric", "metrics")} reported</p>}
    </details>
  );
}
