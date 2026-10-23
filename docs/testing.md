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

## Kubernetes nightly

`deploy/k8s-test/run.sh` builds the images, installs the three charts and a sample application on a throwaway kind cluster and checks the dashboard API of the controller. It needs docker, kind, kubectl and helm; `KEEP=1` leaves the cluster. The `Kubernetes nightly` workflow runs it every night.
