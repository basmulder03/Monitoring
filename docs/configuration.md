# Configuration reference

The controller and the agent read their bootstrap settings from the same sources. Anything not set keeps its default.

## Sources

From lowest to highest priority, so a later source overrides an earlier one:

1. A JSON file. By default `monitoring.json` in the current directory; set `MONITORING_CONFIG_FILE` to use another path. The file is optional, and comments are allowed.
2. Environment variables starting with `MONITORING_`. A nested setting uses a double underscore: `Agent:ScopeName` becomes `MONITORING_Agent__ScopeName`.
3. Command line arguments: `--Agent:ScopeName=shop`.

A list can be written as a JSON array, as indexed variables (`MONITORING_Agent__Controllers__0=...`), or as one comma-separated value (`MONITORING_Agent__Controllers=a:9440,b:9440`).
Durations are written as `hh:mm:ss`, for example `00:00:30`; fractions of a second use a dot (`00:00:00.250`).

Logging is controlled by the usual `Logging:LogLevel` settings, for example `MONITORING_Logging__LogLevel__Default=Debug`.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Stopped normally |
| 1 | Could not start, for example the port is already in use |
| 78 | A setting is missing or invalid; the message names the setting |

## Controller

| Setting | Default | Meaning |
| --- | --- | --- |
| `Controller:Listen:Address` | `0.0.0.0` | IP address agents connect to |
| `Controller:Listen:Port` | `9440` | TCP port agents connect to |
| `Controller:Replicas` | none | The individual `host:port` address of every controller replica, handed to agents so they connect to all of them. List every replica, including this one: agents follow exactly this list |
| `Controller:AgentTokens` | required | The accepted agent tokens, see below. At least one |
| `Controller:Transport:Mode` | `none` | How agent connections are protected: `none`, `tls` or `mtls`, see below |
| `Controller:Transport:Certificate` | none | PEM file with the server certificate and its chain. Required for `tls` and `mtls` |
| `Controller:Transport:Key` | none | PEM file with its private key; may be left out when the certificate file holds the key too |
| `Controller:Transport:ClientCaFile` | none | PEM file with the certificate authority that signs agent certificates. Required for `mtls` |
| `Controller:Transport:HandshakeTimeout` | `00:00:10` | How long a TLS handshake may take before the connection is dropped |
| `Controller:AgentLink:HelloTimeout` | `00:00:10` | How long a new connection may stay silent before it must say hello |
| `Controller:Web:Address` | `0.0.0.0` | IP address the dashboard's web server listens on |
| `Controller:Web:Port` | `8080` | TCP port of the dashboard and its API; plain HTTP, so keep it on a network you trust |
| `Controller:Dashboard:PushInterval` | `00:00:01` | How often open pages are checked for changes to push; bursts of changes become one update |
| `Controller:Dashboard:GroupBy` | `environment,datacenter` | The scope labels the page groups scopes by, outermost first. Scopes without a label are listed last under "not set". `none` turns grouping off |
| `Controller:Dashboard:KeepAliveInterval` | `00:00:05` | How long a stream may stay silent before a keep-alive event is sent. Keep it well below 15 seconds, after which a page warns that its data is old |
| `Controller:Configuration:File` | none | The central configuration file, see below. Without it the built-in defaults apply |
| `Controller:Configuration:PollInterval` | `00:00:05` | How often the file is looked at for a change |
| `Controller:Topology:SampleInterval` | `00:00:05` | How often the dependency graph is updated from what the instances report |
| `Controller:Topology:EdgeFadeAfter` | `00:05:00` | How long an edge may go without a new call before it leaves the graph |
| `Controller:Liveness:HeartbeatInterval` | `00:00:05` | How often agents are told to send a heartbeat |
| `Controller:Liveness:MissedHeartbeats` | `3` | How many heartbeats in a row may be missing before an agent counts as gone (15 seconds by default). A connection that is silent that long is closed |
| `Controller:Alerting:ScopeUnknownWait` | `00:02:00` | How long the agent of a scope must stay gone before the critical `scope-unknown` alert fires |
| `Controller:Registry:GoneRetention` | `00:10:00` | How long a removed instance stays visible as Gone |
| `Controller:Registry:PurgeInterval` | `00:01:00` | How often expired Gone instances are dropped |
| `Controller:Alerting:StartupGrace` | `00:10:00` | After startup no alert rule is evaluated for this long, so agents can send their snapshots first |
| `Controller:Alerting:InstanceUnhealthyWait` | `00:00:30` | How long an instance must stay Unhealthy before its alert fires |
| `Controller:Alerting:RestartLoopCount` | `3` | How many restarts of an instance within the window raise the `restart-loop` warning. Counted from when the controller first saw the instance, so an old restart count alone never raises it |
| `Controller:Alerting:RestartLoopWindow` | `00:10:00` | The time within which that many restarts count as a loop |
| `Controller:Alerting:CertificateWarningDays` | `14` | A watched certificate with fewer days left raises the warning `certificate-expiring` |
| `Controller:Alerting:CertificateCriticalDays` | `3` | With fewer days left, or already expired, it raises the critical alert `certificate-expired-soon` instead |
| `Controller:Alerting:QueueWithoutConsumersWait` | `00:02:00` | How long a watched queue with waiting messages must stay without a consumer before the critical alert `queue-without-consumers` fires. The warning `queue-dead-letters` fires as soon as a watched queue has dead letters |
| `Controller:Alerting:ConnectionPoolFullWait` | `00:01:00` | How long a database connection pool must stay without a free connection before the warning `connection-pool-full` fires |
| `Controller:Alerting:ReplicaLagThreshold` | `25` | A replica that has not acknowledged this many changes of a scope that an agent sent raises the warning `replica-behind` |
| `Controller:Alerting:ReplicaBehindWait` | `00:01:00` | How long a replica must stay behind before that alert fires |
| `Controller:Alerting:ReplicaUnreachableWait` | `00:02:00` | How long an agent may be unable to reach a replica before the warning `replica-unreachable` fires |
| `Controller:Alerting:EdgeErrorWindow` | `00:05:00` | The window in which the calls of a service to a dependency are looked at for failures |
| `Controller:Alerting:EdgeErrorShare` | `0.5` | The share of those calls that must fail to raise the warning `edge-error-rate`, from 0 to 1 |
| `Controller:Alerting:EdgeErrorMinCalls` | `20` | The fewest calls within the window that can raise it, because a few failures of few calls say nothing |
| `Controller:Alerting:ConfigurationDriftWait` | `00:10:00` | How long an agent may use another configuration than expected before the warning `configuration-drift` fires; a rollout passes through this state |
| `Controller:Alerting:DroppedDataWindow` | `00:10:00` | The window in which spans thrown away by an application's client are counted |
| `Controller:Alerting:DroppedDataSpans` | `100` | How many it may throw away within the window before the warning `dropped-data` fires: its buffer is too small for the traffic, and the spans someone opens in the dashboard are not all that happened |
| `Controller:Alerting:ServiceDownWait` | `00:00:00` | How long a service must have no instance able to serve before its alert fires |
| `Controller:Alerting:EvaluationInterval` | `00:00:05` | How often the alert rules run |

