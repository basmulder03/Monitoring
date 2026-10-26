# Testing

`dotnet test` from the repository root runs everything. Most tests need nothing but the .NET SDK. Two groups use external tools,
which are installed for you the first time they are needed, and are skipped with an explanation when that is not possible.

| Tests | Where | Needs | Installed by |
| --- | --- | --- | --- |
| Unit and component tests | `tests/*` | .NET SDK | NuGet |
| Dashboard components and logic, in a DOM (Vitest and jsdom) | `src/Plover.Ui/ui/test` | Node and npm | The build of `Plover.Ui` runs `npm ci` |
| Specifications (Reqnroll) on the whole system, started by the Aspire app host | `tests/Plover.Specifications` | .NET SDK | NuGet |
| Dashboard in a real browser, with axe accessibility checks | `tests/Plover.Component.Tests/Browser` | A Chromium build, and its system libraries | The test fixture runs Playwright's installer when the browser is missing |

## The whole system, run and specified

`tests/Plover.AppHost` is an [Aspire](https://aspire.dev) app host that starts the whole system as real processes: a controller, an agent, a stand-in for the Kubernetes API (`tests/Plover.FakeCluster`, with a control API to add and delete pods), two applications (`orders` and `payments`, the sample application) and a call chain (`testapi` puts a message on the queue `orders` of an ActiveMQ Artemis container, `testworker` takes it and asks `testdata`, which asks a PostgreSQL container; `tests/Plover.TestApplications`), each with a sidecar. The two containers need docker and are pulled the first time. They talk over the ports a real deployment uses. The timers are shortened (a grace period of five seconds, not ten minutes), nothing else differs.

- To look at it: `dotnet run --project tests/Plover.AppHost`, then open the Aspire dashboard it prints. It lists the processes with their logs and the address of the dashboard of the controller (`controller`, endpoint `web`). Every resource has buttons in that dashboard (`DemoCommands`): each application can be made unhealthy and healthy again, and its pod made to crash loop, not ready, removed or restored; the broker and the database can be frozen (unreachable, `docker pause`) and thawed. Stop, start and restart on the controller, the agent and the sidecars are Aspire's own. The same switches by hand: `touch /tmp/plover-e2e-dev/payments.broken` (remove the file to repair), `curl -X DELETE <cluster>/control/pods/payments-1` and `curl -X PUT '<cluster>/control/pods/payments-1?service=payments'`.
- On a real Kubernetes: `dotnet run --project tests/Plover.AppHost -- --Kind=true` builds the images from the checkout (several minutes the first time; `--SkipBuild=1` reuses the ones there), creates a kind cluster `plover-demo` with a two-replica controller, an agent, the injector and three applications (`orders`, `payments`, `catalog`) that get their sidecar from the injector, and keeps the dashboard on http://127.0.0.1:18080 (resource `cluster` in the Aspire dashboard). Needs docker, kind, kubectl and helm, and bash: on Windows that is Git Bash (Git for Windows), not the bash of WSL, which has a kubeconfig of its own; the host says so when it is not there. This has not been run on a Windows machine yet. Each application, the agent and the controller are resources that follow their logs and have buttons: make unhealthy, kill the pod, scale to zero, a bad rollout and its undo; kill the agent, cut it off from the controller with a network policy; kill a controller replica or scale the controller to zero. The cluster stays after the host stops; `deploy/demo/kind-down.sh` deletes it.
- With a Windows server: add `--Windows=true` to the kind command (`-- --Kind=true --Windows=true`). It is off unless you ask. A Windows Server 2022 evaluation runs in a virtual machine inside a container ([dockur/windows](https://github.com/dockur/windows)), with the agent installed as a real Windows service (`deploy/windows/install-agent.ps1`) reporting the services `Spooler` and `W32Time` as the scope `windows-server-1`. Needs Linux with `/dev/kvm` (it cannot run on a Windows host), 4 GB of memory and 40 GB of disk. The first start downloads Windows (about 6 GB) and installs it, which takes around half an hour; watch it in the resource `windows` (link "Windows screen", http://127.0.0.1:8006). The disk is kept in `~/.plover-demo/windows` (`PLOVER_DEMO_WINDOWS_STORAGE` moves it), so later starts take a minute or two; delete the folder to start over. Buttons stop and start the two services and the agent over ssh, kill the agent process (Windows restarts it) and freeze the whole machine. The VM sits on the docker network of kind and reaches the controller through a node port per replica; nothing is published on the network of your machine, the viewer and ssh only on 127.0.0.1.
- To specify it: `tests/Plover.Specifications` holds the functional requirements as Gherkin features (Reqnroll, xUnit v3), one file per area under `Features/`. The steps start the same app host, once for the whole run, and use the API of the controller (what the dashboard shows) and the control surfaces above; they stop and start the agent and the controller through Aspire. The scenarios share one environment and change it, so they run one after the other and leave it as they found it. Read the `.feature` files to learn what the system promises; they are the single source of truth for behaviour, and a requirement is only a requirement when a scenario states it.
- Areas so far (26 scenarios, about three minutes): registration, containers and packages; breaking and repairing an application; alerting (one alert for a lasting problem, which ends with the problem); the dependency graph from real traffic and through a queue and a database; pods that are deleted, come back or are not ready, an application that is killed for lack of memory again and again (shown with the reason) and a sidecar that crash loops (the instance is degraded, the application is not shown as down); an agent that stops (shown as no agent, then the critical `scope-unknown`) and returns; a restarted controller, with no alarm while agents report in again; the System view, the metrics export and the metric history; and central configuration (a change reaches the agent, an invalid file changes nothing).
- Not yet: a fault-injection proxy, silences and the startup grace period, the multiple environments, and the dashboard in a browser (TV mode); the browser tests of `tests/Plover.Component.Tests` cover the last one at the moment. The call from `testworker` to `testdata` shows as a call to an address, not to the service, because every pod of the fake cluster has the address 127.0.0.1.
- How the specifications are built: steps are thin and only say what happens in the words of the feature; the work is done by drivers that steps receive through Reqnroll's dependency injection: `DashboardDriver` (what a person sees, read from the API behind the dashboard), `ApplicationDriver` (call, break, repair), `ClusterDriver` (add, delete, un-ready and failing pods), `ProcessDriver` (stop and start processes through Aspire). Drivers remember what a scenario changed, and an `[AfterScenario]` hook (`Hooks`) puts it all back, so a scenario that fails halfway cannot poison the next one. Every wait has a deadline and, when it runs out, the message shows what the dashboard said at that moment. The generated `*.feature.cs` files are not committed.
- Add a scenario: write it in a feature file, reuse a step if there is one (`Steps/`), else add a step; waits always have a deadline (`Eventually`), because nothing in a monitoring system is instant.

## Dashboard (TypeScript)

The dashboard in `src/Plover.Ui/ui` is built with Node, which the build of `Plover.Ui` needs (Node 24 or later); it runs `npm ci` and `npm run build` itself and stops with a message when npm is missing.
`dotnet test` runs the Vitest tests as one test (`DashboardTests`); to work on them directly, run `npm test` in that folder. The tests mount the real app against fakes for the browser (clock, timers, the live stream, storage),
and use the generated contract types, so a fixture that no longer matches what the controller sends does not compile.

`tests/Plover.Ui.Tests` also checks that `contracts.ts` matches the C# types (regenerate with `UPDATE_CONTRACTS=1`), that the design tokens have enough contrast in both themes, and that no source file can put data into the page as markup.

## Browser tests (Playwright and axe)

The tests start a real controller, real agents and a fake Kubernetes API, and drive Chromium against the dashboard. They check what a person sees
(tiles, grouping, live updates, the warning for old data), keyboard use, themes, the wall display, that agent data cannot run as script,
and that the page has no WCAG 2.0 A/AA violations in both themes (axe-core).

- Browser download: done by the test fixture with Playwright's own installer, once per machine (about 230 MB). Set `PLOVER_SKIP_BROWSER_INSTALL=true` to skip the tests instead of downloading.
- System libraries: Chromium needs a few Linux libraries. If they are missing the tests are skipped and say so. Install them with
  `pwsh bin/Debug/net10.0/playwright.ps1 install-deps chromium`, or `node .playwright/package/cli.js install-deps chromium` from the test output folder; both need root.

## Behind a proxy that inspects TLS

npm and the Playwright download both run on Node. When the proxy replaces certificates, make Node trust the operating system's store instead of turning verification off:

```sh
export NODE_OPTIONS=--use-system-ca
```

Proxy settings for downloads come from `HTTPS_PROXY`, and for npm from the usual `.npmrc`. The lockfile names the public npm registry; npm swaps in the registry you have configured.

## A real database

`tests/Plover.Client.Tests/Features/Database` talks to a real PostgreSQL server, to see what a database provider publishes and that it becomes an edge. It skips itself, and says why, when `PLOVER_TEST_POSTGRES` is not set; CI starts a server and sets it. To run it here:

```sh
docker run -d --name pg -e POSTGRES_PASSWORD=test -p 127.0.0.1:54329:5432 postgres:18-alpine
PLOVER_TEST_POSTGRES='Host=127.0.0.1;Port=54329;Username=postgres;Password=test' dotnet test --project tests/Plover.Client.Tests
```

`samples/Plover.DatabaseSample` is the same check as a Native AOT program: `docker build --build-arg PROJECT=DatabaseSample --build-arg PROJECT_PATH=samples/Plover.DatabaseSample -f deploy/Dockerfile .`, then run the image with `PLOVER_SAMPLE_POSTGRES` set.

## Windows

Code that talks to Windows (the Service Control Manager and the like) has tests that skip themselves with an explanation anywhere else. CI runs them on a Windows runner for every push and pull request (the `windows` job of `ci.yml`); the nightly adds the Native AOT publish and the install as a service. To run them on a Windows machine, publish the test project for it, which works from Linux too, copy the folder over and start the executable:

```sh
dotnet publish tests/Plover.Agent.Tests -c Release -r win-x64 --self-contained -p:PublishAot=false -o out
# on Windows:  Plover.Agent.Tests.exe -class "Plover.Agent.Tests.Features.Discovery.Windows.ServiceControlManagerTests"
```

The numbers Windows uses are not the ones a Linux test can guess: the states of a service start at 1, for example, and an enum that counted from 0 passed every Linux test while it was wrong on the real thing. A mapping of a Windows value therefore needs a test that runs there.

## Budgets

The executables that run next to applications have to stay small. Two checks keep them that way.

### Start time and memory of the binaries

`scripts/budgets.sh <folder>` runs in the `aot` job of CI after the smoke test. It starts the Native AOT controller, agent and sidecar, waits for them to be ready and, after three idle seconds, reads their resident memory. The limits are about two and a half times what was measured on Linux x64, so a shared runner's noise does not fail the build but a change that doubles the cost does:

| | Measured | Limit |
| --- | --- | --- |
| Controller: start, idle memory | 35 to 85 ms, 26 to 31 MB | 1,500 ms, 80 MB |
| Agent: start, idle memory | about 30 ms, 15 MB | 1,500 ms, 48 MB |
| Sidecar: start, idle memory | about 20 ms, 12 to 13 MB | 1,500 ms, 40 MB |

The same numbers sit well inside the limits of the Helm charts (controller 512 Mi, agent 128 Mi, sidecar 64 Mi). A sample web application with the client library in it holds about 26 MB. The library's own share is what its buffers keep when they are full (`tests/Plover.Client.Tests/Budgets`): the 500 dependencies and 2,048 spans that the default options allow take about 0.7 MB, and the test fails above 3 MB. The start time of the library is part of the start of the application and is not measured separately.

### Scale

The tests are in `tests/Plover.Controller.Tests/Budgets`. Five thousand instances with fifty series each, the size `docs/design.md` plans for, take about 90 MB in a controller, roughly 360 bytes per series, and the test fails above 150 MB. A snapshot of one hundred such instances is 457 KB as a frame, so a thousand take about 4.5 MB. A frame may be up to 16 MB (`FrameCodec.DefaultMaxFrameBytes`), which holds a scope of several thousand instances, and the test requires a thousand to use less than half of that. The first frame of a connection, which arrives before the peer has proved who it is, may only be 64 KB (`FrameCodec.HandshakeMaxFrameBytes`), so a stranger cannot make the controller or an agent allocate more than that. The size is checked before any memory is allocated for the frame.

### Speed

The tests are in `tests/Plover.Controller.Tests/Budgets` and `tests/Plover.Client.Tests/Budgets`. At the same size (5,000 instances, 50 series each) a round of deltas from fifty agents is applied in about 2 ms (limit 250 ms), and twenty rules that each hold for 5,000 subjects (100,000 alerts) are evaluated in about 90 ms the first time and 230 ms with the listing afterwards (limits 5 s and 10 s). Counting a call to a dependency in the client library takes about 0.3 microseconds and allocates nothing (limits: 5 microseconds, under a byte per call). The limits are many times what was measured, so a shared runner's noise does not fail the build; each test prints what it measured (`--output Detailed`). Making `EdgeCounters.Record` allocate fails the test. `FanOutBudgetTests` opens 200 dashboard pages over real connections against a controller holding 1,000 instances: every page has its first overview after about 260 ms and a change on every page within one push interval (250 ms in the test); the limit is 10 s for both. `SidecarFanInBudgetTests` (`tests/Plover.Agent.Tests/Budgets`) connects 300 sidecars to one agent over real TCP: all are registered after about 160 ms and a change of health in every application is in the agent's view after about 80 ms; the limit is 10 s for both. `RecoveryBudgetTests` restarts a controller that has 100 agents connected (5,000 instances, 50 series each) and lets all agents come back at once with their whole scope: the new controller has everything again after about 0.9 s; the limit is 10 s. These three tests run alone (xunit collection "Scale budgets"), because they measure time or the heap of the whole process. `FanOutBudgetTests` and `RecoveryBudgetTests` are left out of the CI test step because they failed there on a slow shared runner (42 s instead of 0.3 s, and 13.6 s instead of 0.9 s); the nightly still runs them. 

### Nightly trend

The workflow is `.github/workflows/perf-nightly.yml` and the comparison is `scripts/perf-trend.py`. Every night the budget tests (the `Budgets` namespace of the controller, agent and client test projects) run in Release with `PLOVER_PERF_RESULTS` set, so each of them leaves its numbers through `PerfResults.Record` (13 measurements, a smaller number is always better). The script compares them with the median of the last seven runs and fails the night when a value is more than twice that median and also more than the noise floor of its unit above it (25 ms, 1 microsecond, 5 MB, 100 KB, 1 byte); a measurement that earlier runs had and this one lacks fails as well. The run is appended to `results.json` and the page `index.html` is rewritten (a table with a sparkline per measurement), both committed to the `perf-results` branch by the workflow, also when a night failed, so the page shows the regression. To run it by hand: `PLOVER_PERF_RESULTS=/tmp/run.tsv dotnet test --project tests/Plover.Controller.Tests --filter-namespace "*.Budgets"` for each project, then `python3 scripts/perf-trend.py /tmp/run.tsv results.json index.html $(git rev-parse HEAD)`. A budget test that fails outright stops the run before anything is stored. The docs site is to read the branch (#67).

## The wire format

The bytes that components send each other are a contract between parts that are rolled out one by one. A frame of every message sits next to the tests (`tests/Plover.Protocol.Tests/Golden/*.json`), what is sent has to stay the same as it, and the same frames have to be read back as the same messages. A reader has to cope with a field it does not know (a peer one version ahead) and with a field that is missing (a peer one version behind), and has to refuse a message of a kind it does not know as a bad frame without crashing. A change of the wire format is allowed but has to be on purpose: run `UPDATE_GOLDEN=1 dotnet test --project tests/Plover.Protocol.Tests`, read the diff and think about what a peer one version away does with it. The version of the protocol and what a controller does for an agent of the version before it (it does not ask it for history) are tested next to the code that does it.

## Soak

The budgets measure one moment. `scripts/soak.sh [minutes]` runs the demo host (a real controller with made-up agents that play an incident again and again) for a longer time, with twenty dashboards that are connected all the time and replaced every five minutes and the other pages asked for now and then, and samples memory, threads and open files every 30 seconds. It fails when memory is a quarter higher over the last fifth of the run than between 20% and 40%, and more than 25 MB higher, which is what a leak looks like. A run of three minutes stays at about 105 MB. The `Soak` workflow runs it for two hours every Sunday, or for as long as you ask for by hand (at most 330 minutes), and keeps the samples as an artifact.

## Helm charts

Every chart has unit tests next to it (`tests/*_test.yaml`, [helm-unittest](https://github.com/helm-unittest/helm-unittest)): required values must fail with a clear message, each mode of authentication, transport and TLS must render the right environment, volumes and rights, optional features must be off by default, and the default render of each chart is a reviewed snapshot (`tests/__snapshot__`; after an intended change run `helm unittest -u <chart>` and read the diff). The sidecar library chart is tested through `deploy/helm/test/sample-app`. `deploy/helm/test.sh` lints the charts, runs all of it and checks that the injector adds the same sidecar as the template; install the plugin once with `helm plugin install https://github.com/helm-unittest/helm-unittest --verify=false`. The injector's snapshot uses the cert-manager mode, because the generated certificate of the default mode differs on every render.

## Kubernetes nightly

`deploy/k8s-test/run.sh` builds the images, installs the three charts and a sample application on a throwaway kind cluster and checks the dashboard API of the controller. It needs docker, kind, kubectl and helm; `KEEP=1` leaves the cluster. The `Kubernetes nightly` workflow runs it every night.

`deploy/k8s-test/multicluster.sh` uses two kind clusters that share the docker network. The controller (two replicas, tls, a token) runs in cluster A, and agents with a sample application run in A and in B, the ones in B reaching the controller across clusters through a node port per replica. Then it checks failover in each cluster: it deletes the pod of the agent that holds the lease, and rolls the agents out, and measures for how many seconds the application was not shown as healthy. It fails when that is more than a minute, and it also deletes a controller replica to see that the other cluster's scope stays. On a laptop a crashed holder cost 5 to 8 seconds and a rollout 1 to 3. Afterwards it puts a controller in each cluster, with all four replicas known to every agent, and checks that the second one shows both scopes, that a service that goes down is sent to a receiver on your machine once and not once per site, and that the second site takes over sending when the first is gone. It runs in the same nightly workflow. `SKIP_BUILD=1` reuses images you built, and `KEEP=1` leaves both clusters.
