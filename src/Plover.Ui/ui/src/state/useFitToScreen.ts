import { useEffect } from "preact/hooks";

const SMALLEST = 0.35;
const STEP = 0.04;

/**
 * Keeps a wall display from ever overflowing its screen: whenever the page changes or the window is resized, the type is made smaller
 * (the whole page is sized in rem) until everything fits, down to a smallest size where the rest is cut off rather than scrolled to.
 */
export function useFitToScreen(active: boolean): void {
  useEffect(() => {
    if (!active) return undefined;

    const root = document.documentElement;
    let frame = 0;
    const fit = () => {
      let scale = 1;
      root.style.setProperty("--tv-fit", "1");
      // The drift moves the page a few pixels, so what is measured is the page without it.
      while (scale > SMALLEST && (root.scrollHeight > root.clientHeight || root.scrollWidth > root.clientWidth)) {
        scale = Math.round((scale - STEP) * 100) / 100;
        root.style.setProperty("--tv-fit", String(scale));
      }
    };
    const schedule = () => {
      cancelAnimationFrame(frame);
      frame = requestAnimationFrame(fit);
    };

    // Only changes to the content count; the size that is set here is an attribute and does not start another round.
    const observer = new MutationObserver(schedule);
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
    window.addEventListener("resize", schedule);
    schedule();
    return () => {
      cancelAnimationFrame(frame);
      observer.disconnect();
      window.removeEventListener("resize", schedule);
      root.style.removeProperty("--tv-fit");
    };
  }, [active]);
}
