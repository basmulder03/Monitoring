# Testing

`dotnet test` from the repository root runs everything. Most tests need nothing but the .NET SDK. Two groups use external tools,
which are installed for you the first time they are needed, and are skipped with an explanation when that is not possible.

| Tests | Where | Needs | Installed by |
| --- | --- | --- | --- |
| Unit and component tests | `tests/*` | .NET SDK | NuGet |
| Dashboard components and logic, in a DOM (Vitest and jsdom) | `src/Monitoring.Ui/ui/test` | Node and npm | The build of `Monitoring.Ui` runs `npm ci` |
| Specifications (Reqnroll) on the whole system, started by the Aspire app host | `tests/Monitoring.Specifications` | .NET SDK | NuGet |
| Dashboard in a real browser, with axe accessibility checks | `tests/Monitoring.Component.Tests/Browser` | A Chromium build, and its system libraries | The test fixture runs Playwright's installer when the browser is missing |

## The whole system, run and specified

`tests/Monitoring.AppHost` is an [Aspire](https://aspire.dev) app host that starts the whole system as real processes: a controller, an agent, a stand-in for the Kubernetes API (`tests/Monitoring.FakeCluster`, with a control API to add and delete pods), two applications (`orders` and `payments`, the sample application) and a call chain (`testapi` puts a message on the queue `orders` of an ActiveMQ Artemis container, `testworker` takes it and asks `testdata`, which asks a PostgreSQL container; `tests/Monitoring.TestApplications`), each with a sidecar. The two containers need docker and are pulled the first time. They talk over the ports a real deployment uses. The timers are shortened (a grace period of five seconds, not ten minutes), nothing else differs.

- **To look at it:** `dotnet run --project tests/Monitoring.AppHost`, then open the Aspire dashboard it prints. It lists the processes with their logs and the address of the dashboard of the controller (`controller`, endpoint `web`). Break an application with `touch /tmp/monitoring-e2e-dev/payments.broken` and remove the file to repair it; delete or add a pod with `curl -X DELETE <cluster>/control/pods/payments-1` and `curl -X PUT '<cluster>/control/pods/payments-1?service=payments'`.
- **To specify it:** `tests/Monitoring.Specifications` holds the functional requirements as Gherkin features (Reqnroll, xUnit v3), one file per area under `Features/`. The steps start the same app host, once for the whole run, and use the API of the controller (what the dashboard shows) and the control surfaces above; they stop and start the agent and the controller through Aspire. The scenarios share one environment and change it, so they run one after the other and leave it as they found it. Read the `.feature` files to learn what the system promises; they are the single source of truth for behaviour, and a requirement is only a requirement when a scenario states it.
- **Areas so far (26 scenarios, about three minutes):** registration, containers and packages; breaking and repairing an application; alerting (one alert for a lasting problem, which ends with the problem); the dependency graph from real traffic and through a queue and a database; pods that are deleted, come back or are not ready, an application that is killed for lack of memory again and again (shown with the reason) and a sidecar that crash loops (the instance is degraded, the application is not shown as down); an agent that stops (shown as no agent, then the critical `scope-unknown`) and returns; a restarted controller, with no alarm while agents report in again; the System view, the metrics export and the metric history; and central configuration (a change reaches the agent, an invalid file changes nothing).
- **Not yet:** a fault-injection proxy, silences and the startup grace period, the multiple environments, and the dashboard in a browser (TV mode); the browser tests of `tests/Monitoring.Component.Tests` cover the last one at the moment. The call from `testworker` to `testdata` shows as a call to an address, not to the service, because every pod of the fake cluster has the address 127.0.0.1.
- **How the specifications are built:** steps are thin and only say what happens in the words of the feature; the work is done by drivers that steps receive through Reqnroll's dependency injection: `DashboardDriver` (what a person sees, read from the API behind the dashboard), `ApplicationDriver` (call, break, repair), `ClusterDriver` (add, delete, un-ready and failing pods), `ProcessDriver` (stop and start processes through Aspire). Drivers remember what a scenario changed, and an `[AfterScenario]` hook (`Hooks`) puts it all back, so a scenario that fails halfway cannot poison the next one. Every wait has a deadline and, when it runs out, the message shows what the dashboard said at that moment. The generated `*.feature.cs` files are not committed.
- **Add a scenario:** write it in a feature file, reuse a step if there is one (`Steps/`), else add a step; waits always have a deadline (`Eventually`), because nothing in a monitoring system is instant.

## Dashboard (TypeScript)

The dashboard in `src/Monitoring.Ui/ui` is built with Node, which the build of `Monitoring.Ui` needs (Node 22 or later); it runs `npm ci` and `npm run build` itself and stops with a message when npm is missing.
`dotnet test` runs the Vitest tests as one test (`DashboardTests`); to work on them directly, run `npm test` in that folder. The tests mount the real app against fakes for the browser (clock, timers, the live stream, storage),
and use the generated contract types, so a fixture that no longer matches what the controller sends does not compile.

`tests/Monitoring.Ui.Tests` also checks that `contracts.ts` matches the C# types (regenerate with `UPDATE_CONTRACTS=1`), that the design tokens have enough contrast in both themes, and that no source file can put data into the page as markup.

## Browser tests (Playwright and axe)

The tests start a real controller, real agents and a fake Kubernetes API, and drive Chromium against the dashboard. They check what a person sees
(tiles, grouping, live updates, the warning for old data), keyboard use, themes, the wall display, that agent data cannot run as script,
and that the page has no WCAG 2.0 A/AA violations in both themes (axe-core).

- **Browser download:** done by the test fixture with Playwright's own installer, once per machine (about 230 MB). Set `MONITORING_SKIP_BROWSER_INSTALL=true` to skip the tests instead of downloading.
- **System libraries:** Chromium needs a few Linux libraries. If they are missing the tests are skipped and say so. Install them with
  `pwsh bin/Debug/net10.0/playwright.ps1 install-deps chromium`, or `node .playwright/package/cli.js install-deps chromium` from the test output folder; both need root.

## Behind a proxy that inspects TLS

npm and the Playwright download both run on Node. When the proxy replaces certificates, make Node trust the operating system's store instead of turning verification off:

```sh
export NODE_OPTIONS=--use-system-ca
```

Proxy settings for downloads come from `HTTPS_PROXY`, and for npm from the usual `.npmrc`. The lockfile names the public npm registry; npm swaps in the registry you have configured.

## A real database

`tests/Monitoring.Client.Tests/Features/Database` talks to a real PostgreSQL server, to see what a database provider publishes and that it becomes an edge. It skips itself, and says why, when `MONITORING_TEST_POSTGRES` is not set; CI starts a server and sets it. To run it here:

```sh
docker run -d --name pg -e POSTGRES_PASSWORD=test -p 127.0.0.1:54329:5432 postgres:18-alpine
MONITORING_TEST_POSTGRES='Host=127.0.0.1;Port=54329;Username=postgres;Password=test' dotnet test --project tests/Monitoring.Client.Tests
```

`samples/Monitoring.DatabaseSample` is the same check as a Native AOT program: `docker build --build-arg PROJECT=DatabaseSample --build-arg PROJECT_PATH=samples/Monitoring.DatabaseSample -f deploy/Dockerfile .`, then run the image with `MONITORING_SAMPLE_POSTGRES` set.

## Windows

Code that talks to Windows (the Service Control Manager and the like) has tests that skip themselves with an explanation anywhere else. To run them on a Windows machine, publish the test project for it, which works from Linux too, copy the folder over and start the executable:

```sh
dotnet publish tests/Monitoring.Agent.Tests -c Release -r win-x64 --self-contained -p:PublishAot=false -o out
# on Windows:  Monitoring.Agent.Tests.exe -class "Monitoring.Agent.Tests.Features.Discovery.Windows.ServiceControlManagerTests"
```

The numbers Windows uses are not the ones a Linux test can guess: the states of a service start at 1, for example, and an enum that counted from 0 passed every Linux test while it was wrong on the real thing. A mapping of a Windows value therefore needs a test that runs there.

## Budgets

The executables that run next to applications have to stay small. Two checks keep them that way.

**Start time and memory of the published binaries** (`scripts/budgets.sh <folder>`, run by the `aot` job in CI after the smoke test). It starts the Native AOT controller, agent and sidecar, waits for them to be ready and, after three idle seconds, reads their resident memory. The limits are about two and a half times what was measured on Linux x64, so a shared runner's noise does not fail the build but a change that doubles the cost does:

| | Measured | Limit |
| --- | --- | --- |
| Controller: start, idle memory | 35 to 85 ms, 26 to 31 MB | 1,500 ms, 80 MB |
| Agent: start, idle memory | about 30 ms, 15 MB | 1,500 ms, 48 MB |
| Sidecar: start, idle memory | about 20 ms, 12 to 13 MB | 1,500 ms, 40 MB |

The same numbers sit well inside the limits of the Helm charts (controller 512 Mi, agent 128 Mi, sidecar 64 Mi). A sample web application with the client library in it holds about 26 MB. The library's own share is what its buffers keep when they are full (`tests/Monitoring.Client.Tests/Budgets`): the 500 dependencies and 2,048 spans that the default options allow take about 0.7 MB, and the test fails above 3 MB. The start time of the library is part of the start of the application and is not measured separately.

**Scale** (`tests/Monitoring.Controller.Tests/Budgets`). Five thousand instances with fifty series each, the size `docs/design.md` plans for, take about 90 MB in a controller, roughly 360 bytes per series, and the test fails above 150 MB. A snapshot of one hundred such instances is 457 KB as a frame, so a thousand take about 4.5 MB. A frame may be up to 16 MB (`FrameCodec.DefaultMaxFrameBytes`), which holds a scope of several thousand instances, and the test requires a thousand to use less than half of that. The first frame of a connection, which arrives before the peer has proved who it is, may only be 64 KB (`FrameCodec.HandshakeMaxFrameBytes`), so a stranger cannot make the controller or an agent allocate more than that. The size is checked before any memory is allocated for the frame.

**Speed** (`tests/Monitoring.Controller.Tests/Budgets/SpeedBudgetTests.cs`, `tests/Monitoring.Client.Tests/Budgets/OverheadBudgetTests.cs`). At the same size (5,000 instances, 50 series each) a round of deltas from fifty agents is applied in about 2 ms (limit 250 ms), and twenty rules that each hold for 5,000 subjects (100,000 alerts) are evaluated in about 90 ms the first time and 230 ms with the listing afterwards (limits 1 s and 2 s). Counting a call to a dependency in the client library takes about 0.3 microseconds and allocates nothing (limits: 5 microseconds, under a byte per call). The limits are many times what was measured, so a shared runner's noise does not fail the build; each test prints what it measured (`--output Detailed`). Making `EdgeCounters.Record` allocate fails the test. `FanOutBudgetTests` opens 200 dashboard pages over real connections against a controller holding 1,000 instances: every page has its first overview after about 260 ms and a change on every page within one push interval (250 ms in the test); the limit is 10 s for both. Not yet measured: an agent with many sidecars, controller recovery with many agents, and a trend across nightly runs.

## Helm charts

Every chart has unit tests next to it (`tests/*_test.yaml`, [helm-unittest](https://github.com/helm-unittest/helm-unittest)): required values must fail with a clear message, each mode of authentication, transport and TLS must render the right environment, volumes and rights, optional features must be off by default, and the default render of each chart is a reviewed snapshot (`tests/__snapshot__`; after an intended change run `helm unittest -u <chart>` and read the diff). The sidecar library chart is tested through `deploy/helm/test/sample-app`. `deploy/helm/test.sh` lints the charts, runs all of it and checks that the injector adds the same sidecar as the template; install the plugin once with `helm plugin install https://github.com/helm-unittest/helm-unittest --verify=false`. The injector's snapshot uses the cert-manager mode, because the generated certificate of the default mode differs on every render.

## Kubernetes nightly

`deploy/k8s-test/run.sh` builds the images, installs the three charts and a sample application on a throwaway kind cluster and checks the dashboard API of the controller. It needs docker, kind, kubectl and helm; `KEEP=1` leaves the cluster. The `Kubernetes nightly` workflow runs it every night.