### Central configuration

How often agents read what their applications report is decided centrally, not on each agent. The controller reads a JSON file (comments and trailing commas are allowed), typically a mounted ConfigMap, and hands each agent its settings when it connects and again whenever the file changes. Nothing is stored: every replica reads the same file, so they agree without talking to each other.

```json
{
  "defaults": { "pullSeconds": 15, "edgeFlushSeconds": 10 },
  "overrides": [
    { "match": { "labels": { "environment": "prod" } }, "set": { "pullSeconds": 10 } },
    { "match": { "scope": "orders-platform" }, "set": { "edgeFlushSeconds": 5 } },
    { "match": { "service": "reports" }, "set": { "pullSeconds": 120 } },
    { "match": { "scope": "orders-platform", "service": "reports" }, "set": { "pullSeconds": 90 } }
  ],
  "overridable": ["pullSeconds"]
}
```

| Setting | Default | Meaning |
| --- | --- | --- |
| `pullSeconds` | `15` | How often an agent reads the metrics of its applications |
| `edgeFlushSeconds` | `10` | How often an agent reads the calls of its applications to their dependencies |

Layers, the most specific winning: the built-in defaults, then `defaults`, then every override whose `labels` all match the scope, then every override whose `scope` matches (and whose labels, if given, match too), then every override for a `service`, and last a local override on the agent. Within the same kind, a later rule wins. A rule has to match on a scope, a service or labels; use `defaults` for everyone.

- **A service** narrows a rule to the applications of that service: `{ "match": { "service": "reports" }, "set": { "pullSeconds": 120 } }` makes the agents pull the metrics of the `reports` application every two minutes and leaves every other service alone. A service rule can also name a scope or labels, and then only counts on the agents that match them. The agent reads what is set for a service as the application introduces itself.
- **A local override** is set on the agent itself, for example `Agent:Overrides:PullSeconds` or `MONITORING_Agent__Overrides__PullSeconds`, and wins over everything above, but only for the settings that the central file lists under `overridable` (`pullSeconds`, `edgeFlushSeconds`). The central file stays in charge: a setting it does not list cannot be changed on the machine, and an agent that has not heard from a controller yet ignores its local overrides.

- Each agent gets a configuration with a version that changes whenever any of its settings does, and applies it without a restart. An agent that cannot reach a controller keeps the last one; after a restart it uses the built-in defaults until a controller answers.
- A file that cannot be read, is not valid JSON, has a name that is not a setting or a value outside 1 to 3600 seconds is rejected as a whole, the error is logged, and what was in force stays in force. Fix the file and it is picked up at the next look.
- Agents say in every heartbeat which version they use. When it differs from what the controller would give that agent now, the scope view says so and the tile shows "Configuration not yet up to date". A moment after a change this is normal; if it stays, the agent is not receiving it. Replicas that run different files during a rollout show up the same way.
- The heartbeat interval is not part of it: it belongs together with how long silence is tolerated, and is set with `Controller:Liveness`.

### Aligned packages

Different services may use different versions of a library, and often that is fine. For libraries that should match, such as a family of framework packages or your own shared libraries, list them. The packages view then marks a package whose versions are further apart than allowed, and the warning `package-drift` fires after `PackageDriftWait`, which is long by default because a rollout of a library passes through this state.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Controller:Packages:Aligned:<n>:Package` | required | A package id, or a prefix ending in `*`, such as `Microsoft.Extensions.*`. The first rule that covers a package decides |
| `Controller:Packages:Aligned:<n>:Spread` | `major` | `exact`: the same version everywhere; `minor`: the same major and minor version; `major`: the same major version |
| `Controller:Alerting:PackageDriftWait` | `01:00:00` | How long a package must stay out of alignment before its alert fires |

Pre-release labels do not count: `10.0.0-rc.1` and `10.0.0` are the same version for `minor` and `major`, but not for `exact`.

### Vulnerable packages

The controller checks every package version in use against advisories in the [OSV format](https://ossf.github.io/osv-schema/), for example an export of the GitHub Advisory Database for NuGet. It does not fetch advisories itself, so it can run in a closed network and its answer does not depend on an outside service: you provide the file or folder, and a scheduled job of your own keeps it current. A change is picked up without a restart; a file that cannot be read is logged and the advisories that were loaded stay in force.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Controller:Vulnerabilities:Advisories` | none | A JSON file (one advisory or a list) or a folder of such files, read including subfolders. Without it nothing is checked, and the page says so |
| `Controller:Vulnerabilities:PollInterval` | `00:01:00` | How often the file or folder is looked at for a change |
| `Controller:Alerting:VulnerablePackageWait` | `00:00:00` | How long a vulnerable version must be in use before its alert fires |

A finding is raised for each advisory, package and version in use. Versions match through the ranges of the advisory (`introduced`, `fixed`, `last_affected`) or its list of versions; only the NuGet ecosystem is used. Advisories with severity HIGH or CRITICAL raise the critical alert `vulnerable-package-critical`; the rest, including advisories that do not give a severity, raise the warning `vulnerable-package`. Silences work as for any other alert.

