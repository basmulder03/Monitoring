import { createContext } from "preact";
import { useContext } from "preact/hooks";
import type { Services } from "./Services";

const ServicesContext = createContext<Services | null>(null);

export const ServicesProvider = ServicesContext.Provider;

/** The services of the running app. */
export function useServices(): Services {
  const services = useContext(ServicesContext);
  if (services === null) throw new Error("The app was started without services.");
  return services;
}
