export type IconName = "healthy" | "degraded" | "unhealthy" | "unknown" | "bell" | "scope" | "theme";

export function Icon({ name }: { name: IconName }) {
  return (
    <svg class="icon" aria-hidden="true">
      <use href={`#i-${name}`} />
    </svg>
  );
}
