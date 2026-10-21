import type { ServiceDetail } from "../../api/contracts";
import { Status } from "../../components/Status";
import { labelText, plural } from "../../format";
import { useDisclosure } from "../../state/OpenState";
import { InstanceDetails } from "./InstanceDetails";

/** A service with its instances; open at once when it is not healthy. */
export function ServiceCard({ scope, service }: { scope: string; service: ServiceDetail }) {
  const disclosure = useDisclosure(`service:${scope}/${service.name}`, service.health !== "Healthy");

  return (
    <details class="service" {...disclosure}>
      <summary>
        <strong>{service.name}</strong>
        <Status health={service.health} />
        <span class="muted">
          {service.healthyInstances} of {plural(service.liveInstances, "instance", "instances")} healthy
        </span>
      </summary>
      <table class="instances">
        <thead>
          <tr>
            {["Instance", "State", "Health", "Labels"].map((title) => (
              <th key={title}>{title}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {service.instances.map((instance) => (
            <tr key={instance.id}>
              <td>
                <InstanceDetails scope={scope} instance={instance} />
              </td>
              <td>{instance.lifecycle}</td>
              <td>
                <Status health={instance.health} />
              </td>
              <td class="muted">{labelText(instance.labels)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </details>
  );
}