### Change markers

When a service runs a version it did not run at the previous look, the controller records a marker, served newest first at `GET /api/changes` as `at`, `scope`, `service`, `from` and `to`. Markers are kept in memory only, so a restarted replica starts empty and treats the versions it first sees as the baseline.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Controller:ChangeMarkers:SampleInterval` | `00:00:05` | How often the versions are compared |
| `Controller:ChangeMarkers:MaxMarkers` | `200` | How many markers are kept |

### Dead-man's switch

Each controller replica can ping an external watchdog, which alerts when all pings stop. That covers the one failure the system cannot report itself: all replicas being down.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Controller:DeadMansSwitch:Url` | none | The address that is requested with GET at startup and then every interval. Without it nothing is pinged |
| `Controller:DeadMansSwitch:Interval` | `00:01:00` | How often to ping |

A ping that fails, or is answered with an error, is logged as a warning and tried again at the next interval.

### Silences

A silence keeps alerts out of the dashboard until it ends, for example during planned maintenance. The conditions are still checked, so an alert that is still true when the silence ends appears at once. A silence has to end: `Until` is required, and an expired silence does nothing.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Controller:Alerting:Silences:<n>:Until` | required | When the silence ends, as a date and time such as `2026-11-02T06:00:00Z` |
| `Controller:Alerting:Silences:<n>:Rule` | `*` | The rule to silence, such as `service-down`, or `*` for all |
| `Controller:Alerting:Silences:<n>:Subject` | all | Silences subjects starting with this text, such as `shop/` for a whole scope or `shop/orders/` for one service |
| `Controller:Alerting:Silences:<n>:Reason` | none | Why, for whoever reads the file |

### Dependency graph

Applications report the calls they make: HTTP calls and, with the right activity sources, database and broker calls. The controller connects each caller to the thing it called:

| Address used by the caller | Node |
| --- | --- |
| The IP address of a pod of a known service | That service |
| `service.namespace`, `service.namespace.svc`, `service.namespace.svc.cluster.local` | That service in that scope |
| A plain `service` name | That service in the caller's own scope |
| Anything else | An external system |
| A database call | A database node, by server and database name |
| A message sent to a queue or topic | A queue node |

A name that only matches a service of another scope is not guessed at; it stays external. An edge nobody called for `EdgeFadeAfter` disappears. Calls of all instances of a service are added up.

### When an agent goes away

The controller judges from its own connections whether the agent of each scope is alive. An agent is alive while its connection is open and something, usually a heartbeat, was heard within the timeout. When it is not:

- The scope turns **Unknown** (grey) at once, instead of keeping its last colour, because its workloads may be fine or down and the controller cannot tell. The tile says "Agent not connected" and when the agent was last heard from; the scope view shows a banner and keeps listing the instances it last reported.
- After `ScopeUnknownWait` the critical alert `scope-unknown` fires for the scope, so a dead agent does not hide behind a quiet tile. It clears when the agent is back.
- Scopes stay listed until the controller restarts; a scope that is gone for good keeps its grey tile and its alert until then.

Each controller replica judges for itself, so replicas can briefly disagree.

### Agent tokens

The controller never needs a list of agents. It holds the hashes of the tokens it accepts, and agents register themselves under the scope name they announce.

| Entry | Meaning |
| --- | --- |
| `Scope: "*"` | An enrollment token: any agent that holds it registers under whatever scope name it announces. One such token is enough for a whole fleet. Anyone with the token can claim any scope name |
| `Scope: "shop"` | A token bound to one scope: its holders can only report as `shop`. Use it where a team should not be able to report as another team |

Several entries may share a scope, so a new token can be introduced before the old one is removed.
The hash is the SHA-256 of the token as lower-case hexadecimal text: `printf '%s' 'the-token' | sha256sum`.

```json
{
  "Controller": {
    "Listen": { "Port": 9440 },
    "Replicas": ["controller-0.example:9440", "controller-1.example:9440"],
    "AgentTokens": [
      { "Scope": "*", "TokenHash": "<sha-256 of the enrollment token>" }
    ]
  }
}
```

### Service account tokens

Agents in Kubernetes need no shared secret. With `Controller:ServiceAccounts:Enabled` the controller also accepts the token of the agent's own service account: Kubernetes projects a token made for the controller (a given *audience*) into the agent's pod and rotates it, the agent reads it from a file for every connection (`Agent:TokenFile`), and the controller asks the API server whether it is valid (a `TokenReview`). The agent can only report as the scope named after the namespace of its service account, so an agent in one namespace can never report for another. The configured tokens keep working beside it.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Controller:ServiceAccounts:Enabled` | `false` | Accept service account tokens |
| `Controller:ServiceAccounts:Audience` | `monitoring-controller` | The audience the tokens must be made for; the agent's projected token has to ask for the same one, so a token made for another service never opens this one |
| `Controller:ServiceAccounts:Scopes:<namespace>` | the namespace itself | The scope a namespace reports as, when it is not named after the namespace |
| `Controller:ServiceAccounts:ApiServer`, `TokenFile`, `CaFile` | the pod's own | How the controller reaches the API server, for a controller that does not run in the cluster of its agents |
| `Agent:TokenFile` | none | A file with the token, read again for every connection; instead of `Agent:Token` |

With the Helm charts: `serviceAccountAuth.enabled=true` on the controller chart (which creates a service account for the controller and binds it to `system:auth-delegator`, the right to review tokens and nothing else) and `auth.mode=serviceAccount` on the agent chart (which projects the token and needs no secret). Use the same audience on both. A token is only asked about when it has the three parts of a JWT, so the configured tokens cost no question to the API server, and when the API server cannot be reached the agent is turned away and tries again by itself. Use transport security as well: the token is a credential and travels in the hello.

### Transport security

