export type WallView = "overview" | "dependencies" | "packages";

export interface WallConfig {
  /** The views to rotate through, in order. */
  views: WallView[];
  /** Seconds each view stays; 0 keeps the first view and only reacts to failures. */
  rotateSeconds: number;
  /** Seconds a failing scope is kept on screen before the rotation goes on without it. */
  focusSeconds: number;
  /** Whether the layout drifts slowly, so a screen that is on all day does not burn in. */
  drift: boolean;
}

/** What /tv becomes when it is opened without settings, so the address on the screen says what the screen does and can be changed. */
export const DEFAULT_WALL_SEARCH = "rotate=60&views=overview,dependencies&focus=300&drift=1";

const VIEWS: readonly WallView[] = ["overview", "dependencies", "packages"];

const seconds = (value: string | null, fallback: number): number => {
  const parsed = value === null ? Number.NaN : Number.parseInt(value, 10);
  return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback;
};

/**
 * Reads how a wall display behaves from its address: <c>/tv?rotate=60&views=overview,dependencies&focus=300&drift=1</c>.
 * Returns null for the normal page, which never rotates by itself.
 */
export function readWallConfig(pathname: string, search: string): WallConfig | null {
  if (pathname !== "/tv") return null;

  const params = new URLSearchParams(search);
  const requested = (params.get("views") ?? "")
    .split(",")
    .map((name) => name.trim())
    .filter((name): name is WallView => (VIEWS as readonly string[]).includes(name));

  return {
    views: requested.length > 0 ? [...new Set(requested)] : ["overview", "dependencies"],
    rotateSeconds: seconds(params.get("rotate"), 60),
    focusSeconds: seconds(params.get("focus"), 300),
    drift: params.get("drift") === "1",
  };
}

export const hashOf = (view: WallView): string => (view === "overview" ? "#/" : `#/${view}`);
