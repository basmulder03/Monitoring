# Cross-platform monitoring system: design

Oct 20, 2026 · @Bas Mulder

## Purpose and scope

This is a small, stateless, Native AOT monitoring system for .NET 10 workloads on Kubernetes and Windows. It gives one live dashboard of health, metrics and service dependencies across environments and data centers, with alerting, and without a service mesh or a database. It is generic and centrally configured, so it can be published as open source.

**Goals**

- Show per namespace (Kubernetes) or per server (Windows) whether its services are healthy, down to service and pod level.
- Add monitoring to a workload with a Helm change and a one-line NuGet reference, nothing else.
- Build a service dependency graph from real traffic: HTTP/TCP, ActiveMQ and database calls.
- Recover from any component restart without manual action.
- Stay small: low memory and fast start for every part that runs next to an application.

**Constraints**

| Constraint | Consequence |
| --- | --- |
| .NET 10 LTS, Native AOT | No reflection, source-generated serialization, no SignalR (use Server-Sent Events) |
| No service mesh, no Prometheus stack | Tracing and metrics come from an in-process library, not a proxy |
| Stateless, everything in memory | State is rebuilt from agents on reconnect; nothing is persisted |
| Highly available controller | Two or more identical active replicas, each fed by every agent; no database and no leader for state |
| Multiple environments and data centers | Free-form labels on every scope (for example `env` and `site`); the UI groups and filters by them |

**Non-goals**

- Long-term metric or trace storage, log aggregation, automatic remediation.
- Replacing application-level observability tools; this is a live status and topology view.

## Architecture overview

The system has four components in three tiers: a library and an auto-injected sidecar next to every application, one agent per namespace or Windows server, and a group of controller replicas that serve the web UI. All connections are opened from the lower tier upward, so no tier needs to know addresses of the tier below.

&#91;embedded content: architecture · 4 components, 2 platforms\]

The Kubernetes and Windows scopes look the same to the controller: each has an agent that dials upward, with applications reporting to it.

| Component | Runs as | Responsibility | Holds in memory |
| --- | --- | --- | --- |
| Client library (NuGet) | Inside the application | Exposes info, health, metrics and spans locally; propagates trace context | Span ring buffer, meters |
| Sidecar | Extra container per pod (native sidecar) | Registers the pod, sends heartbeats, probes health, scrapes the library, adds pod metadata | Last collected data |
| Agent | Deployment per namespace; Windows service per server | Tracks pods through the Kubernetes API, pulls data from sidecars, aggregates, streams to the controller | Registry of its scope |
| Controller | Deployment with 2 or more active replicas | Registry of all agents, health roll-up, dependency graph, alert rules, web UI with live push | Everything, all environments |
|  |  |  |  |

**Key principles**

1. Connections only go upward: sidecars dial the agent, agents dial the controllers. No tier needs the address of the tier below, which works across data centers and firewalls.
2. The agent is the source of truth for which instances exist (Kubernetes pod watch or Windows Service Control Manager). Registration adds detail but never decides existence.
3. Agents send a full snapshot on every connection and deltas after that, so any controller replica can be rebuilt from the agents alone.
4. Nothing is stored on disk. Configuration is input, not state.
5. Everything is named by labels and extended through interfaces; nothing is organization-specific.

## How it works

Four processes run continuously: registration and collection, the instance lifecycle, recovery after a controller restart, and the dependency edge pipeline. The sections after this one describe each part in detail.

&#91;embedded content: registration and collection · sequence\]

A sidecar registers once and then sends heartbeats. The agent pulls data on a timer and pushes changes to the controller, which pushes events to dashboards.

&#91;embedded content: instance lifecycle · 6 states\]

A pod delete event moves any state straight to Gone; only then is the instance removed from the live view.

**Pod lifecycle**

1. A pod appears in the watch: the agent creates the instance as Starting, with Kubernetes metadata (node, image, restarts, start time).
2. The sidecar registers: the agent attaches service name, version and health checks, and the instance becomes Healthy or not.
3. The pod is replaced: a delete event removes the old instance at once and the new pod follows steps 1 and 2. No timeout guessing is needed.
4. The pod is terminating: the instance is shown as Terminating; a graceful `Goodbye` from the sidecar avoids a false unhealthy alert during rollouts.

&#91;embedded content: restart recovery · 3 participants\]

Each agent feeds all controller replicas, so a restarted replica only has to reconnect and receive a snapshot.

&#91;embedded content: dependency edge pipeline · 5 steps\]

The controller resolves where each call went using its registry; an unknown address is shown as an external node.

## Discovery and liveness

Each hop finds its parent through one bootstrap value, and the controller hands out the rest. Two independent signals decide whether an instance is alive, so a missing sidecar can never make a pod disappear silently.

**Who finds whom**

| Who | Finds | How |
| --- | --- | --- |
| Library | Sidecar (Kubernetes) | Fixed socket path on the shared volume, set by the Helm template or injector |
| Library | Local agent (Windows) | Fixed named pipe name |
| Sidecar | Agent | Environment variable with the agent's in-namespace Service name; cluster DNS resolves it |
| Agent | Controller replicas | One bootstrap address plus an optional fallback list; the controller returns every replica's own address in `Config`, and the agent connects to all of them |
| Browser or wall display | Controller | One shared URL; any replica serves the UI |

- Replicas need individual addresses (one Route per replica, or per-pod DNS names inside a cluster). A shared load-balanced address would reach only one replica, so it is used only for bootstrap and the UI.
- The replica list is re-sent whenever it changes, so adding or removing a replica needs no agent change.
- Each namespace runs one agent replica. After a restart it rebuilds from the pod list and the sidecars reconnect; a second replica with Lease-based election is possible later.

**Liveness signals**