| Mode | What it does | When to use |
| --- | --- | --- |
| `none` | Plain TCP. The agent token travels in clear text, and both executables log a warning at startup | A closed network the traffic cannot leave |
| `tls` | The controller presents a certificate and the agent checks it against `CaFile` and the host name (or `ServerName`) | Agents in other clusters or on Windows servers, for example through a TLS passthrough Route |
| `mtls` | `tls`, and the agent also presents a client certificate that the controller checks against `ClientCaFile` | Strict environments |

- The certificate files are read again when they change, so a renewed certificate is used for the next connection without a restart. Connections already open keep the old one.
- A mistake in a file or setting stops the executable from starting, with exit code 78 and a message naming the file.
- Revocation is not checked for a private authority (`CaFile`, `ClientCaFile`): such authorities rarely publish a list, and the check would fail every handshake offline. Use short-lived certificates. Without `CaFile` the agent uses the machine's authorities and the normal checks.
- Authentication stays on whatever the mode: the agent token is still required, and `mtls` adds to it.

```json
{
  "Controller": { "Transport": { "Mode": "tls", "Certificate": "/certs/tls.crt", "Key": "/certs/tls.key" } }
}
```

```json
{
  "Agent": { "Transport": { "Mode": "tls", "CaFile": "/certs/ca.crt" } }
}
```

### Dashboard

The controller serves the dashboard itself, so there is nothing else to install. Open `http://<controller>:8080/` in a browser.

| Path | Shows |
| --- | --- |
| `/` | A tile per scope with its status as an icon and a word, unhealthiest first, and the alerts that are firing. Select a tile for its services and pods |
| `/tv` | The same for a wall display: type scales with the screen, the cursor is hidden, and the page needs no input, see below |
| `/?filter=environment:prod` | Shows only scopes with that label value; repeat `filter` for several labels. It works on `/tv` too, so a wall display can stay on one environment. When the scopes have several values of the outermost label, a selector in the page switches between them |
| `/?theme=dark` or `?theme=light` | Forces a theme; otherwise the page follows the system setting, and the button in the corner switches it |
| `/api/overview` | The overview as JSON |
| `/api/scopes/<name>` | One scope with its services and instances as JSON, each instance with its version, health checks, containers and latest metrics |
| `/api/topology` | The dependency graph as JSON: nodes (services, databases, queues, external systems) and the edges between them with calls, errors and time |
| `/api/packages` | The packages in use anywhere in the landscape, each with its versions (newest first) and the services that use them, and how many services have not reported what they are built from |
| `/api/packages/<name>` | One package, with every version in use and by whom |
| `/api/sbom` | The packages of the whole landscape as a CycloneDX 1.5 bill of materials (JSON), each package and version once |
| `/api/sbom/<scope>/<service>` | The bill of materials of one service, which vulnerability scanners and licence checks can read |
| `/api/vulnerabilities` | The package versions in use that the loaded advisories say are vulnerable, most severe first, with the fix and the services affected, and how many advisories were checked against |
| `/api/events` | The overview as Server-Sent Events: the current one at once, then one per change, plus a keep-alive event while nothing changes |

The page has no login yet; reach it only over a network you trust.

### Wall display

Open `/tv` on a screen that nobody touches. It goes through the views on a timer, and when a scope turns Unhealthy it stops and shows that scope, with its failing services open, until the problem is gone or the focus time has passed. The warning about data that is old (no update for 15 seconds) stays, so a frozen screen never shows stale green.

| Address setting | Default | Meaning |
| --- | --- | --- |
| `rotate` | `60` | Seconds each view stays; `0` keeps the first view and only reacts to failures |
| `views` | `overview,dependencies` | The views to go through, in order: `overview`, `dependencies`, `packages` |
| `focus` | `300` | Seconds a failing scope is kept on screen. After that the rotation goes on and that scope is not shown again until it has recovered, so one lasting problem does not hold the screen |
| `drift` | off | `drift=1` moves the page a few pixels over twenty minutes and back, against burn-in; it is off for people who prefer reduced motion |
| `theme` | system | `dark` or `light` |
| `filter` | all | `filter=environment:prod` keeps the screen on one environment |

For example `/tv?filter=environment:prod&rotate=45&views=overview,dependencies&theme=dark&drift=1`. Only a scope that is Unhealthy takes over the screen; a Degraded one is visible in the rotation.

## Agent

| Setting | Default | Meaning |
| --- | --- | --- |
| `Agent:ScopeName` | required | The scope this agent reports as, for example the namespace or the server name. It registers itself under this name |
| `Agent:Token` | required | The token the controller holds a hash of. Prefer an environment variable or a mounted secret over the JSON file |
| `Agent:Controllers` | required | The bootstrap controller address as `host:port`, plus optional fallbacks. The controller answers with every replica's address, and the agent then connects to all of them |
| `Agent:Id` | machine name | Id of this agent |
| `Agent:Version` | none | Version of this agent, shown by the controller |
| `Agent:Labels:<name>` | none | Free-form labels such as `environment` and `datacenter`. They describe the scope, are sent with every snapshot (a change reaches the controller within the snapshot interval) and are what the dashboard groups and filters by |
| `Agent:Transport:Mode` | `none` | How the connection to the controller is protected: `none`, `tls` or `mtls`, see below. Must match the controller |
| `Agent:Transport:CaFile` | machine's authorities | PEM file with the certificate authority that signed the controller's certificate |
| `Agent:Transport:ServerName` | host of the address | The name the controller's certificate must be valid for, when it differs from the host the agent connects to, as behind a Route |
| `Agent:Transport:Certificate`, `Agent:Transport:Key` | none | The agent's client certificate and key (PEM). Required for `mtls` |
| `Agent:Transport:HandshakeTimeout` | `00:00:10` | How long a TLS handshake may take |
| `Agent:Reconnect:MinDelay` | `00:00:01` | Shortest wait before reconnecting to a replica |
| `Agent:Reconnect:MaxDelay` | `00:00:30` | Longest wait; repeated failures grow the wait up to this |
| `Agent:ConfigReplyTimeout` | `00:00:10` | How long to wait for the controller's answer to the hello |
| `Agent:SnapshotInterval` | `00:05:00` | How often a full snapshot is sent to repair drift |

### Discovery

