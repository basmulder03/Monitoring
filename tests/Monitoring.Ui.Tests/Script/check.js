// Behaviour of the page script in a real DOM (jsdom). Run with: node check.js (see ScriptBehaviourTests).
const { open } = require("./harness");
const assert = require("assert");

const scope = (name, health, labels, extra = {}) =>
  ({ name, health, services: 1, instances: 2, problems: health === "Healthy" ? 0 : 1, labels, agentConnected: true, ...extra });
const overview = (scopes, groupBy = ["environment", "datacenter"], alerts = []) => ({ scopes, alerts, groupBy });
const withOverview = (data, options = {}) => { const page = open(options); page.send("overview", data); return page; };
const all = (page, selector) => [...page.app.querySelectorAll(selector)];

const tests = [];
function test(name, fn) { tests.push([name, fn]); }

test("connects to the live stream", () => {
  const page = open();
  assert.strictEqual(page.eventSources.length, 1);
  assert.strictEqual(page.eventSources[0].url, "/api/events");
});

test("shows a message before any scope has reported", () => {
  const page = withOverview(overview([]));
  assert.ok(page.app.textContent.includes("No scope has reported yet."), page.app.textContent);
});

test("groups scopes by environment and then datacenter, unhealthiest group first", () => {
  const page = withOverview(overview([
    scope("alpha", "Healthy", { environment: "prod", datacenter: "a" }),
    scope("bravo", "Unhealthy", { environment: "test", datacenter: "b" }),
    scope("charlie", "Healthy", { environment: "prod", datacenter: "b" }),
  ]));
  const text = page.app.textContent;
  assert.ok(text.indexOf("environment: test") < text.indexOf("environment: prod"), "test (unhealthy) must come before prod: " + text);
  assert.ok(text.includes("datacenter: a") && text.includes("datacenter: b"), text);
  assert.ok(page.app.querySelector("h3"), "a second grouping level uses a smaller heading");
});

test("puts scopes without the label in a group called 'not set', last", () => {
  const page = withOverview(overview([scope("alpha", "Healthy", { environment: "prod" }), scope("loose", "Unhealthy", {})], ["environment"]));
  const text = page.app.textContent;
  assert.ok(text.includes("environment: not set"), text);
  assert.ok(text.indexOf("environment: prod") < text.indexOf("environment: not set"), "scopes without the label come last: " + text);
});

test("shows flat tiles when grouping is off", () => {
  const page = withOverview(overview([scope("alpha", "Healthy", {}), scope("bravo", "Degraded", {})], []));
  assert.strictEqual(all(page, "a.tile").length, 2);
  assert.strictEqual(all(page, "h3, h4, h5, h6").length, 0);
});

test("each status is shown as an icon and a word", () => {
  const page = withOverview(overview([scope("alpha", "Degraded", {})], []));
  const status = page.app.querySelector(".status");
  assert.ok(status.textContent.includes("Degraded"));
  assert.strictEqual(status.querySelectorAll("svg use").length, 1);
  assert.strictEqual(status.querySelector("use").getAttribute("href"), "#i-degraded");
});

test("narrows the scopes to the filter in the address", () => {
  const scopes = [scope("alpha", "Healthy", { environment: "prod" }), scope("bravo", "Healthy", { environment: "test" })];
  const page = withOverview(overview(scopes, ["environment"]), { url: "http://controller/?filter=environment:prod" });
  assert.ok(page.app.textContent.includes("alpha") && !page.app.textContent.includes("bravo"), page.app.textContent);
});

test("says so when the filter matches nothing", () => {
  const page = withOverview(overview([scope("alpha", "Healthy", { environment: "prod" })], ["environment"]), { url: "http://controller/?filter=environment:nope" });
  assert.ok(page.app.textContent.includes("No scope matches the filter."), page.app.textContent);
});

test("offers a choice of the outermost label when there are several values", () => {
  const page = withOverview(overview([scope("alpha", "Healthy", { environment: "prod" }), scope("bravo", "Healthy", { environment: "test" })], ["environment"]));
  const select = page.app.querySelector("select");
  assert.ok(select, "a select is rendered");
  assert.deepStrictEqual([...select.options].map((o) => o.textContent), ["All", "prod", "test"]);
  assert.strictEqual(page.app.querySelector("label").htmlFor, select.id, "the selector has a label");
});

