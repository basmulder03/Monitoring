import { createContext } from "preact";
import type { ComponentChildren } from "preact";
import { useContext, useRef } from "preact/hooks";

type Opened = Map<string, boolean>;

const OpenContext = createContext<Opened | null>(null);

/** Remembers which sections a person opened, also while they look at another view and come back. */
export function OpenStateProvider({ children }: { children: ComponentChildren }) {
  const opened = useRef<Opened>(new Map());
  return <OpenContext.Provider value={opened.current}>{children}</OpenContext.Provider>;
}

/** Props for a `<details>` that remembers whether it is open, starting from a default. */
export function useDisclosure(key: string, openByDefault: boolean) {
  const opened = useContext(OpenContext);
  return {
    open: opened?.get(key) ?? openByDefault,
    onToggle(event: Event) {
      opened?.set(key, (event.currentTarget as HTMLDetailsElement).open);
    },
  };
}
