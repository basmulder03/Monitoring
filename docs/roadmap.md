# Roadmap

The work is tracked as GitHub issues, with [issue 61](https://github.com/basmulder03/Monitoring/issues/61) as the list that ties them together. This page says where things stand; the issues have the detail.

## Built

Everything in the [design](design) is built and tested. That covers the protocol with its transports (none, TLS and mutual TLS), the controller, the agent, the sidecar, the client library, the dashboard, the Helm charts and the injector.

The controller has a registry, a health roll-up, central configuration with layers, a dependency graph, alerting with silences, change markers, notifications by webhook, chat and email, authentication for agents and for the web UI, an optional SQLite state store and self-monitoring. The agent discovers Kubernetes pods and Windows services. The client library reports health, metrics, spans and dependency edges, and instruments HTTP, databases, ActiveMQ and TCP. The dashboard has the overview, scope, dependencies, packages and system views and a TV mode.

The tests are unit, component, browser, specifications and architecture tests, a Native AOT smoke test, nightly runs on Kubernetes and Windows, and speed and size budgets with a nightly trend.

## Open

| What | State |
| --- | --- |
| Documentation site ([67](https://github.com/basmulder03/Monitoring/issues/67)) | Live; grows with the code, the reference pages are generated |
| Packaging and publishing ([56](https://github.com/basmulder03/Monitoring/issues/56)) | Not started on purpose: nothing is published without the owner's go-ahead (see [Releases](releases)) |
| Name ([57](https://github.com/basmulder03/Monitoring/issues/57)) | Decided: the code and the repository are called Monitoring. A last trademark and package-name check is left for the day of the first publish |

## Not planned

Long-term storage of metrics or traces, log aggregation and automatic remediation are non-goals (see [Purpose and scope](design#purpose-and-scope)). Scope providers for Docker hosts and systemd machines, other alert sinks, and other state stores are possible through the interfaces but have no date.