test("switching the selector narrows the tiles and the address", () => {
  const page = withOverview(overview([scope("alpha", "Healthy", { environment: "prod" }), scope("bravo", "Healthy", { environment: "test" })], ["environment"]));
  const select = page.app.querySelector("select");
  select.value = "test";
  select.dispatchEvent(new page.window.Event("change"));
  assert.deepStrictEqual(all(page, "a.tile").map((tile) => tile.textContent.includes("bravo")), [true]);
  assert.ok(page.window.location.search.includes("filter=environment%3Atest"), page.window.location.search);
  assert.strictEqual(page.app.querySelector("select").value, "test", "the selector keeps showing the choice");
});

test("offers no choice when there is only one value", () => {
  const page = withOverview(overview([scope("alpha", "Healthy", { environment: "prod" })], ["environment"]));
  assert.strictEqual(page.app.querySelectorAll("select").length, 0);
});

test("shows names from agents as text, never as markup", () => {
  const evil = '<img src=x onerror="alert(1)">';
  const page = withOverview(overview([scope(evil, "Healthy", { environment: evil })], ["environment"]));
  assert.ok(page.app.textContent.includes(evil), "shown literally");
  assert.strictEqual(page.app.querySelectorAll("img").length, 0, "no element was created from it");
});

test("links a tile to the scope view with an escaped name", () => {
  const page = withOverview(overview([scope("orders platform", "Healthy", {})], []));
  assert.strictEqual(page.app.querySelector("a.tile").getAttribute("href"), "#/scopes/orders%20platform");
});

test("lists the alerts that are firing", () => {
  const alerts = [{ key: "k", rule: "service-down", subject: "shop/orders", severity: "Critical", since: new Date(Date.now() - 90000).toISOString() }];
  const page = withOverview(overview([scope("alpha", "Healthy", {})], [], alerts));
  const text = page.app.textContent;
  assert.ok(text.includes("shop/orders") && text.includes("Critical") && text.includes("service-down") && text.includes("for 1 min"), text);
});

test("says when no alert is firing", () => {
  const page = withOverview(overview([scope("alpha", "Healthy", {})], []));
  assert.ok(page.app.textContent.includes("No alerts are firing."));
});

test("announces a scope whose health changed", () => {
  const page = withOverview(overview([scope("alpha", "Healthy", {})], []));
  page.send("overview", overview([scope("alpha", "Unhealthy", {})], []));
  assert.strictEqual(page.announcer.textContent, "alpha is now Unhealthy");
});

test("announces nothing for the first overview", () => {
  const page = withOverview(overview([scope("alpha", "Unhealthy", {})], []));
  assert.strictEqual(page.announcer.textContent, "");
});

test("a keep-alive event keeps the stale warning away", () => {
  const page = withOverview(overview([], []));
  page.send("keep-alive", {});
  assert.strictEqual(page.stale.hidden, true);
});

test("warns when the data is old", () => {
  const page = withOverview(overview([], []));
  const realNow = page.window.Date.now;
  page.window.Date.now = () => realNow() + 20000;
  page.timers.forEach((tick) => tick());
  assert.strictEqual(page.stale.hidden, false);
  assert.ok(/The data is \d+ seconds old/.test(page.stale.textContent), page.stale.textContent);
});

test("marks a tile whose agent is not connected, in words", () => {
  const gone = scope("alpha", "Unknown", {}, { agentConnected: false, lastSeen: new Date(Date.now() - 300000).toISOString() });
  const page = withOverview(overview([gone], []));
  const text = page.app.textContent;
  assert.ok(text.includes("Agent not connected, last heard 5 min ago"), text);
  assert.ok(text.includes("Unknown"), text);
});

test("says nothing about the agent for a scope whose agent is connected", () => {
  const page = withOverview(overview([scope("alpha", "Healthy", {})], []));
  assert.ok(!page.app.textContent.includes("not connected"), page.app.textContent);
});

