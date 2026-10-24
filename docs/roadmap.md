# Roadmap

The work is tracked as GitHub issues, with [issue 61](https://github.com/basmulder03/Monitoring/issues/61) as the list that ties them together. This page says where things stand; the issues have the detail.

## Built

Everything in the [design](design) is built and tested: the protocol and its transports (none, TLS, mutual TLS), the controller (registry, health roll-up, central configuration with layers, dependency graph, alerting with silences, change markers, notifications with webhook, chat and email, authentication of agents and of the web UI, the optional SQLite state store, self-monitoring), the agent (Kubernetes and Windows discovery, sidecar connections, metric history), the sidecar, the client library (health, metrics, spans, dependency edges, instrumentation of HTTP, databases, ActiveMQ and TCP), the dashboard (overview, scope, dependencies, packages, system, TV mode), the Helm charts and the injector, and the tests that go with them: unit, component, browser, specifications, architecture, Native AOT smoke, Kubernetes and Windows nightly runs, and speed and size budgets with a nightly trend.

## Open

| What | State |
| --- | --- |
| Documentation site ([67](https://github.com/basmulder03/Monitoring/issues/67)) | Live; grows with the code, the reference pages are generated |
| Packaging and publishing ([56](https://github.com/basmulder03/Monitoring/issues/56)) | Not started on purpose: nothing is published without the owner's go-ahead (see [Releases](releases)) |
| Name ([57](https://github.com/basmulder03/Monitoring/issues/57)) | Decided: the code and the repository are called Monitoring. A last trademark and package-name check is left for the day of the first publish |

## Not planned

Long-term storage of metrics or traces, log aggregation and automatic remediation are non-goals (see [Purpose and scope](design#purpose-and-scope)). Scope providers for Docker hosts and systemd machines, other alert sinks, and other state stores are possible through the interfaces but have no date.
