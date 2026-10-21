const STALE_AFTER_MS = 15000;

/** Warns when nothing has been heard from the controller for a while, so a frozen screen never shows stale green. */
export function StaleBanner({ lastHeard, now }: { lastHeard: number; now: number }) {
  const seconds = Math.round((now - lastHeard) / 1000);
  const stale = seconds * 1000 >= STALE_AFTER_MS;
  return (
    <div id="stale" class="banner" role="status" hidden={!stale}>
      {stale ? `The data is ${seconds} seconds old. The connection to the controller may be lost.` : ""}
    </div>
  );
}
