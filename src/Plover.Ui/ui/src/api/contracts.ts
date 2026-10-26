// GENERATED from the C# types of the dashboard API. Do not edit.
// Regenerate with: UPDATE_CONTRACTS=1 dotnet test --project tests/Plover.Ui.Tests

export interface Acknowledgement {
  key: string;
  by: string;
  at: string;
  note: string | null;
  removed: boolean;
}

export interface AlertOccurrence {
  key: string;
  rule: string;
  subject: string;
  severity: AlertSeverity;
  started: string;
  ended: string | null;
  endedBy: string | null;
}

export type AlertSeverity = "Warning" | "Critical";

export interface AlertSummary {
  key: string;
  rule: string;
  subject: string;
  severity: AlertSeverity;
  since: string;
  acknowledged: Acknowledgement | null;
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

export type ContainerRole = "App" | "PloverSidecar" | "OtherSidecar" | "Init";

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

export interface MetricHistory {
  intervalSeconds: number;
  series: SeriesHistory[];
}

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
  /** Left out by the controller when there is none. */
  violation?: string | null;
}

export interface RulesReport {
  alerts: { name: string; severity: string; waitSeconds: number }[];
  aligned: { package: string; rule: string; description?: string | null }[];
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

export interface ReplicaState {
  address: string;
  thisReplica: boolean;
  agentsReporting: number;
  agentsConnected: number;
  maxLag: number;
}

export interface RuntimeSilence {
  id: string;
  rule: string;
  subjectPrefix: string;
  until: string;
  reason: string | null;
  by: string;
  updatedAt: string;
  removed: boolean;
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

export interface SeriesHistory {
  name: string;
  labels: Record<string, string>;
  values: number[];
}

export interface ServiceDetail {
  name: string;
  health: HealthState;
  liveInstances: number;
  healthyInstances: number;
  instances: InstanceDetail[];
}

export interface SystemStatus {
  replica: string;
  uptimeSeconds: number;
  scopes: number;
  agentsConnected: number;
  eventClients: number;
  alertEvaluationMilliseconds: number | null;
  memoryBytes: number;
  healthy: boolean;
  replicas: ReplicaState[];
  unmonitored: UnmonitoredInstance[];
  framesPerSecond: number;
  instances: number;
  series: number;
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

export interface UnmonitoredInstance {
  scope: string;
  service: string;
  instance: string;
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

export interface WhoAmI {
  authenticationEnabled: boolean;
  name: string;
  role: string;
}
