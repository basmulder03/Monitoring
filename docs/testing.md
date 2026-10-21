# Testing

`dotnet test` from the repository root runs everything. Most tests need nothing but the .NET SDK. Two groups use external tools,
which are installed for you the first time they are needed, and are skipped with an explanation when that is not possible.

| Tests | Where | Needs | Installed by |
| --- | --- | --- | --- |
| Unit and component tests | `tests/*` | .NET SDK | NuGet |
| Page script in a DOM (jsdom) | `tests/Monitoring.Ui.Tests` | Node and npm | The build runs `npm ci` when `node_modules` is missing |
| Dashboard in a real browser, with axe accessibility checks | `tests/Monitoring.Component.Tests/Browser` | A Chromium build, and its system libraries | The test fixture runs Playwright's installer when the browser is missing |

## Page script (jsdom)

`tests/Monitoring.Ui.Tests` runs the real `index.html` and `app.js` in jsdom and checks grouping, filtering, escaping of agent data,
alerts, announcements and themes. `package.json` and `package-lock.json` list the dependencies; the build runs `npm ci` itself.
To do it by hand, run `npm ci` in that folder. Set `SkipNodeDependencies=true` to stop the build from doing it.

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