| Question | Answered by |
| --- | --- |
| Does the pod exist? | Agent pod watch (namespace-scoped `list` and `watch`), with a full re-list every 60 s to repair missed events |
| Is the application alive? | Sidecar heartbeat and the library socket; 3 missed heartbeats means Unhealthy |
| Is the application healthy? | Health checks exposed by the library, probed by the sidecar |
| Is the scope alive? | Agent connection to the controller; 3 missed agent heartbeats means the scope becomes Unknown |
| Which deployment does a pod belong to? | Pod labels and owner references, read by the agent |

The agent needs only a namespace-scoped Role with read access to pods, plus replica sets and services for deployment names and dependency resolution.

## Wire protocol

One message set is used on every hop, serialized as JSON with System.Text.Json source generation and sent as length-prefixed frames over TCP. There is no gRPC: raw TCP with JSON is fully Native AOT compatible and needs no extra framework. Only the transport underneath differs per hop.

| Hop | Transport | Opened by |
| --- | --- | --- |
| Library to sidecar | Unix domain socket on a shared `emptyDir` | Sidecar (library listens) |
| Sidecar to agent | TCP to the agent's in-namespace Service, long-lived | Sidecar |
| Library to Windows agent | Named pipe or localhost TCP | Library |
| Agent to controller | Long-lived TCP connection, both directions; TLS configurable | Agent |

**Messages**

| Message | Direction | Purpose |
| --- | --- | --- |
| `Hello` | Child to parent | Identity: protocol version, instance id, service name, version, labels (for example `environment`, `datacenter`, `namespace`), and for an agent the scope it reports as |
| `Heartbeat` | Child to parent | Every 5 s; sequence number plus a one-byte health summary |
| `Pull` / `PullResult` | Parent to child and back | Request info, health checks, metrics or spans since a cursor |
| `Snapshot` | Agent to controller | Full state of the agent's scope; on connect and every 5 minutes as reconciliation |
| `Delta` | Agent to controller | Only what changed since the last message |
| `Goodbye` | Child to parent | Graceful shutdown, so the entity is removed instead of marked unhealthy |
| `Config` | Controller to agent | Effective configuration and the list of controller replica addresses; on connect and on every change |
| `Ack` | Controller to agent | The last sequence number this replica applied, per agent; also asks for a new snapshot when the replica cannot use deltas yet |

**Defaults (to be tuned)**

- Heartbeat every 5 s; unhealthy after 3 missed heartbeats.
- Protocol versions: `Hello` carries the version, and a peer at most one version away is served, so adjacent versions work together during rollouts.
- Metrics pulled every 15 s; trace edges flushed every 10 s.
- All pulls carry a cursor, so a reconnect never replays data and never loses more than one interval.

**Security**

- Agent to controller: transport security and authentication are separate, pluggable settings (see below).
- In-cluster local hops are plain; they never leave the pod or namespace.
- The web UI has no login for now: it is reachable only on the internal network, and an authentication layer can be added later.

**Transport security and authentication**

Two independent settings, each behind its own interface (`ITransportSecurity`, `IAgentAuthenticator`) and chosen in the central configuration. Authentication is always on; encryption depends on the network.

| Transport mode | What it does | When to use |
| --- | --- | --- |
| `none` | Plain TCP | Closed internal network where the traffic cannot leave it |
| `tls` | Controller has a server certificate; agents verify it against a configured CA certificate | Default for the open-source release; required through OpenShift passthrough Routes |
| `mtls` | TLS plus a client certificate per agent | Strict environments |

| Authentication | How an agent proves who it is | Notes |
| --- | --- | --- |
| `token` (default) | Sends a token in `Hello`, together with the scope it reports as | The controller config holds only token hashes, never a list of agents. An enrollment token (scope `*`) lets any agent that holds it register itself under the scope it announces; a token can also be bound to one scope, so its holders can only report as that scope. Two tokens per scope can be valid during rotation |
| `serviceAccount` | Sends its projected Kubernetes service account token; the controller validates it | No secrets to hand out and automatic rotation; Kubernetes only, and the controller needs access to each cluster's token review or OIDC endpoint |
| `certificate` | The client certificate from `mtls` | Built-in enrollment: the agent generates a key in memory and the controller signs a short-lived certificate after a token check; or certificates from cert-manager or the company PKI |

- Whatever the mode, the controller checks that each `Hello` matches the authenticated identity, so an agent can never report for another scope.
- With `none`, the token travels in clear text. That is acceptable only where the network itself is trusted, and the configuration warns about it at startup.
- The certificate for `tls` comes from the company PKI or cert-manager when available, otherwise from a CA created once at install and stored in a Secret that all replicas mount. Certificates are reloaded when the files change.
- Data protection does not depend on the transport: the library removes query strings and SQL literals from span details by default, and span details travel only when someone opens a drill-down.
- Everything uses .NET's own `SslStream` and certificate APIs, which work with Native AOT; other modes can be added by implementing the interfaces.

**Recommended settings**

| Situation | Transport | Authentication |
| --- | --- | --- |
| One internal network, agents in the same cluster as the controller | `none` | `token` |
| Agents in other clusters or on Windows servers, through Routes | `tls` (one certificate) | `token` |
| Strict or regulated environments | `mtls` | `certificate` |

## Data model and health

Each controller replica holds one in-memory tree: label groups (for example environment, then site), scope (a namespace or a server), service, instance (a pod or a process), container (Kubernetes only). Health rolls up as worst-of, so one failing container turns its instance, service, scope and group tile red.

| Entity | Key fields | Source |
| --- | --- | --- |
| Scope | Display name, kind (namespace or server), labels, agent connection state, last seen | Agent `Hello` and agent configuration |
| Service | Name, version, owner label, health roll-up | Sidecar or library `Hello` |
| Instance | Id, pod or host name, node, image, start time, restarts, IP, health checks, last heartbeat | Sidecar, plus Kubernetes API or Service Control Manager |
| Container | Name, role (app, monitoring sidecar, other sidecar, init), image and digest, state and reason, ready, restarts, last termination reason and exit code, requests and limits, usage | Kubernetes pod watch (container statuses); usage from the pod metrics API (opt-in) |
| Metric series | Name, labels, latest value | Library meters and collectors |
| Edge | Caller service, callee service or resource, type (HTTP, MQ, DB), calls, errors, latency histogram | Library edge counters |
| Alert state | Rule, subject, since, acknowledged | Controller |

