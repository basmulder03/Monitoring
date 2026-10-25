import type { HealthState } from "./api/contracts";

/** The words shown for each health state; a state is always an icon and a word, never a colour alone. */
export const HEALTH_LABELS: Record<HealthState, string> = {
  Healthy: "Healthy",
  Degraded: "Degraded",
  Unhealthy: "Unhealthy",
  Unknown: "Unknown",
};

/** From best to worst, so the worst of several is the one with the highest index. */
export const SEVERITY: readonly HealthState[] = ["Healthy", "Unknown", "Degraded", "Unhealthy"];

export function worstOf(states: Iterable<HealthState>): HealthState {
  let worst: HealthState = "Healthy";
  for (const state of states) {
    if (SEVERITY.indexOf(state) > SEVERITY.indexOf(worst)) worst = state;
  }
  return worst;
}

/** How long ago something was, in the largest sensible unit. */
export function age(since: string, now: number): string {
  const seconds = Math.max(0, Math.round((now - new Date(since).getTime()) / 1000));
  if (seconds < 60) return `${seconds} s`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)} min`;
  return `${Math.floor(seconds / 3600)} h ${Math.floor((seconds % 3600) / 60)} min`;
}

export function plural(count: number, one: string, many: string): string {
  return `${count} ${count === 1 ? one : many}`;
}

export function labelText(labels: Record<string, string>): string {
  return Object.entries(labels).map(([key, value]) => `${key}=${value}`).join(", ");
}
