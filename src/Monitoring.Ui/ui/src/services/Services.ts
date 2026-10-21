import type { Overview } from "../api/contracts";

/** What the page needs from the browser, so the whole app can run against fakes in tests. */
export interface Services {
  /** Reads JSON from the controller. Rejects when the request fails. */
  getJson<T>(path: string): Promise<{ status: number; body: T | null }>;
  /** Follows the live stream of overviews. Returns a function that stops it. */
  followOverview(handlers: { onOverview(overview: Overview): void; onKeepAlive(): void }): () => void;
  /** The current time in milliseconds, for ages and for noticing old data. */
  now(): number;
  /** Runs the callback on an interval and returns a function that stops it. */
  every(milliseconds: number, callback: () => void): () => void;
  /** Reads a remembered setting; null when there is none or storage is not available. */
  recall(key: string): string | null;
  /** Remembers a setting; silently does nothing when storage is not available. */
  remember(key: string, value: string): void;
}