**Health states**

- Healthy, Degraded (a non-critical check fails) and Unhealthy (a critical check fails or heartbeats are missed).
- Unknown means the agent for that scope is disconnected. It is shown grey, not red, because the controller cannot tell whether the workload or only the link is down. Alerting escalates Unknown after a configurable time.

**Container level**

Every pod is shown down to its containers, so it is clear whether the application, the monitoring sidecar or another sidecar is the problem.

- The agent reads container statuses from the pod watch it already has, so this needs no extra permission or traffic. Init containers are shown while they run, then folded away.
- Each container gets its own health: Waiting with a reason (for example image pull error or crash loop) is Unhealthy, Running but not ready is Degraded, Terminated after a restart shows the last exit code and reason (for example out of memory).
- The app container is linked to the library's data (health checks, metrics, spans). Other containers have Kubernetes data only, unless they also use the library.
- A failing monitoring sidecar is shown as "monitoring degraded" on the instance, not as an application failure, so a broken sidecar never reports the application as down.
- Usage against requests and limits per container (opt-in, needs read access to the pod metrics API) shows throttling and out-of-memory risk.
- On Windows the level below the instance is the process (threads, handles, memory), shown in the same place.

**Scope names**

- The agent chart has values `scope.displayName` (for example "Orders platform") and an optional `scope.group`. A namespace-scoped Role cannot read namespace objects, so the name is configured, not discovered.
- A tile shows the display name, with the namespace or server name underneath.

**Memory and storage**

- The controller keeps the registry, health, edges and the latest value per metric series. Metric history for sparklines lives in a small ring buffer in each agent, fetched through the controller only while a view is open.
- Spans never reach the controller. Each library keeps the last 2,048 spans, which the agent reads on demand.
- Removed instances are kept 10 minutes as Gone, so the UI can show what disappeared, then dropped.
- Rough example, not a measured figure: 5,000 instances with 50 series each is 250,000 series. Latest values alone take a few tens of MB; 15 minutes of history in the controller would take on the order of 100 MB.
- All controller state goes through one interface (for example `IStateStore`). The default is in memory. SQLite on an `emptyDir` volume survives a container restart but not a reschedule, and with several replicas adds little, so it is only added if the prototype exceeds the memory budget.
- Other levers at large scale: one controller group per environment with a UI that merges them, and coarser pull intervals for large namespaces.

## Tracing and dependency graph

Tracing is done in the application process with W3C Trace Context, and only aggregated caller-to-callee edges leave the agent. That gives a live dependency graph without storing any traces.

| Traffic | Captured by | Context carried in | Callee shown as |
| --- | --- | --- | --- |
| HTTP | Built-in HttpClient and ASP.NET Core activities, read with an `ActivityListener` | `traceparent` header | Service, resolved from the target address |
| Raw TCP | Connect and close events of the library's wrapper | None (TCP has no headers) | Peer address; edge only, no trace link |
| ActiveMQ | Library helper around producer and consumer, registered once at startup | `traceparent` message property | Queue or topic as its own node |
| Database | Provider diagnostics (for example SqlClient) read by the same listener | Not applicable | Database server and name as a resource node |

AOT rules out patching third-party code at runtime, so ActiveMQ needs one registration line (for example `AddMonitoring(o => o.UseActiveMq())`). HTTP and database capture need no code.

**How edges are built**

1. The library updates edge counters (calls, errors, latency histogram) on every completed client span. Counters are never sampled; only span detail is.
2. The agent pulls the counters with the other metrics and sends them as edges, not as spans.
3. The controller resolves each callee address to a registered service using the registry: Kubernetes Service names and pod IPs (from the agent), Windows host names and ports. Unresolved addresses become an external node.
4. A message queue is modeled as service, queue, service, so producer and consumer are both visible.
5. An edge with no calls for 5 minutes fades out, and then disappears.

**Drill-down**

- Each library keeps the last 2,048 spans in memory. The agent can fetch recent slow or failed spans for one instance when someone opens it in the UI.
- Nothing is stored centrally, so a trace that has already scrolled out of the buffer is gone by design.

**Provider extensibility**

- The library has one extension interface (for example `IClientInstrumentation`) that turns a client's activities or events into edges.
- Built in: HttpClient, ASP.NET Core, and the official database and ActiveMQ packages that publish activity sources or diagnostics. No fixed provider list and no reflection.
- Supporting another database or message client means implementing that interface and registering it with one line.

## Windows design

On Windows, one Windows service per server replaces both the sidecar and the namespace agent. It uses the same protocol and the same agent code as on Kubernetes, with a different discovery source: the Service Control Manager instead of the Kubernetes API.

- The server is the scope. Labels (`environment`, `datacenter`, server role) come from the agent's local config file.
- The library connects to the local agent over a named pipe with an access list limited to the service accounts that run monitored applications, then sends `Hello` with process id, service name, executable path and version.
- The agent adds host-level data the application cannot see itself.
- The agent dials the controller exactly like a Kubernetes agent, so the controller treats both kinds of scope the same.

| Data | Source |
| --- | --- |
| Host name, OS version, uptime | Environment APIs |
| CPU, memory, disk | Performance counters |
| Windows service state, start type, recent restarts | Service Control Manager |
| Process memory, threads, handles | Process APIs, by the registered process id |
| Health, metrics, spans | Library, over the pipe |

**Liveness**

- A registered application is alive while its pipe connection and its Windows service are both up.
- A service that is Stopped is Unhealthy with the cause shown (stopped by Service Control Manager), instead of only a missing heartbeat.
- Services on an allowlist in the agent config appear even without the library, as "status only" entries with Service Control Manager state and process data.

