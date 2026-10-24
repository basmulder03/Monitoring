export type IconName =
  | "healthy" | "degraded" | "unhealthy" | "unknown" | "bell" | "scope" | "theme"
  | "environment" | "datacenter" | "service" | "instance" | "database" | "queue" | "external";

/** One icon from the sprite, by name; it is decoration, so it is hidden from screen readers and the text next to it carries the meaning. */
export function Icon({ name }: { name: IconName }) {
  return (
    <svg class="icon" aria-hidden="true">
      <use href={`#i-${name}`} />
    </svg>
  );
}
