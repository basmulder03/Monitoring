/** Where the documentation lives; the dashboard is served by the controller, so the links go to the published site. */
const DOCS = "https://basmulder03.github.io/Plover/";

/** The page of the documentation that explains an alert rule; rules without a section of their own go to the list of all rules. */
const ALERT_DOCS: Record<string, string> = {
  "instance-unhealthy": "design#alerting",
  "service-down": "design#alerting",
  "restart-loop": "design#alerting",
  "edge-error-rate": "design#alerting",
  "scope-unknown": "configuration#when-an-agent-goes-away",
  "configuration-drift": "configuration#central-configuration",
  "connection-pool-full": "configuration#database-connection-pools",
  "queue-without-consumers": "configuration#queues-an-application-depends-on",
  "queue-dead-letters": "configuration#queues-an-application-depends-on",
  "certificate-expiring": "configuration#certificates-an-application-serves-or-uses",
  "certificate-expired-soon": "configuration#certificates-an-application-serves-or-uses",
  "package-drift": "configuration#aligned-packages",
  "vulnerable-package": "configuration#vulnerable-packages",
  "vulnerable-package-critical": "configuration#vulnerable-packages",
};

export const alertRuleDocs = (name: string): string => `${DOCS}${ALERT_DOCS[name] ?? "reference/structure#alert-rules"}`;

export const alignmentRuleDocs = `${DOCS}configuration#aligned-packages`;