**Installation**

- Native AOT single executable, installed as an auto-start service with recovery set to restart.
- Needs only the bootstrap controller address and a client certificate; installed and configured with Ansible playbooks.

## High availability and recovery

Two or more identical controller replicas run active at the same time, all fed by every agent. Losing one changes nothing for the others, and no database or leader is needed.

- The agent pulls from its sidecars once and sends the same snapshots and deltas to every replica, so applications are never polled twice.
- Replicas do not talk to each other. Each acknowledges the last sequence number it applied per agent; when acknowledgements stall, the agent flags that replica and the UI shows "replica is behind". Snapshots every 5 minutes repair any drift.
- Every replica evaluates alerts and shows them in its UI. When external channels are added, one replica sends (chosen by a Kubernetes Lease, which the API server holds) or all send with stable alert keys and the receiver de-duplicates.
- Place replicas in separate data centers. Two are enough to start; agent traffic grows with the number of replicas, memory per replica does not.
- A dead-man's switch: each replica pings an external watchdog every minute, which alerts when all pings stop. This covers the one failure the system cannot report itself.

**What happens when something restarts**

| Component restarts | Result |
| --- | --- |
| Application or library | Sidecar sees the socket drop; instance is marked restarting, then re-registers |
| Sidecar | Agent sees the connection drop; the pod watch confirms the pod still exists, so it is Unhealthy until the sidecar returns |
| Agent | Rebuilds from a Kubernetes pod list plus sidecar re-registrations, then sends a fresh `Snapshot` |
| Controller | That replica loses everything while the others keep serving; agents reconnect to it with backoff and jitter and send a snapshot, so it is complete again within one snapshot interval |
| Pod deleted | Pod watch emits a delete event; the agent removes the instance immediately |

**Why replicas do not sync with each other**

- Agents are the single source of truth, so a replica never merges or trusts another replica's data: no ordering problems, no message loops, no split brain.
- Controller-to-controller sync would need peer discovery, merge rules, loop prevention and gap repair: a second distributed protocol to build and test, for no extra capability.
- Fallback if one connection per agent is ever required (many hundreds of agents): relay mode, where the receiving replica forwards each message to its peers, tagged with agent id and sequence number. Only data agents cannot rebuild, such as alert acknowledgements, would ever need that exchange.

## Central configuration

All behavior is configured centrally in the controller. Agents, sidecars and libraries pick it up when they connect and whenever it changes, and a more specific layer can override a default.

| Kind | Lives in | Examples |
| --- | --- | --- |
| Bootstrap (cannot be central) | Agent or sidecar environment or file | One controller address, certificate, scope name and labels |
| Defaults and policy | Controller config file (ConfigMap), hot-reloaded | Intervals, timeouts, health thresholds, trace sampling, enabled collectors, metric allow and deny lists, alert rules, UI settings |
| Local override (optional) | Agent or library environment | Only the keys the central policy marks as overridable |

**Layers, most specific wins**

1. Built-in defaults, compiled in, so everything works with no controller.
2. Controller defaults.
3. Label match, for example `env=prod` or `site=a`.
4. Scope (one agent).
5. Service.
6. Local override, if allowed.

**Behavior**

- The controller computes the effective configuration per agent and sends it in `Config` on connect and on every change. The agent narrows it per service and instance for the sidecars and libraries.
- Every configuration has a version hash. Agents report the version they applied, so the UI shows drift, and also differences between controller replicas during a rollout.
- The config file is input, not state: all replicas load the same ConfigMap, so they stay consistent without a database. An invalid file is rejected on load and the previous configuration stays active.
- If the controller is unreachable, agents keep the last configuration in memory. After an agent restart they use the built-in defaults until the controller is reachable again.
- Format: JSON (comments allowed), read with source-generated System.Text.Json, so it stays AOT compatible.

Illustrative example:

```json
{
  "defaults": { "heartbeatSeconds": 5, "pullSeconds": 15, "edgeFlushSeconds": 10 },
  "overrides": [
    { "match": { "labels": { "env": "prod" } }, "set": { "pullSeconds": 10 } },
    { "match": { "scope": "orders-platform" }, "set": { "metrics": { "deny": ["http.client.*"] } } }
  ],
  "overridable": ["pullSeconds", "metrics.deny"]
}
```

## Alerting

The controller evaluates alert rules against its in-memory state and shows active alerts in the UI. Because it holds no history, rules are state-based ("is X true for N minutes"), not trend-based.

| Default rule | Fires when | Default wait |
| --- | --- | --- |
| Instance unhealthy | A critical health check fails or heartbeats are missed | 30 s |
| Service down | No healthy instance left | 0 s |
| Scope unknown | The agent stream is down | 2 min |
| Restart loop | An instance restarted 3 times in 10 minutes | 0 s |
| Edge error rate | Errors on one dependency edge above a threshold | 5 min |

**Stateless consequences**

- After a replica restarts, its rules are not evaluated until agents have sent their snapshots (startup grace, about twice the snapshot interval). This prevents a flood of false alerts.
- Alert keys are derived from rule and subject, so a repeated notification after a restart can be recognised by the receiving system.
- Rules and silences are part of the central configuration; a silence therefore has a required expiry and survives restarts.

**Channels**

For now alerts are only shown in the UI: an alert panel with active alerts, severity, subject and how long they have been firing. External channels (webhook, email) come later.

## Self-monitoring

The sidecars, agents and controllers report their own health, metrics and traces through the same pipeline as the applications they watch, and the controller shows them in a dedicated System view. Only the controllers need an outside check: the dead-man's switch.

