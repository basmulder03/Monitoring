import type { HealthState } from "../api/contracts";
import { HEALTH_LABELS } from "../format";
import { Icon } from "./Icon";

/** A health state as an icon and a word, never a colour alone. */
export function Status({ health }: { health: HealthState }) {
  const known = health in HEALTH_LABELS ? health : "Unknown";
  return (
    <span class={`status ${known.toLowerCase()}`}>
      <Icon name={known.toLowerCase() as "healthy" | "degraded" | "unhealthy" | "unknown"} />
      <span>{HEALTH_LABELS[known]}</span>
    </span>
  );
}
