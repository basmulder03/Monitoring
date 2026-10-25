# Glossary

| Term | Meaning |
| --- | --- |
| Scope | The group of services one agent reports on: a Kubernetes namespace or a Windows server. It is the unit of a tile on the dashboard. |
| Service | A logical application, for example `orders`. It can have several instances. |
| Instance | One running copy of a service: a pod on Kubernetes, a process on Windows. |
| Container | A container inside a pod, shown under its instance so you can tell whether the application, the Plover sidecar or another sidecar is the problem. |
| Client library | The NuGet package an application references. It exposes the application's name, version, health checks and metrics, and counts calls to its dependencies. |
| Sidecar | A small container next to the application in each pod. It registers the pod with the agent, sends heartbeats and answers the agent's requests with what the library says. |
| Agent | One per namespace or Windows server. It decides which instances exist, collects what the sidecars report and sends it to every controller. |
| Controller | The part that holds the whole picture, evaluates alerts and serves the dashboard. You run two or more identical replicas. |
| Replica | One copy of the controller. Each replica is fed by every agent, so any one of them can be restarted or lost. |
| Snapshot | The complete state of a scope, sent by an agent when it connects and every five minutes. |
| Delta | What changed since the previous message of the same agent. |
| Heartbeat | A short message sent every five seconds. Three missed heartbeats make an instance unhealthy. |
| Pull | A request from a parent to its child for health checks, metrics or spans. Pulls carry a cursor, so a reconnect does not repeat data. |
| Health | Healthy, Degraded (a non-critical check fails), Unhealthy (a critical check fails or heartbeats are missing) or Unknown (the agent of the scope is disconnected). Health rolls up as the worst of what is inside. |
| Lifecycle | Where an instance is in its life: Starting, Running, Terminating or Gone. It is separate from health. |
| Gone | An instance that was deleted. It stays visible for ten minutes so you can see what disappeared. |
| Edge | A counted connection from one service to another service, a database or a queue. The edges make up the dependency graph. |
| Label | A free key and value on a scope, service or instance, such as `environment=prod`. The dashboard groups and filters by labels. |
| Enrollment token | A token with scope `*`. An agent that holds it can register itself under any scope name. Other tokens are bound to one scope. |
| Startup grace | The time after a controller starts during which no alert rule is evaluated, so agents can send their snapshots first. |
| Silence | A rule in the central configuration that mutes matching alerts until an expiry time. |
| TV mode | The wall display view at `/tv`: it rotates through views, hides the cursor and opens a failing scope by itself. |