| Component | Own telemetry | Where it goes |
| --- | --- | --- |
| Library | Buffer use, dropped spans, local socket state | Pulled by the sidecar with the app data, shown on the app container |
| Sidecar | Memory, CPU, start time, pull latency, connection state to library and agent | Sent to the agent over its existing connection, shown on the sidecar container |
| Agent | Pods watched, Kubernetes API watch lag and errors, pull duration, frames sent, queue size, connection state per replica, applied configuration version | Sent in its own snapshots and deltas, so every replica has it |
| Controller replica | Agents connected, frames per second, apply lag, memory per entity type, event stream clients, alert evaluation time | Kept locally and shared with the other replicas through the agents' acknowledgement data (see below) |

**How it is modeled**

- The monitoring system is a reserved scope (for example "Monitoring system") with the controller replicas and agents as its services. Sidecars stay on their own pods, as the monitoring sidecar container.
- The components use the same library internally, so their internal steps (pull, apply, push) are traced with the same `ActivityListener` model. Their edges (sidecar to agent to controller) form the system's own dependency graph.
- Each agent reports which replicas acknowledge it and how far behind they are. Because every replica receives this from every agent, each replica can show the state of all replicas, without replicas talking to each other.
- Self-telemetry uses the same memory bounds and pull intervals, configurable centrally, so monitoring the monitor does not become the biggest consumer.

**System view in the UI**

- A System tile in the Overview: green when all agents are connected to all replicas, configuration versions match, and nothing is dropping data.
- Detail: replicas with connected agents and lag; agents with watch health, pull duration and configuration version; sidecars that are failing or missing ("unmonitored" pods).
- The same alert rules apply, with defaults for agent disconnected, replica behind, configuration drift and dropped data.

**Export (optional)**

Every component can also expose its own metrics in Prometheus text format on a local port, off by default. This costs little and lets the company's future observability stack scrape the monitoring system without any change to it.

## Dashboard UI

The UI is a static single-page app embedded in the controller. It is built to be read from across a room on a large TV, rotating between dashboards, and it updates live without reloads.

**Views**

| View | Shows | Interaction |
| --- | --- | --- |
| Overview | One tile per scope (namespace or server), grouped by configured label keys (for example environment, then site); unhealthy tiles first | Open a tile |
| Scope | Services, expandable to instances and their containers; failing health checks and container reasons with their message | Expand, filter |
| Dependencies | Service graph with HTTP, queue and database nodes; edge thickness is call volume, red edges have errors | Select a node |
| Instance | Info, containers with state and restarts, health checks, metric sparklines, recent slow or failed spans | Back |

An environment switcher selects one environment or all of them (development, test, acceptance, production).

**TV mode** (`/tv?env=prod&rotate=60&views=overview,dependencies&theme=dark`)

- No input needed: the page rotates through the chosen views on a timer and hides the cursor.
- Focus mode: when a scope turns Unhealthy, the rotation stops and the page opens that scope with the failing service expanded. It returns to the rotation once the problem has cleared, or after a configurable time.
- Type is sized to the viewport, so a tile status is readable at several meters on a 1080p or 4K screen.
- A visible "data is N seconds old" banner appears when no update arrived for 15 s, so a frozen screen never shows stale green.
- Optional slow layout drift to avoid screen burn-in.

**Live updates**

- The controller pushes changes over Server-Sent Events, coalesced to about one batch per second. SignalR is avoided because it is not AOT-friendly.
- Every controller replica serves the same UI behind one shared URL. On connect and on reconnect the page fetches the full state first, then applies events, so it does not matter which replica it lands on and no sticky sessions or manual refresh are needed. Wall displays can list fallback URLs per data center.
- The graph layout is recomputed only when the topology changes, so nodes do not jump on a wall display.

**Accessibility (WCAG 2.0 AA minimum)**

- Text contrast at least 4.5:1 (3:1 for large text and icons), in both themes; checked automatically in the tests.
- Status is never color alone: every state has its own icon and a text label.
- Light and dark themes through CSS design tokens; the default follows the system setting, and the TV URL can force one.
- Full keyboard use with a visible focus ring; status changes announced through a polite live region.

**Icons**

One consistent outline icon set as an inlined SVG sprite (no font or network dependency), for example Tabler or Lucide, both MIT licensed.

| Concept | Icon idea |
| --- | --- |
| Environment, data center | Layers, building |
| Namespace, Windows server | Box grid, server |
| Service, instance (pod, process) | Cube, small cube |
| Database, queue, external system | Cylinder, stacked messages, globe |
| Healthy, Degraded, Unhealthy, Unknown | Check circle, warning triangle, cross octagon, question dashed circle |
| Alert, silenced, theme toggle | Bell, bell-off, sun and moon |

## Additional data worth tracking

These extend the base data model. "On" means collected by default; "opt-in" is enabled through central configuration.

| Area | Data | Why it helps | Default |
| --- | --- | --- | --- |
| Kubernetes | Last termination reason (for example OOM killed), restart count | Explains crashes without opening logs | On |
| Kubernetes | Desired versus ready replicas, rollout status | Shows stuck or partial rollouts | On |
| Kubernetes | Warning events for the scope's pods (image pull, scheduling) | Explains pods that never start | On |
| Kubernetes | CPU and memory requests and limits against usage | Spots throttling and out-of-memory risk; needs read access to the pod metrics API | Opt-in |
| Kubernetes and Windows | Image tag or file version per instance | Marks version changes on the timeline | On |
| .NET runtime | GC heap size, gen 2 collections, pause time, thread pool queue length, exception count, lock contention | Early warning of leaks and starvation; from the built-in runtime meters | On |
| Requests | Rate, errors and duration per service and route | Standard service health view | On |
| Dependencies | Database connection pool usage; queue depth, consumers and dead-letter count | Shows saturation before failure; queue depth needs broker access | Opt-in |
| Certificates | Expiry of TLS certificates an app serves or uses | Prevents expiry outages | Opt-in |
| Host | Disk space, clock skew, uptime; on Windows pending reboot and service recovery restarts | Common root causes outside the app | On |
| Change markers | Version or configuration changes as events | Correlates problems with changes | On |
| System itself | Dropped frames, queue sizes, applied configuration version, replica connection state | Lets you trust the monitoring | On |

