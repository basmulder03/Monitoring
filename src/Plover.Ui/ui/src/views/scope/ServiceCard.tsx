import type { ServiceDetail } from "../../api/contracts";
import { Icon } from "../../components/Icon";
import { Status } from "../../components/Status";
import { plural } from "../../format";
import { useDisclosure } from "../../state/OpenState";
import { InstanceDetails } from "./InstanceDetails";

/** A service with its instances; open at once when it is not healthy. */
export function ServiceCard({ scope, service }: { scope: string; service: ServiceDetail }) {
  const disclosure = useDisclosure(`service:${scope}/${service.name}`, service.health !== "Healthy");

  return (
    <details class="service" {...disclosure}>
      <summary>
        <Icon name="service" /> <strong>{service.name}</strong>
        <Status health={service.health} />
        <span class="muted">
          {service.healthyInstances} of {plural(service.liveInstances, "instance", "instances")} healthy
        </span>
      </summary>
      <table class="instances service-instances">
        <thead>
          <tr>
            {["Instance", "State", "Health"].map((title) => (
              <th key={title}>{title}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {service.instances.map((instance) => (
            <tr key={instance.id}>
              <td>
                <InstanceDetails scope={scope} instance={instance} />
                <div class="labels">
                  {Object.entries(instance.labels).map(([key, value]) => (
                    <span class="label" key={key}>{`${key}=${value}`}</span>
                  ))}
                </div>
              </td>
              <td>{instance.lifecycle}</td>
              <td>
                <Status health={instance.health} />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </details>
  );
}
