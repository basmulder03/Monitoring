import { useEffect, useState } from "preact/hooks";
import { useServices } from "../services/ServicesContext";

/** The current time, updated at an interval, so ages and warnings about old data stay true while nothing else changes. */
export function useClock(milliseconds: number): number {
  const services = useServices();
  const [now, setNow] = useState(() => services.now());
  useEffect(() => services.every(milliseconds, () => setNow(services.now())), [services, milliseconds]);
  return now;
}