## Deployment and packaging

Every runtime component is a Native AOT executable on .NET 10, and one shared protocol project keeps all hops in sync.

| Project | Type |
| --- | --- |
| `Monitoring.Protocol` | Shared messages, frame codec, health and status model |
| `Monitoring.Client` | NuGet library: meters, activity listener, health, local server |
| `Monitoring.Sidecar` | AOT executable, Linux container |
| `Monitoring.Agent` | AOT executable; Linux container and Windows service |
| `Monitoring.Controller` | AOT executable with embedded web UI |
| `Monitoring.Ui` | Static assets embedded in the controller |
| `Monitoring.AppHost`, `Monitoring.Tests.*`, `TestApi`, `TestWorker`, `TestData` | Aspire host, tests and test applications |

**AOT rules**

- Every project sets the AOT-compatibility analyzer; trim and AOT warnings fail the build.
- Serialization is source-generated System.Text.Json only; no reflection-based libraries.
- The official Kubernetes client is reflection-heavy, so the agent talks to the Kubernetes API with a small `HttpClient` wrapper and source-generated JSON for pod and service list and watch.
- Live updates use Server-Sent Events, not SignalR.
- No gRPC: every hop uses length-prefixed JSON frames over TCP.

**Helm**

- A named template (for example `monitoring.sidecar`) that application charts include once. It adds the sidecar as a native sidecar container, a shared `emptyDir` for the socket, and the Downward API variables (pod name, pod IP, node, namespace).
- Values: `monitoring.enabled` and free-form `monitoring.labels`; nothing else per application.
- A separate agent chart per namespace: Deployment, Service, ServiceAccount, and a Role limited to reading pods (and services).
- A controller chart with the certificate secret, the central configuration ConfigMap, one Route per replica, and a shared Route for the UI.
- Small resource requests and limits by default, tightened once the budgets from the testing section are known.

**Sidecar injection**

Start with the Helm template; a label-based admission webhook (the way service meshes inject) can be added later as a separate small injector. Both build the pod change from one shared definition, so they cannot drift apart.

|  | Helm template | Admission webhook (label) |
| --- | --- | --- |
| Opt-in | Include the template in each chart | A label on the pod or namespace, for example `monitoring/inject=true` |
| Cluster privileges | None | A cluster-scoped webhook registration, installed by a cluster admin |
| Updates | Bump every chart | Change once, then roll the pods |
| Failure mode | None at runtime | Webhook down: with failure policy Ignore, pods start unmonitored |
| Apps you do not own | Needs a chart change | Works unchanged |

- The agent cannot inject itself: it has only namespace permissions, and webhook registrations are cluster-scoped.
- Injection only changes the pod; the application still needs the NuGet reference for in-process data. The agent marks pods without a sidecar as "unmonitored".
- On OpenShift the webhook's serving certificate can come from the OpenShift service CA.

**OpenShift**

- Pods run under the restricted security context with an arbitrary non-root user. Sidecar and agent images run as non-root, write only to the `emptyDir`, and make the socket group-writable (OpenShift runs arbitrary users in group 0).
- Native sidecars need Kubernetes 1.29 or newer, which is OpenShift 4.16 or newer. On older versions the Helm template falls back to a normal second container, and the sidecar retries until the library is up.
- Routes carry HTTP and TLS only. Agents in other clusters reach the controller through a TLS passthrough Route, which needs transport mode TLS; agents in the controller's own cluster can use the Service directly, where mode none works. The web UI uses a normal Route limited to the internal network.
- Network policies must allow pod to agent Service inside the namespace, and agent egress to every controller replica.
- Windows agents are installed and configured with Ansible playbooks, and connect to the same controller Routes.

## Generic design and open source

Nothing in the design is specific to one organization: everything is named by labels, configured centrally and extended through interfaces, so the project can be published as open source.

**Making it generic**

- A scope is any group of services that report through one agent: a namespace, a server, later a Docker host or a systemd machine.
- Labels are free key-value pairs on scopes, services and instances. `environment` and `datacenter` become recommended labels, not fixed fields, and the UI groups by configured label keys (for example `env`, then `site`).
- The `Hello` message carries a protocol version, so agents and controllers of adjacent versions work together during rollouts.
- No organization-specific names, hosts or defaults in code or charts. Platform notes (OpenShift, Ansible) live in separate deployment guides.

| Extension point | Built in | Possible later |
| --- | --- | --- |
| Scope provider | Kubernetes, Windows Service Control Manager | Docker, systemd |
| Client instrumentation | HTTP, ASP.NET Core, database and ActiveMQ via official packages | Other brokers and databases |
| Collector | .NET runtime, host, pod data | Custom collectors |
| Alert sink | UI alert panel | Webhook, email, chat |
| State store | In memory | SQLite |

**Publishing**

- License: Apache-2.0 is suggested (patent grant, widely accepted by companies); MIT is the simpler alternative.
- Packages: NuGet for the library and extensions; container images for sidecar, agent, controller and injector; Helm charts as OCI artifacts; the Windows agent as a zip or MSI.
- Repository: README with a five-minute quickstart (an Aspire sample), this design as architecture docs, contributing guide, security policy, semantic versioning, changelog, and CI that runs the AOT and end-to-end tests.
- Pick a neutral working name and check trademarks and package-name availability before the first publish.
- Before publishing, confirm with your employer who owns the code and whether open-sourcing it is allowed, since it is being designed for work.

## Code structure

The code is organized in vertical (functional) slices: each feature owns everything it needs, from message handling and endpoints to state and tests, instead of shared technical layers such as `Services`, `Repositories` or `Controllers` folders. A change to one feature then touches one folder.

**Slices per executable**

