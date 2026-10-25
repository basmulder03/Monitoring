import { render } from "preact";
import { App } from "./App";
import type { Services } from "./services/Services";
import { ServicesProvider } from "./services/ServicesContext";
import { applyTheme } from "./state/useTheme";

/** Starts the page in a container. Everything the browser provides comes in through the services. */
export function start(services: Services, container: HTMLElement): void {
  // A wall display is the same page under /tv, with larger type and no cursor.
  if (window.location.pathname === "/tv") document.body.classList.add("tv");
  applyTheme(services);

  render(
    <ServicesProvider value={services}>
      <App />
    </ServicesProvider>,
    container,
  );
}
