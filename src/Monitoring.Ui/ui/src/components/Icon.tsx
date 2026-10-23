export type IconName =
  | "healthy" | "degraded" | "unhealthy" | "unknown" | "bell" | "scope" | "theme"
  | "environment" | "datacenter" | "service" | "instance" | "database" | "queue" | "external";

export function Icon({ name }: { name: IconName }) {
  return (
    <svg class="icon" aria-hidden="true">
      <use href={`#i-${name}`} />
    </svg>
  );
}