| Executable | Slices |
| --- | --- |
| Controller | `AgentLink` (accepts agent connections, applies their snapshots and deltas, acknowledges them), `Registry`, `Health`, `Topology`, `Metrics`, `Alerting`, `Configuration`, `Security`, `Replication` (replica state and lag), `Dashboard` (API and event stream), `SystemMonitoring` |
| Agent | `ScopeState` (the in-memory record of the scope and the source of snapshots and deltas), `Discovery` (Kubernetes or Service Control Manager), `SidecarConnections`, `Collection`, `ControllerLink`, `Configuration`, `Security`, `SystemMonitoring` |
| Sidecar | `Registration`, `Probing`, `LibraryLink`, `AgentLink` |
| Library | `Health`, `Metrics`, `Tracing`, `Edges`, `Instrumentation` (HTTP, database, ActiveMQ), `LocalServer` |

Example layout of one slice:

```text
Monitoring.Controller/
  Features/
    Alerting/
      AlertingSlice.cs          // AddAlerting() and MapAlerting()
      AlertRule.cs
      AlertEvaluator.cs
      AlertState.cs
      AlertEndpoints.cs
      AlertingOptions.cs
    Health/
    Topology/
  Shared/                       // only true cross-cutting code: clock, logging, frame transport
  Program.cs                    // composes the slices

Monitoring.Controller.Tests/
  Features/
    Alerting/                   // mirrors the slice
```

**Rules**

- Each slice exposes one registration method (`AddAlerting`) and, when it has endpoints, one mapping method (`MapAlerting`). `Program.cs` only composes slices.
- Slices do not reach into each other's internals. They talk through small public contracts or in-process domain events (for example `InstanceHealthChanged`, raised by Health and consumed by Alerting and Dashboard).
- The shared kernel is small: `Monitoring.Protocol` (messages and serialization contexts) plus abstractions such as `TimeProvider`, `IStateStore` and the extension interfaces. Anything feature-specific stays in its slice.
- Native AOT friendly: explicit registration instead of assembly scanning, minimal API endpoints with the request delegate generator, source-generated JSON contexts per slice, and no reflection-based mediator libraries. In-process events use a small hand-written dispatcher.
- One type per file, named after the type (`AlertRule.cs` holds `AlertRule`), so each file has a single responsibility and is easy to find and review. Small private helper types may sit next to the one type that uses them; anything used by more than one type gets its own file. Test classes follow the same rule: one test class per file, named `<Type>Tests`.
- Classes are `internal` and `sealed` by default; only a slice's contract is public. Options are validated at startup.
- Architecture tests (in the test projects, which do not need AOT) fail the build when a slice references another slice's internals or when shared code depends on a slice.
- Tests mirror the slices: unit tests next to the slice's folder in the test project, and Gherkin feature files grouped by the same functional areas, so requirements, code and tests line up one to one.

**Documentation and comments**

Code is documented in two layers: C# XML documentation (csdoc) for what a member is and promises, and inline comments for why the code does something non-obvious.

- Every public type and member has XML documentation, since a slice's contract and the shared kernel are the API surface. This includes the NuGet library, where the documentation ships with the package. Internal code gets it when its purpose or contract is not clear from the name.
- Follow the standard conventions:
  - `<summary>` is one short sentence about what the member is or does, not how.
  - `<param>`, `<returns>` and `<exception>` state meaning, units, ranges, nullability and failure modes, not just repeated names.
  - `<remarks>` holds threading, ordering and lifetime guarantees (for example "called from the pull loop only").
  - `<see cref="..."/>` links to related types instead of plain text names.
  - `<inheritdoc/>` is used for overrides and interface implementations that add nothing.
- Wire-protocol messages document each field, including units and what a missing value means, because the doc is the protocol specification for other implementations.
- Inline comments explain intent, constraints and trade-offs: why a choice was made, an invariant, a workaround for an AOT or platform limitation, or a reference to the section of this design. They do not restate what the code already says.
- Comment only where the code cannot say it itself. Rename or restructure first; add a comment if that is still not enough. No commented-out code and no change-history comments (version control holds that).
- Comments are kept in step with the code: a change that makes a comment wrong updates or removes it in the same commit.
- The build enforces the baseline: XML documentation generation is on and missing-documentation warnings for public members (CS1591) are errors in the library and protocol projects. Review checks the quality of the text.

**Commits**

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/), so the history is readable and the changelog and semantic version can be generated from it.

