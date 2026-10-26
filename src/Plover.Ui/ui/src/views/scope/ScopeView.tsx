import type { ScopeDetail } from "../../api/contracts";
import { Status } from "../../components/Status";
import { age, labelText } from "../../format";
import { useJson } from "../../state/useJson";
import { ServiceCard } from "./ServiceCard";

const BACK = <a href="#/">← All scopes</a>;

interface ScopeViewProps {
  name: string;
  /** Changes whenever the overview does, which is when the scope is read again. */
  refresh: unknown;
  now: number;
}

/** One scope with its services, instances and containers. */
export function ScopeView({ name, refresh, now }: ScopeViewProps) {
  const loaded = useJson<ScopeDetail>(`/api/scopes/${encodeURIComponent(name)}`, refresh);

  if (loaded.state === "loading") return <p class="muted">Loading…</p>;
  if (loaded.state === "failed") return <p class="muted">The scope could not be loaded.</p>;
  if (loaded.state === "missing") {
    return (
      <div>
        {BACK}
        <p>The scope '{name}' is not known.</p>
      </div>
    );
  }

  const scope = loaded.value;
  const labels = labelText(scope.labels);
  return (
    <div>
      {BACK}
      <h2>
        {scope.name} <Status health={scope.health} />
      </h2>
      {!scope.agentConnected && (
        <p class="banner">
          The agent of this scope is not connected{scope.lastSeen ? ` (last heard ${age(scope.lastSeen, now)} ago)` : ""}. What is shown is the
          state it last reported.
        </p>
      )}
      {!scope.configuration.inSync && (
        <p class="notice">
          The agent of this scope uses configuration {scope.configuration.applied}, but {scope.configuration.expected} is expected. A recent change may
          not have reached it yet.
        </p>
      )}
      {labels && <p class="muted">{labels}</p>}
      {scope.services.length === 0 && <p class="muted">This scope has no services.</p>}
      <div class="cards">
        {scope.services.map((service) => (
          <ServiceCard key={service.name} scope={scope.name} service={service} />
        ))}
      </div>
    </div>
  );
}