test("shows a banner in the scope view when the agent is not connected", async () => {
  const detail = { name: "shop", health: "Unknown", labels: {}, agentConnected: false, lastSeen: new Date(Date.now() - 120000).toISOString(),
    services: [{ name: "orders", health: "Healthy", liveInstances: 1, healthyInstances: 1, instances: [{ id: "orders-1", lifecycle: "Running", health: "Healthy", labels: {} }] }] };
  const page = withOverview(overview([scope("shop", "Unknown", {}, { agentConnected: false })], []), { url: "http://controller/#/scopes/shop", scopeResponses: { shop: detail } });
  await page.settle();
  const text = page.app.textContent;
  assert.ok(text.includes("The agent of this scope is not connected (last heard 2 min ago)"), text);
  assert.ok(text.includes("orders-1"), "the last known instances are still listed: " + text);
});

test("shows no banner in the scope view when the agent is connected", async () => {
  const detail = { name: "shop", health: "Healthy", labels: { environment: "prod" }, agentConnected: true, services: [] };
  const page = withOverview(overview([scope("shop", "Healthy", {})], []), { url: "http://controller/#/scopes/shop", scopeResponses: { shop: detail } });
  await page.settle();
  assert.ok(!page.app.textContent.includes("not connected"), page.app.textContent);
  assert.ok(page.app.textContent.includes("environment=prod"), page.app.textContent);
});

test("opens the scope view when the address changes and returns with the link back", async () => {
  const detail = { name: "shop", health: "Healthy", labels: {}, agentConnected: true, services: [{ name: "orders", health: "Healthy", liveInstances: 0, healthyInstances: 0, instances: [] }] };
  const page = withOverview(overview([scope("shop", "Healthy", {})], []), { scopeResponses: { shop: detail } });
  page.window.location.hash = "#/scopes/shop";
  await page.settle();
  assert.ok(page.app.textContent.includes("orders"), page.app.textContent);
  page.window.location.hash = "#/";
  await page.settle();
  assert.strictEqual(all(page, "a.tile").length, 1, "back on the overview");
});

test("expands the services that are not healthy", async () => {
  const instance = { id: "i-1", lifecycle: "Running", health: "Healthy", labels: {} };
  const detail = { name: "shop", health: "Degraded", labels: {}, agentConnected: true, services: [
    { name: "fine", health: "Healthy", liveInstances: 1, healthyInstances: 1, instances: [instance] },
    { name: "broken", health: "Unhealthy", liveInstances: 1, healthyInstances: 0, instances: [instance] }] };
  const page = withOverview(overview([scope("shop", "Degraded", {})], []), { url: "http://controller/#/scopes/shop", scopeResponses: { shop: detail } });
  await page.settle();
  const open = all(page, "details.service").map((d) => [d.querySelector("strong").textContent, d.open]);
  assert.deepStrictEqual(open, [["fine", false], ["broken", true]]);
});

test("says so when the scope in the address is not known", async () => {
  const page = withOverview(overview([], []), { url: "http://controller/#/scopes/nowhere" });
  await page.settle();
  assert.ok(page.app.textContent.includes("The scope 'nowhere' is not known."), page.app.textContent);
});

test("the theme button switches between the light and the dark theme", () => {
  const page = open();
  const button = page.document.getElementById("theme-toggle");
  button.click();
  assert.strictEqual(page.document.documentElement.dataset.theme, "dark");
  button.click();
  assert.strictEqual(page.document.documentElement.dataset.theme, "light");
});

test("the address can force the theme", () => {
  const page = open({ url: "http://controller/?theme=dark" });
  assert.strictEqual(page.document.documentElement.dataset.theme, "dark");
});

test("the wall display page gets its own class", () => {
  const page = open({ url: "http://controller/tv" });
  assert.ok(page.document.body.classList.contains("tv"));
});

(async () => {
  let failures = 0;
  for (const [name, fn] of tests) {
    try { await fn(); console.log("ok   -", name); } catch (e) { failures++; console.log("FAIL -", name, "\n      ", e.message); }
  }
  if (failures > 0) { console.log(failures + " failed"); process.exit(1); }
  console.log("all passed");
})();
