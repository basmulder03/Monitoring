// GENERATED from the C# types of the dashboard API. Do not edit.
// Regenerate with: UPDATE_CONTRACTS=1 dotnet test --project tests/Monitoring.Ui.Tests

export type AlertSeverity = "Warning" | "Critical";

export interface AlertSummary {
  key: string;
  rule: string;
  subject: string;
  severity: AlertSeverity;
  since: string;
}

export interface ContainerInfo {
  name: string;
  role: ContainerRole;
  phase: ContainerPhase;
  health: HealthState;
  ready: boolean;
  restarts: number;
  image: string | null;
  imageDigest: string | null;
  reason: string | null;
  message: string | null;
  exitCode: number | null;
  lastExitCode: number | null;
  lastReason: string | null;
}

export type ContainerPhase = "Waiting" | "Running" | "Terminated";

export type ContainerRole = "App" | "MonitoringSidecar" | "OtherSidecar" | "Init";

export interface HealthCheckResult {
  name: string;
  health: HealthState;
  description: string | null;
}

export type HealthState = "Healthy" | "Unknown" | "Degraded" | "Unhealthy";

export interface InstanceDetail {
  id: string;
  lifecycle: InstanceLifecycle;
  health: HealthState;
  labels: Record<string, string>;
  version: string | null;
  checks: HealthCheckResult[];
  containers: ContainerInfo[];
  metrics: MetricSeries[];
}

export type InstanceLifecycle = "Starting" | "Running" | "Terminating" | "Gone";

export interface MetricSeries {
  name: string;
  labels: Record<string, string>;
  value: number;
}

export type NodeKind = "Service" | "Database" | "Queue" | "External";

export interface Overview {
  scopes: ScopeSummary[];
  alerts: AlertSummary[];
  groupBy: string[];
}

export interface PackageCatalog {
  packages: PackageEntry[];
  services: number;
  servicesWithoutList: number;
}

export interface PackageEntry {
  name: string;
  versions: PackageVersion[];
  violation: string | null;
}

export interface PackageUsage {
  scope: string;
  service: string;
  instances: number;
  direct: boolean;
}

export interface PackageVersion {
  version: string;
  usedBy: PackageUsage[];
}

export interface ScopeConfiguration {
  applied: string | null;
  expected: string;
  inSync: boolean;
}

export interface ScopeDetail {
  name: string;
  health: HealthState;
  services: ServiceDetail[];
  labels: Record<string, string>;
  agentConnected: boolean;
  lastSeen: string | null;
  configuration: ScopeConfiguration;
}

export interface ScopeSummary {
  name: string;
  health: HealthState;
  services: number;
  instances: number;
  problems: number;
  labels: Record<string, string>;
  agentConnected: boolean;
  lastSeen: string | null;
  configurationInSync: boolean;
}

export interface ServiceDetail {
  name: string;
  health: HealthState;
  liveInstances: number;
  healthyInstances: number;
  instances: InstanceDetail[];
}

export interface TopologyEdge {
  from: string;
  to: string;
  type: string;
  calls: number;
  errors: number;
  durationSumMs: number;
}

export interface TopologyGraph {
  nodes: TopologyNode[];
  edges: TopologyEdge[];
}

export interface TopologyNode {
  id: string;
  name: string;
  kind: NodeKind;
  scope: string | null;
  health: HealthState | null;
}

export interface VulnerabilityFinding {
  advisory: string;
  aliases: string[];
  summary: string | null;
  severity: string;
  package: string;
  version: string;
  fixedIn: string | null;
  usedBy: PackageUsage[];
}

export interface VulnerabilityReport {
  findings: VulnerabilityFinding[];
  advisories: number;
  loadedAt: string | null;
}
