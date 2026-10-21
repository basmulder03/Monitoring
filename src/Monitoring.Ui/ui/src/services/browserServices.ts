import type { Overview } from "../api/contracts";
import type { Services } from "./Services";

/** The real thing: fetch, EventSource, the clock and local storage. */
export const browserServices: Services = {
  async getJson<T>(path: string) {
    const response = await fetch(path);
    return { status: response.status, body: response.status === 200 ? ((await response.json()) as T) : null };
  },

  followOverview({ onOverview, onKeepAlive }) {
    const source = new EventSource("/api/events");
    source.addEventListener("overview", (event) => onOverview(JSON.parse((event as MessageEvent<string>).data) as Overview));
    source.addEventListener("keep-alive", onKeepAlive);
    return () => source.close();
  },

  now: () => Date.now(),

  every(milliseconds, callback) {
    const handle = setInterval(callback, milliseconds);
    return () => clearInterval(handle);
  },

  recall(key) {
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  },

  remember(key, value) {
    try {
      localStorage.setItem(key, value);
    } catch {
      // A remembered setting is a convenience only.
    }
  },
};
