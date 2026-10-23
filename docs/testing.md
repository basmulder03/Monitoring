# Testing

`dotnet test` from the repository root runs everything. Most tests need nothing but the .NET SDK. Two groups use external tools,
which are installed for you the first time they are needed, and are skipped with an explanation when that is not possible.

| Tests | Where | Needs | Installed by |
| --- | --- | --- | --- |
| Unit and component tests | `tests/*` | .NET SDK | NuGet |
| Dashboard components and logic, in a DOM (Vitest and jsdom) | `src/Monitoring.Ui/ui/test` | Node and npm | The build of `Monitoring.Ui` runs `npm ci` |
| Dashboard in a real browser, with axe accessibility checks | `tests/Monitoring.Component.Tests/Browser` | A Chromium build, and its system libraries | The test fixture runs Playwright's installer when the browser is missing |

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

## Budgets

The executables that run next to applications have to stay small. Two checks keep them that way.

**Start time and memory of the published binaries** (`scripts/budgets.sh <folder>`, run by the `aot` job in CI after the smoke test). It starts the Native AOT controller, agent and sidecar, waits for them to be ready and, after three idle seconds, reads their resident memory. The limits are about two and a half times what was measured on Linux x64, so a shared runner's noise does not fail the build but a change that doubles the cost does:

| | Measured | Limit |
| --- | --- | --- |
| Controller: start, idle memory | 35 to 85 ms, 26 to 31 MB | 1,500 ms, 80 MB |
| Agent: start, idle memory | about 30 ms, 15 MB | 1,500 ms, 48 MB |
| Sidecar: start, idle memory | about 20 ms, 12 to 13 MB | 1,500 ms, 40 MB |

The same numbers sit well inside the limits of the Helm charts (controller 512 Mi, agent 128 Mi, sidecar 64 Mi). A sample web application with the client library in it holds about 26 MB; the library's own share has not been separated from the application, so it has no budget of its own yet.

**Scale** (`tests/Monitoring.Controller.Tests/Budgets`). Five thousand instances with fifty series each, the size `docs/design.md` plans for, take about 90 MB in a controller, roughly 360 bytes per series, and the test fails above 150 MB. A snapshot of one hundred such instances is 457 KB as a frame, so a thousand take about 4.5 MB. A frame may be up to 16 MB (`FrameCodec.DefaultMaxFrameBytes`), which holds a scope of several thousand instances, and the test requires a thousand to use less than half of that. The first frame of a connection, which arrives before the peer has proved who it is, may only be 64 KB (`FrameCodec.HandshakeMaxFrameBytes`), so a stranger cannot make the controller or an agent allocate more than that. The size is checked before any memory is allocated for the frame.

## Kubernetes nightly

`deploy/k8s-test/run.sh` builds the images, installs the three charts and a sample application on a throwaway kind cluster and checks the dashboard API of the controller. It needs docker, kind, kubectl and helm; `KEEP=1` leaves the cluster. The `Kubernetes nightly` workflow runs it every night.