- Format: `<type>(<optional scope>): <description>`, with an optional body that explains why, and footers such as `BREAKING CHANGE:`.
- Types: `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `ci`, `chore`. A breaking change adds `!` after the type or scope, or a `BREAKING CHANGE:` footer.
- Scope is the slice or project, for example `feat(controller-alerting): ...` or `fix(protocol): ...`.
- The description is imperative, lower case, without a final period, and short enough to read in one line.
- One commit per functional slice: a commit contains one coherent change together with its tests and documentation, and the build and tests are green at every commit. Unrelated changes, such as a refactor found along the way, get their own commit.

## Testing strategy

Behavior is specified in Gherkin and verified end to end against a real multi-platform environment started by .NET Aspire, while xUnit v3 unit tests cover the logic in isolation. The Gherkin feature files double as the functional requirements.

| Layer | Tooling | Covers |
| --- | --- | --- |
| Unit | xUnit v3, a readable assertion library, `TimeProvider` with `FakeTimeProvider` | Health roll-up, edge aggregation, alert rules and grace periods, protocol codecs, SSE batching |
| Component | xUnit v3, in-process agent and controller, a fake Kubernetes API (pod list and watch) | Registration, pod lifecycle, stale cleanup, snapshot recovery |
| End to end | Reqnroll (Gherkin), Aspire testing host | Complete flows across controller, agents, sidecars and test applications |
| UI | Playwright for .NET, axe-core | Live updates, TV mode, themes, WCAG checks |
| AOT | CI publishes every component with Native AOT, trim and AOT warnings as errors, then smoke-tests the binaries | Failures that only appear in the published binary |

**Unit test rules**

- A unit test tests one unit (a class or a small cohesive set of functions) in isolation and checks one behavior. Collaborators outside the unit are replaced by simple fakes; time comes from `FakeTimeProvider`.
- Every test follows Arrange-Act-Assert, in that order, marked with `// Arrange`, `// Act` and `// Assert` comments. Arrange builds the inputs and the unit, Act is a single call on the unit, and Assert checks the outcome. A test never acts twice, and an assertion never sits in Arrange or Act. A trivial Arrange may say so in its comment instead of being left out.
- The same behavior with different data is one parameterized test, not several copies: `[Theory]` with `[InlineData]`, or `[MemberData]` and `TheoryData<T>` for complex inputs. Each data row is a case the test name still describes.
- Test names state the behavior and the condition, for example `ReadAsync_rejects_a_frame_cut_short`, so a failing name tells what broke.
- Assertions use Shouldly for readability. Each value gets its own assertion line, so a failure names the value that is wrong, and several values are never packed into a tuple to compare in one go; `ShouldSatisfyAllConditions` is for when a failure should report every broken value at once. Tests that depend on private state, the order of internal calls or a real clock, network or file are not unit tests; they belong in the component or end-to-end layers.
- Set-up that is more than a line or two lives in a fixture that uses the builder pattern: a small class with fluent `With...` methods and a `Build` method, so each test states only what matters for its case (`new FrameStreamBuilder().WithMessage(hello).WithoutLastBytes(2).BuildAsync()`). A fixture needed by one test class is a `private sealed` nested class of that class. A fixture needed by several test classes is shared at the narrowest level that covers them: in a `Support` folder next to the slice's tests when one slice uses it, or in the test project's `Shared` folder when several slices do, as an `internal sealed` class. There are no base test classes and no fixture with hidden mutable state.

Time is always injected through `TimeProvider`, so heartbeat timeouts, grace periods and rotation timers are tested instantly and deterministically.

**End-to-end environment**

The Aspire app host starts the controller, one or more agents, test applications and their dependencies (an ActiveMQ broker, a database, a fault-injection proxy). Tests drive it through the same ports a real deployment uses.

| Tier | Environment | When |
| --- | --- | --- |
| Fast | Processes and containers with the real sidecar, agent and controller, using the fake Kubernetes API | Every pull request |
| Full Kubernetes | A throwaway cluster (kind or k3d) deployed with the real Helm chart | Nightly |
| Windows | A Windows runner with the agent and a test app installed as real Windows services; scenarios tagged `@windows` | Nightly, and on changes to the Windows agent |

**Test applications (also AOT)**

- `TestApi`: HTTP service with endpoints to set health, add latency and return errors on demand.
- `TestWorker`: ActiveMQ producer and consumer.
- `TestData`: service that calls a database.
- A call chain of `TestApi` to `TestWorker` to `TestData` through the queue, so the expected dependency graph is known exactly.
- A control API to kill a process, break a health check or cut the network, so scenarios can cause real failures.

**Example scenarios**

```gherkin
Feature: Pod lifecycle

  Scenario: A replaced pod disappears immediately
    Given the namespace "shop" is registered with 2 healthy instances of "orders"
    When one pod of "orders" is deleted
    Then the dashboard shows 1 instance of "orders" within 5 seconds
    And the service "orders" is Healthy

  Scenario: A restarted controller recovers without help
    Given 3 namespaces are registered in "production"
    When the controller is restarted
    Then all 3 namespaces are shown again within 30 seconds
    And no alert was sent during the restart

  Scenario: The dependency graph follows real traffic
    Given TestApi calls TestWorker through the queue "orders", and TestWorker calls the database
    When 20 requests are sent to TestApi
    Then the graph contains the edges TestApi to queue to TestWorker to database
```

**Feature areas to cover**

- Registration and heartbeats; unhealthy detection; pod replacement; graceful shutdown during a rollout.
- Namespace loss (agent down) shown as Unknown, then escalated; container-level states (crash loop, out of memory, failing sidecar); System view and self-monitoring alerts.
- Controller restart; agent restart; reconnect with backoff.
- Multiple environments and the environment switcher.
- Dependency graph for HTTP, queue and database traffic.
- Alert firing, de-duplication, silence expiry and the startup grace period.
- TV mode: rotation, focus mode on failure, stale-data banner, light and dark themes, contrast and keyboard checks.
- Budgets: sidecar and library memory and start time stay under agreed limits (numbers to be set after the first prototype).

## Risks and open questions

The biggest risk is that automatic capture is limited by Native AOT, so spikes on database diagnostics and ActiveMQ should come before any broad build.

| Risk | Mitigation |
| --- | --- |
| Sidecar cannot see application traffic without a mesh | Capture in the library; sidecar handles registration, heartbeat and probing |
| JSON frames are larger than a binary format | Deltas instead of full state, batching, a frame size limit; measure in the prototype |
| Database and ActiveMQ spans differ per provider | Use the official packages' own activity sources; one extension interface for the rest |
| All controller replicas down at once blinds the dashboards | Replicas in separate data centers, fast recovery from agent snapshots, external dead-man's switch |
| Controller memory grows with many series | Only latest values in the controller, history in the agents; memory budget tested in CI |
| Alert flood after controller restart | Startup grace period and stable alert keys |

**Open questions**

- [ ] Agent scope (confirmed, no action needed): The design assumes per namespace.
- [ ] Authentication for the UI and alert channels beyond the UI: both deliberately later.
- [ ] Is every controller replica reachable from every cluster and Windows server through a TLS passthrough Route?
- [ ] Windows agents: which Ansible playbook flow and package source will be used to install them?
- [ ] Which database and ActiveMQ libraries are in use? Not blocking: the extension interface covers others.
- [ ] Expected scale (pods per namespace, namespaces, servers): measure in the prototype, then set memory budgets and intervals.
- [ ] OpenShift version: native sidecars need 4.16 or newer, otherwise the fallback applies.
