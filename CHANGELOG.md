# Changelog

Versions follow semantic versioning. The protocol has its own version, and a change that old and new components cannot serve side by side is called out here. The text under a version is what its GitHub release shows.

## [1.0.1]

Two fixes to the Windows agent, found by running 1.0.0 end to end on a fresh Windows 11.

- The machine was merged into the agent's own instance, so the service `windows-host` never showed and the host figures appeared under `plover-agent`. The agent now shows as the service instance `PloverAgent` (always listed, whatever you put under `Discovery:Windows:Services`), and the machine as the instance named after it in `windows-host`.
- Available memory was reported as the total memory. It now comes from Windows (`GlobalMemoryStatusEx`) and follows what is really free.

## [1.0.0]

The first release, at 1.0.0.

- The controller: replicas that each receive everything from every agent, alerting with silences and acknowledgements, the service graph, metric history, change markers, a dead man's switch and an optional Prometheus export. The dashboard is embedded, so it is one executable.
- The agent: discovers pods, containers and Windows services, takes data from sidecars, and keeps the state of one scope.
- The sidecar and the client library: health, info, metrics and traces of a .NET application, over a local socket.
- The injector: adds the sidecar to the pods of labelled namespaces.
- Transport security: none, TLS or mutual TLS, with token, service account or certificate authentication.
- Native AOT container images on ghcr.io, Helm charts as OCI artifacts, a zip of the Windows agent, and the NuGet packages `Plover.Protocol` and `Plover.Client`.
