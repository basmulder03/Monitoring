# Monitoring

A small, stateless, Native AOT monitoring system for .NET 10 workloads on Kubernetes and Windows. One live dashboard shows the health, metrics and (soon) service dependencies of every namespace or server, across environments and data centers, with alerting, and without a service mesh, a Prometheus stack or a database.

> **Status: early development.** The pieces below work end to end and are tested, but the project is not released and has no license yet. See the [roadmap](https://github.com/basmulder03/MonitoringAgent/issues/61) for what is built and what is next.

## How it fits together

```text
 application + client library ──(unix socket)── sidecar ──(TCP)── agent ──(TCP, TLS optional)── controller ── dashboard
```

| Part | What it does |
| --- | --- |
| Client library (`Monitoring.Client`) | One line in the application: answers with its name, version, health checks and metrics |
| Sidecar (`Monitoring.Sidecar`) | Next to each pod: registers the application with the agent, sends heartbeats, probes health |
| Agent (`Monitoring.Agent`) | One per namespace: follows the pods through the Kubernetes API, combines that with what applications report, streams snapshots and deltas upward |
| Controller (`Monitoring.Controller`) | Two or more identical replicas, each fed by every agent: health roll-up, alerts, and the web dashboard with live updates |

Connections only ever go upward, and everything is rebuilt from the agents after any restart, so nothing is stored. The full design is in [docs/design.md](docs/design.md).

## What works today

- Kubernetes discovery down to containers, with application health checks and metrics on top
- Controller with registry, health roll-up, alerts (unhealthy instance, service down, agent gone, restart loop, silences) and a live dashboard with grouping and filtering by labels
- Agent authentication by token, transport security with TLS or mutual TLS
- Component tests that run real controllers, agents, sidecars and applications, and browser tests with accessibility checks

## Build and test

You need the .NET 10 SDK.

```sh
dotnet build
dotnet test
```

Node.js 22 or later is needed to build the dashboard (TypeScript, see [src/Monitoring.Ui/ui](src/Monitoring.Ui/ui/README.md)), and the browser tests download a Chromium build the first time. [docs/testing.md](docs/testing.md) explains both.

## Run

Settings, defaults and examples for the controller, the agent and the sidecar are in [docs/configuration.md](docs/configuration.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Report security problems as described in [SECURITY.md](SECURITY.md).
