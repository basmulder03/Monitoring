# Start here

Plover shows, on one live page, whether the services in your Kubernetes namespaces and on your Windows servers are healthy, and which service calls which. It is written for .NET 10 and is small enough to run next to every application. It stores nothing: if you restart any part, it rebuilds its picture from the parts around it.

## See it first

You need the .NET 10 SDK and Node.js 24, because the build of the dashboard uses Node.

```sh
git clone https://github.com/basmulder03/Plover
cd Plover
dotnet run --project samples/Plover.Demo
```

Open <http://127.0.0.1:5080>. The demo starts a real controller and three agents that are made up. About every twenty seconds a pod in the `shop` scope gets slow, fails and is replaced by a new one, while a queue backs up in `billing`. You can click through the whole dashboard while it happens, and the alerts come and go as they would for real.

## What you are looking at

Each tile on the first page is a scope: a Kubernetes namespace or a Windows server. The colour and the word on it show the worst health found inside, so one failing pod is visible from the top. Click a tile to see its services, then the instances of a service, then the containers of an instance and the health checks the application reports about itself.

The Dependencies view draws which service calls which, and how often. It is built from counters in the applications, so no traces are stored. The Packages view lists what each service is built from and flags known vulnerabilities. The System view shows how the monitoring system itself is doing.

[The dashboard in motion](dashboard) has short recordings of all of this.

## How the parts fit

An application references the client library and adds one line, `AddPlover`, which makes it answer questions about its name, version, health and metrics. On Kubernetes a small sidecar in the same pod talks to the library, registers the pod and sends heartbeats. One agent per namespace follows the pods through the Kubernetes API and collects what the sidecars report. Two or more controllers receive everything from every agent, work out the health of the whole, evaluate the alert rules and serve the dashboard.

Every connection is opened from the lower part towards the upper one, so a part never needs to know the address of the parts below it. A controller that restarts is complete again after its agents send a fresh snapshot. On a Windows server one agent replaces the sidecar and the namespace agent.

The [design](design) goes through all of this in detail, and the [glossary](glossary) explains the terms.

## Putting it on your own workloads

1. Install the controller chart with the hash of an enrollment token. The token itself stays with you. The charts and images are on ghcr.io (`oci://ghcr.io/basmulder03/charts/plover-controller`), and the client library is on NuGet (`dotnet add package Plover.Client`).
2. Install the agent chart in each namespace you want to monitor, with the address of the controller and the token.
3. Give each application the sidecar, either by adding the sidecar library chart to its chart or by labelling its namespace so that the injector adds it.
4. Add the client library to the application and call `AddPlover`.

The exact commands and values are in [deploy/README.md](https://github.com/basmulder03/Plover/blob/main/deploy/README.md). For Windows servers, read [Windows servers](windows). For OpenShift, read [OpenShift](openshift). Every setting is listed in [Configuration](configuration).

## Where to go next

Read the [design](design) to understand why it works this way, [Configuration](configuration) to set it up, and the [reference](reference) to look something up. If you want to change the code, start with [Contributing](contributing) and [Testing](testing).
