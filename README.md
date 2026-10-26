# Plover

Plover is a small, stateless, Native AOT monitoring system for .NET 10 workloads on Kubernetes and Windows. One live dashboard shows the health, metrics and service dependencies of every namespace or server, across environments and data centers, with alerting. It needs no service mesh, no Prometheus stack and no database.

Version 1.0.0 is out. It works end to end and is tested; see the [changelog](CHANGELOG.md) for what it contains.

Documentation: <https://basmulder03.github.io/Plover/>

## Try it

You need the .NET 10 SDK and Node.js 24 (the dashboard is built with it).

```sh
dotnet run --project samples/Plover.Demo
```

Then open <http://127.0.0.1:5080>. The demo runs a real controller with made-up agents that play a small incident again and again, so you can click through the dashboard without a cluster.

## Install

| What | Where |
| --- | --- |
| Client library | `dotnet add package Plover.Client` ([NuGet](https://www.nuget.org/packages/Plover.Client)) |
| Controller, agent, sidecar, injector | Images `ghcr.io/basmulder03/plover-controller`, `plover-agent`, `plover-sidecar` and `plover-injector` (amd64) |
| Helm charts | `oci://ghcr.io/basmulder03/charts/plover-controller` and `plover-agent`, `plover-injector`, `plover-sidecar` |
| Windows agent | `plover-agent-win-x64-<version>.zip` on the [releases page](https://github.com/basmulder03/Plover/releases) |

[deploy/README.md](deploy/README.md) has the commands, and the [start page](https://basmulder03.github.io/Plover/start) walks through them in order.

## How it fits together

```text
 application + client library ──(unix socket)── sidecar ──(TCP)── agent ──(TCP, TLS optional)── controller ── dashboard
```

An application adds the client library (`Plover.Client`) and one line of code, and answers with its name, version, health checks and metrics. A sidecar in the same pod registers it with the agent and sends heartbeats. One agent per namespace follows the pods through the Kubernetes API and sends what it finds upward. Two or more identical controller replicas receive everything from every agent, work out the health of the whole, evaluate alerts and serve the dashboard. On a Windows server one agent takes the place of the sidecar and the namespace agent.

Connections only go upward, and everything is rebuilt from the agents after a restart, so nothing is stored. The [design](docs/design.md) has the details.

## What works today

- Discovery of Kubernetes pods down to their containers, and of Windows services and processes, with the application's own health checks and metrics on top.
- A dependency graph built from real HTTP, TCP, database and ActiveMQ calls, without storing traces.
- A controller with registry, health roll-up, central configuration, alert rules with silences, notifications by webhook, chat and email, and a live dashboard with a wall display mode.
- Authentication for agents (tokens, Kubernetes service accounts, certificates), for the dashboard (a trusted proxy or tokens), and TLS or mutual TLS between the parts.
- Helm charts, an admission webhook that injects the sidecar, and an install script for Windows.
- Tests that run real controllers, agents, sidecars and applications, browser tests with accessibility checks, and size and speed budgets that fail the build.

## Build and test

```sh
dotnet build
dotnet test
```

The browser tests download a Chromium build the first time. [docs/testing.md](docs/testing.md) explains this and the other test layers.

## Run it for real

[deploy/README.md](deploy/README.md) has the images and Helm charts, [docs/configuration.md](docs/configuration.md) lists every setting, and there are guides for [Windows](docs/windows.md) and [OpenShift](docs/openshift.md).

## Contributing and security

Read [CONTRIBUTING.md](CONTRIBUTING.md) before you change code. Report security problems as described in [SECURITY.md](SECURITY.md).

## License

[Apache License 2.0](LICENSE).