The agent learns which instances exist from the platform it runs on. `Agent:Discovery:Kind` is `none` (the default), `kubernetes` or `windows`.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Agent:Discovery:Kind` | `none` | Where instances come from: `none`, `kubernetes` or `windows` |
| `Agent:Discovery:Kubernetes:Namespace` | namespace of the pod | The namespace whose pods become instances |
| `Agent:Discovery:Kubernetes:ApiServer` | in-cluster address | Address of the API server, for example `https://10.0.0.1:443` |
| `Agent:Discovery:Kubernetes:TokenFile` | in-cluster token file | File with the service account token, read again for every request because Kubernetes rotates it |
| `Agent:Discovery:Kubernetes:Token` | none | A fixed token instead of a token file; meant for development |
| `Agent:Discovery:Kubernetes:CaFile` | in-cluster certificate authority | PEM file with the certificate authority that signed the API server's certificate |
| `Agent:Discovery:Kubernetes:MonitoringSidecarContainer` | `monitoring-sidecar` | Name of this system's own sidecar container. When it fails, the instance is shown as Degraded with the cause, not Unhealthy |
| `Agent:Discovery:Kubernetes:ResyncInterval` | `00:01:00` | How long a watch stays open before the pods are listed again in full, which repairs a missed event |
| `Agent:Discovery:Kubernetes:RetryMinDelay` | `00:00:01` | Shortest wait before retrying after a failure |
| `Agent:Discovery:Kubernetes:RetryMaxDelay` | `00:00:30` | Longest wait; repeated failures grow the wait up to this |

Inside a cluster none of the Kubernetes settings are needed: the namespace, the API server address, the token and the certificate authority come from the pod's service account.
The API server is always contacted directly, never through a proxy set in the environment.

The agent needs a namespace-scoped role that can read pods and list events, deployments and stateful sets, and nothing else. Without the permission on events everything works, but a pod that does not start no longer shows the warning of the cluster as a check; without the one on deployments and stateful sets a pod no longer says its workload is rolling out:

```yaml
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: monitoring-agent
rules:
  - apiGroups: [""]
    resources: ["pods"]
    verbs: ["get", "list", "watch"]
  - apiGroups: [""]
    resources: ["events"]
    verbs: ["list"]
  - apiGroups: ["apps"]
    resources: ["deployments", "statefulsets"]
    verbs: ["list"]
```

Every pod of a Deployment or StatefulSet that is changing or short of pods carries a check `rollout`, shown as Degraded information and not as a state of the pod, such as `Rolling out: 1 of 3 replicas updated, 2 ready.` or `2 of 3 replicas are ready.`. It is read when the pods are listed again, so it can lag by up to the resync interval.

How a pod becomes an instance:

