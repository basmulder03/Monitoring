// Records the dashboard into docs/public/recordings/*.webm while the demo host plays its incident:
//   dotnet run --project samples/Monitoring.Demo        (in another terminal; leave it running)
//   cd docs && npm run record
// Needs a Chromium that Playwright knows (`npx playwright-core install chromium`, or one from the component tests). Not part of the build: the recordings are committed.
import { chromium } from 'playwright-core'
import { mkdirSync, mkdtempSync, copyFileSync, readdirSync, rmSync } from 'node:fs'
import { join, dirname } from 'node:path'
import { tmpdir } from 'node:os'
import { fileURLToPath } from 'node:url'

const base = process.env.DASHBOARD ?? 'http://127.0.0.1:5080'
const out = join(dirname(fileURLToPath(import.meta.url)), '..', 'public', 'recordings')
mkdirSync(out, { recursive: true })
const size = { width: 1000, height: 640 }

const recordings = [
  // Every scope as a tile; a scope turns degraded, then unhealthy, and recovers, and the alerts come and go.
  ['overview', 24, async page => { await page.goto(base) }],
  // The scope that has the incident: the failing instance opens by itself, with its failing check, and a rollout replaces it.
  ['scope', 24, async page => { await page.goto(base); await page.locator('a.tile', { hasText: 'shop' }).first().click() }],
  // The service graph, with the calls between the services and the database and queue.
  ['dependencies', 14, async page => { await page.goto(`${base}/#/dependencies`) }],
]

const browser = await chromium.launch({ executablePath: process.env.CHROME })
for (const [name, seconds, script] of recordings) {
  const dir = mkdtempSync(join(tmpdir(), 'rec-'))
  const context = await browser.newContext({ viewport: size, recordVideo: { dir, size } })
  const page = await context.newPage()
  await script(page)
  await page.waitForTimeout(seconds * 1000)
  await context.close() // writes the video
  const [file] = readdirSync(dir)
  copyFileSync(join(dir, file), join(out, `${name}.webm`))
  rmSync(dir, { recursive: true })
  console.log('recorded', name)
}
await browser.close()