| Pod | Instance |
| --- | --- |
| Name | Instance id |
| Label `app.kubernetes.io/name`, else `app`, else the owning workload (a Deployment's ReplicaSet is shown as the Deployment), else the pod name | Service name |
| Pending | Starting |
| Running | Running |
| Being deleted, or Failed | Terminating |
| Succeeded (a finished job) | Not shown |
| All containers ready | Healthy |
| Only the monitoring sidecar is failing or not ready | Degraded, with a check named `monitoring sidecar` |
| Running but not ready | Degraded |
| A container stuck in `CrashLoopBackOff`, an image pull error, a configuration error, or exited with an error | Unhealthy |
| Pending without a problem, or phase Unknown | Unknown |

```json
{
  "Agent": {
    "ScopeName": "orders-platform",
    "Controllers": ["controller.example:9440"],
    "Labels": { "environment": "prod", "datacenter": "a" }
  }
}
```

with the token in the environment: `MONITORING_Agent__Token=...`.

### Windows services

On a Windows server the agent runs as one process per machine and the machine is the scope; its labels come from `Agent:Labels` in the local configuration file like any other agent. With `Agent:Discovery:Kind` set to `windows` it asks the Service Control Manager every few seconds and shows the services you list, even when the application behind them knows nothing of the monitoring client.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Agent:Discovery:Windows:Services` | none | The services to show, each an exact service name or a prefix ending in `*`, without regard to case. Only these are shown, because a server runs hundreds of services and few matter; with an empty list nothing is shown |
| `Agent:Discovery:Windows:PollInterval` | `00:00:05` | How often the Service Control Manager is asked |
| `Agent:Discovery:Windows:MeasureInterval` | `00:00:30` | How often the machine and the processes of the listed services are measured, which is slower because those figures change all the time |

| Service | Instance |
| --- | --- |
| Name | Instance id and service name; the display name, state and start type are labels (`windows.display-name`, `windows.state`, `windows.start-type`) |
| Running | Healthy |
| Paused | Degraded |
| Start or continue pending | Starting, health Unknown |
| Stop or pause pending | Terminating, health Unknown |
| Stopped | Unhealthy, with a check `windows service` that says why: the start type is Automatic so it should be running, or it is Disabled, or the start type is Manual |
| Removed from the machine | Gone |

The metric `windows.service.restarts` counts how often a service came back to Running after the agent saw it in another state; the first sighting counts for nothing, so the count starts at zero when the agent starts. It runs only on Windows, and asking for it elsewhere stops the agent from starting.

**The machine** is shown as an instance of its own, named after the machine, in the service `windows-host`, whether or not it is on the allowlist. It carries the metrics `host.uptime_seconds`, `host.cpu_percent` (from the second measurement on), `host.memory_total_bytes`, `host.memory_available_bytes`, `host.pending_reboot` (1 while Windows waits for a restart to finish an update or installation) and, per fixed disk, `host.disk_total_bytes` and `host.disk_free_bytes` tagged `drive`. A disk with less than 10% free makes the host Degraded and one with less than 3% Unhealthy, with a check that names the disk. The operating system is the label `windows.os`.

**The processes** of the listed services that are running carry `windows.process.memory_bytes` (working set), `windows.process.threads` and `windows.process.handles`. A service that shares a process, such as one of the many that run in `svchost`, shows the figures of that whole process.

Clock skew is not measured: the agent has no trusted time to compare with.

**Applications that use the monitoring client** report over a named pipe, as they do through a sidecar on Kubernetes, and the agent matches each with its service by process id. The service must be on the allowlist and running; an application in any other process is turned away. The library connects to the pipe `monitoring-agent` by default on Windows (set `MONITORING_AGENT_PIPE` or `MonitoringOptions.AgentPipe` for another name, or blank for none), introduces itself with its process id, program, name and version, sends heartbeats with its health and answers pulls for its health, metrics and calls exactly as a sidecar does. If there is no agent it retries quietly and the application is not affected.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Agent:Sidecars:Pipe:Name` | `monitoring-agent` with Windows discovery, else none | The name of the pipe the applications connect to |
| `Agent:Sidecars:Pipe:Allow` | none | The accounts the monitored applications run as, such as `NT SERVICE\billing` or `DOMAIN\svc-orders`. The pipe lets in only these, the system, the administrators, the agent's own account and the built-in Local Service and Network Service accounts, so another user on the machine cannot report as an application. An account that does not exist stops the agent from starting |
| `Agent:Sidecars:Listen:Enabled` | `false` with Windows discovery, else `true` | Whether to also listen on a TCP port for sidecars; a Windows server only needs the pipe

### Running as a Windows service

`deploy/windows/install-agent.ps1` installs the agent as a service that starts with the machine. Put `Monitoring.Agent.exe` and its `monitoring.json` in one folder, and run from an elevated PowerShell:

```powershell
.\install-agent.ps1 -Path C:\MonitoringAgent                  # install and start
.\install-agent.ps1 -Path C:\MonitoringAgent -Account 'DOMAIN\svc-monitoring'   # run as another account, asks for the password
.\install-agent.ps1 -Path C:\MonitoringAgent -Uninstall
```

- A service starts in the system folder, so the agent reads `monitoring.json` from the folder of the program when it runs as a service; `MONITORING_CONFIG_FILE` still overrides that.
- Recovery is set to restart after 5 seconds, again after 5, then after 30, and the count resets after a day without failures. A stop with a non-zero exit code counts as a failure, so a configuration error is retried and the server is not left unmonitored in silence.
- The agent logs to the Windows event log (Application, source `MonitoringAgent`) as well as to the console.
- The default account is Local System. For a smaller set of rights, use an account that may read the services and processes it is to report on.

### Sidecars

The sidecar next to each application connects to the agent, registers its application and sends heartbeats. The agent follows the health of the application through it. By default the agent listens for sidecars when `Agent:Discovery:Kind` is `kubernetes`, and not otherwise.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Agent:Sidecars:Enabled` | `true` for Kubernetes, else `false` | Whether the agent listens for sidecars |
| `Agent:Sidecars:Listen:Address` | `0.0.0.0` | IP address sidecars connect to |
| `Agent:Sidecars:Listen:Port` | `9441` | TCP port sidecars connect to. Plain TCP: keep it inside the namespace, for example behind a Service no one outside can reach |
| `Agent:Sidecars:HelloTimeout` | `00:00:10` | How long a new connection may stay silent before it must say hello |
| `Agent:Sidecars:HeartbeatInterval` | `00:00:05` | How often sidecars are told to send a heartbeat |
| `Agent:Sidecars:MissedHeartbeats` | `3` | How many heartbeats in a row may be missing before the sidecar counts as gone and its connection is closed |
| `Agent:Sidecars:HealthPullInterval` | `00:00:15` | How often the health checks of an application are read even when its health did not change |

How often metrics and dependency calls are read is not set here but in the controller's central configuration (`pullSeconds`, `edgeFlushSeconds`), so it can be changed for all agents at once.

An instance whose sidecar is gone is shown as Unhealthy until the sidecar returns. A sidecar that says goodbye on purpose, as during a rollout, does not cause that: the agent only forgets what the application reported. A pod that exists but has no sidecar keeps what the platform says about it.

The sidecar's own settings (`Sidecar:` in its environment, `MONITORING_Sidecar__...`) include `Pod:Ip`, `Pod:Node` and `Pod:Namespace`, which the chart template and the injector fill from the Downward API. They are sent in the registration as the labels `pod.ip`, `node` and `namespace`, unless a label of that name is set by hand with `Labels:<name>`. The pod name is the host name, which is already the default instance id.

### Containers

Each pod is shown down to its containers, taken from the same pod watch, so no extra permission is needed.

| Container | Role |
| --- | --- |
| The first container that is not the monitoring sidecar | Application |
| The container named by `MonitoringSidecarContainer` | Monitoring sidecar |
| Any other container, and init containers that keep running (native sidecars) | Other sidecar |
| Init containers | Init: shown while they run or after they failed, and left out once they finished without error |

| Container state | Health |
| --- | --- |
| Running and ready | Healthy |
| Running, not ready | Degraded |
| Waiting for a reason such as `CrashLoopBackOff` or an image pull error | Unhealthy |
| Waiting while it is created | Unknown |
| Exited with an error | Unhealthy |

Each container also carries its restart count, image, image digest, the reason and message of what it waits for, and the reason and exit code of its previous run, for example `OOMKilled`.

### Certificates an application serves or uses

An application can report how long its TLS certificates are valid, so a lapse is warned about before it causes an outage. It is off until you name the certificates:

```csharp
builder.Services.AddMonitoring(options =>
{
    // A file, such as the secret a Kubernetes certificate is mounted from; it is read again each time, so a renewal needs no restart.
    options.Certificates.Add(CertificateWatch.File("https", "/etc/tls/tls.crt"));

    // Or whatever the code has: the function is called at every reading and the certificate disposed afterwards.
    options.Certificates.Add(new CertificateWatch("signing", () => LoadSigningCertificate()));
});
```

Each certificate becomes the gauge `monitoring.certificate.expiry_days`, tagged `certificate=<name>`, with the days left (negative once it has expired). A certificate that cannot be read at one reading is left out of it, which shows as a gap. The controller alerts on it as described under `CertificateWarningDays` and `CertificateCriticalDays`; replicas that share a certificate count as one, and the one that expires first decides.

### Queues an application depends on

The application knows how to ask its broker, so it says what to ask:

```csharp
builder.Services.AddMonitoring(options =>
{
    options.Queues.Add(new QueueWatch("orders", () => cache.Read("orders")));   // QueueStats(Depth, Consumers, DeadLetters)
});
```

The function is called each time the metrics are read, every few seconds, so answer from a cache or a cheap call. Each queue becomes the gauges `monitoring.queue.depth`, `monitoring.queue.consumers` and `monitoring.queue.dead_letters`, tagged `queue=<name>`; what the broker does not tell (`null`), or a queue that cannot be read at that moment, is left out and shows as a gap. The controller alerts on dead letters and on waiting messages without consumers (`QueueWithoutConsumersWait`); replicas ask the same broker, so a consumer seen by one of them counts.

### Database connection pools

Nothing needs to be set up for Npgsql: it publishes the pool as `db.client.connections.usage` (`state=used` and `idle`) and `db.client.connections.max`, tagged with the pool, which is the connection string without the password. They are collected like any other metric, and the controller raises `connection-pool-full` when a pool has no free connection for `ConnectionPoolFullWait`.

### Databases

A database call becomes an edge to the server and database, with its calls, errors and duration, and a span with the statement (with the values taken out). No code is needed beyond naming the activity source of the provider, because the client only listens to the sources you list:

```csharp
builder.Services.AddMonitoring(options => options.TraceSources.Add("Npgsql"));
```

| Provider | Source | Checked |
| --- | --- | --- |
| Npgsql 9 (PostgreSQL) | `Npgsql` | Against a real server, JIT and Native AOT (`samples/Monitoring.DatabaseSample`): the edge, the failed query counted as an error, the statement with its values replaced, no trim or AOT warning |

Other providers work when they publish activities with the `db.system` tag and the address of the server, in the older (`net.peer.name`, `db.name`, `db.statement`) or the current (`server.address`, `db.namespace`, `db.query.text`) names; a failure is recognized from the status of the activity, the `error.type` tag, the `otel.status_code` tag or an `exception` event. They have not been tried here: SqlClient and the others are yours to check with the sample, which takes one connection string.

### Messages through ActiveMQ

A Native AOT application cannot be patched at run time, so the messages are counted by two calls around the application's own send and receive. They know nothing about the broker library, so the client stays free of one: you pass the code that sets and reads a property of your message. Register once:

```csharp
builder.Services.AddMonitoring(options => options.UseActiveMq());
```

```csharp
// Sending: the queue or topic becomes a node of its own, and the trace context travels in the message property "traceparent".
using var send = MonitoredMessaging.StartSend(MonitoredMessaging.ActiveMq, "invoices", (name, value) => message.Properties.SetString(name, value));
try { producer.Send(message); } catch (Exception ex) { send.Fail(ex); throw; }

// Receiving: the work on the message continues the trace of the sender.
using var process = MonitoredMessaging.StartProcess(MonitoredMessaging.ActiveMq, "invoices", name => message.Properties.GetString(name));
```

The graph then shows `sender → queue → receiver`. A message without a usable `traceparent` starts a trace of its own. Without monitoring both calls return `null` and do nothing, so the application code needs no check. The calls have been tested with plain dictionaries as the message, not with a real broker or its library.

### What the library says about itself

The client reports its own state as metrics, which the agent pulls with the rest and which show on the instance: `monitoring.client.spans_buffered` and `spans_dropped` (spans thrown away before anyone read them; a number that keeps rising says `SpanBufferSize` is too small for the traffic), `monitoring.client.edges` (dependencies counted; the limit is `MaxEdges`), `monitoring.client.metric_series` (the limit is `MaxMetricSeries`), `monitoring.client.sidecar_connections` (sidecars connected over the socket) and `monitoring.client.agent_connected` (1 while the agent of the machine has accepted the application, over the pipe on Windows).

### Raw TCP connections

HTTP calls and the calls of database and broker packages that publish activities are counted without any code. For a protocol that has nothing of the sort, open the connection through the client:

```csharp
using var client = await MonitoredTcp.ConnectAsync("legacy-host", 5000, cancellationToken);
```

It works like `TcpClient.ConnectAsync` and also counts the attempt, with how long it took and whether it failed, as a call of kind `TCP` to `host:port`. The dependency graph shows the peer as a service when the address belongs to one, and as an external node otherwise. TCP has no headers, so there is no trace context to pass on: you get the edge and its counts, and a span `TCP connect`, but no link to what the peer did.

### Packages of an application

An application that references the monitoring client lists the packages it is built from, so the dashboard can show what each service contains. A Native AOT binary cannot ask NuGet at run time, so the list is written during the build: the client package ships MSBuild targets (`build/Monitoring.Client.targets`) that read what NuGet restored (direct and transitive packages with their versions) and embed it in the application. Nothing needs to be configured.

- The list is only made for executables (applications and services), not for libraries.
- Turn it off for a project with `<MonitoringReportPackages>false</MonitoringReportPackages>`; the application then reports an empty list.
- The agent asks for the list once per sidecar connection, as soon as the application has reported its health, because it can only change with a new version of the application.


## What the agent and the sidecar report about themselves

| Setting | Default | Meaning |
| --- | --- | --- |
| `Agent:SelfMonitoring:Interval` | `00:00:15` | How often the agent reports about itself |
| `Agent:SelfMonitoring:PodName` | the host name | The pod the report belongs to, which is how it finds the instance of the agent's own pod |

Both report through the pipeline they already use, as metrics (no settings; the agent reports every 15 seconds, and only fixed names are used, so memory stays bounded).

- **Sidecar**: added to the metrics of its application, so they show on the instance: `monitoring.sidecar.memory_bytes`, `cpu_seconds`, `uptime_seconds`, `library_pull_ms` (the latest question to the application) and `library_connected` (1 when the application answered the latest question).
- **Agent**: reported as the application of its own pod, which becomes the service `monitoring-agent` (only when the pod is in a namespace the agent discovers): `monitoring.agent.memory_bytes`, `cpu_seconds`, `uptime_seconds`, `instances_watched`, `controller_connections`, `sidecar_connections`, `frames_sent`, `pull_ms` (the latest answer of a sidecar), `kubernetes_errors` and `kubernetes_relists`.

**Towards each controller replica** the agent reports, tagged with the replica as `replica=host:port`, whether it is connected (`replica_connected`, 1 or 0) and, while it is, how many changes of its scope the replica has not yet acknowledged (`replica_lag`). A replica that is never reached shows as 0 and one that is taken out of the list disappears. Because every replica receives this from every agent, each of them can show the state of all of them without talking to the others.

**The System page** of the dashboard (and the tile on the overview) shows this controller replica, then every replica the agents connect to with how many agents are connected and how far behind it is, then the pods that run without a monitoring sidecar. A replica that is behind or that an agent cannot reach raises the alerts `replica-behind` and `replica-unreachable` on any controller that sees it, including the others when one replica is down.

## Prometheus export

Each of the controller, the agent and the sidecar can serve its own values in the Prometheus text format, for an observability stack that should watch the monitoring system without going through it. It is off by default.

| Setting | Default | Meaning |
| --- | --- | --- |
| `Controller:Prometheus:Port`, `Agent:Prometheus:Port`, `Sidecar:Prometheus:Port` | `0` (off) | The port that serves `GET /metrics` |
| `Controller:Prometheus:Address`, `Agent:Prometheus:Address`, `Sidecar:Prometheus:Address` | `127.0.0.1` | The IP address to listen on. Local only until you change it; there is no authentication, so put it on a network you trust |

The values are the ones described under "What the agent and the sidecar report about themselves", and for the controller the System view (`monitoring_controller_uptime_seconds`, `scopes`, `agents_connected`, `event_clients`, `memory_bytes`, `healthy` and `alert_evaluation_ms`). Dots in names become underscores, and every value is exposed as a gauge. The controller serves it from its own listener, not the dashboard's port.

## Metric history

The agent keeps the last values of the metrics of each instance, so the dashboard can draw them as a sparkline when someone opens the instance. The controller keeps no history: it asks the agent of the scope over the connection that is already there, only while a page shows the instance (`GET /api/scopes/{scope}/instances/{id}/history`, which answers 204 when the agent keeps nothing or does not answer within a few seconds).

| Setting | Default | Meaning |
| --- | --- | --- |
| `Agent:MetricsHistory:Samples` | `60` | Values kept per series; one value arrives per metric pull, so at the default interval of 15 seconds this is 15 minutes |
| `Agent:MetricsHistory:MaxSeriesPerInstance` | `200` | Series kept per instance; further ones are not recorded, which bounds the memory of an application that makes up new series without end |

The history of an instance is dropped when its sidecar disconnects. The request is part of protocol version 2; a controller never sends it to an agent that announced version 1.


## The injector

The Helm template of the sidecar needs a change in the chart of every application. For applications whose chart you do not own, a small web server, the injector, adds the sidecar to a pod when it is created, the way a service mesh does. Install it once per cluster, as a cluster administrator, because the registration of the webhook is cluster-wide (`deploy/helm/monitoring-injector`):

```sh
helm install monitoring-injector deploy/helm/monitoring-injector -n monitoring \
  --set sidecar.agentAddress=monitoring-agent:9441
kubectl label namespace shop monitoring/inject=true     # every new pod of the namespace gets the sidecar
```

- **Who is changed.** A pod is given the sidecar when its namespace carries the label `monitoring/inject=true`, or the pod carries it itself, and not when the pod carries `monitoring/inject=false`. Only new pods are changed, so roll the deployments of a namespace after labelling it. The namespace of the injector itself and the ones under `excludedNamespaces` (by default `kube-system` and `kube-public`) are never changed.
- **What is added.** The sidecar as a native sidecar (Kubernetes 1.29 and later; `sidecar.nativeSidecar=false` makes it a second container), a small in-memory volume for the socket, and for each container of the pod the volume and the variable `MONITORING_SOCKET`. This is the same sidecar the library chart includes: both are checked against one golden file (`deploy/injector/sidecar.golden.json`), so a pod does not depend on how it got its sidecar. The agent of the namespace has to be there, at `sidecar.agentAddress`.
- **It never stops a pod.** The failure policy is `Ignore` by default, so when the injector is down pods start without the sidecar, and show up under "Pods without a monitoring sidecar" on the System page. Run two replicas (the default). With `failurePolicy=Fail` a pod that cannot be checked is refused, which keeps everything monitored and makes the injector a dependency of the whole cluster; be sure before you choose it.
- **It does not make the application monitored by itself.** The application still needs the monitoring client in its code; the sidecar only carries what the client reports.
- **Certificate.** The API server only talks to a webhook over TLS and has to trust its certificate: `tls.mode=generate` (the default) makes one when the chart is installed or upgraded and is meant for trying it out, `certManager` asks cert-manager (`tls.issuerRef`) and keeps it renewed, `openshift` lets the service CA of OpenShift make it, and `existingSecret` uses a secret you made together with the `tls.caBundle` that signed it. The injector looks at the files of the certificate again (`Injector:CertificateCheckInterval`, 30 seconds by default, when a connection is made), so a renewal needs no restart; a half-written or unreadable replacement is ignored and the working certificate stays. Kubernetes brings a changed secret to the pods after a minute or so.

| Setting (environment variable `MONITORING_Injector__...`) | Default | Meaning |
| --- | --- | --- |
| `SidecarImage` | required | The image of the sidecar |
| `ImagePullPolicy` | `IfNotPresent` | When the image of the sidecar is pulled |
| `AgentAddress` | `monitoring-agent:9441` | The agent of the namespace, as the sidecar reaches it |
| `NativeSidecar` | `true` | A native sidecar, or an ordinary second container |
| `Labels:<name>` | none | Labels the sidecar sends with the registration |
| `Certificate`, `Key` | required | The PEM files with the certificate and the key |
| `Web:Address`, `Web:Port` | `0.0.0.0`, `8443` | Where the webhook listens |
