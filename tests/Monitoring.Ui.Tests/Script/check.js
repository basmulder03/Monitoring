// Behaviour of the page script, checked in a stand-in DOM. Run with: node check.js (see ScriptBehaviourTests).
const { run } = require("./harness");
const assert = require("assert");
const scope = (name, health, labels, extra = {}) => ({ name, health, services: 1, instances: 2, problems: health === "Healthy" ? 0 : 1, labels, agentConnected: true, ...extra });
const overview = (scopes, groupBy = ["environment", "datacenter"], alerts = []) => ({ scopes, alerts, groupBy });

let failures = 0;
const tests = [];
function test(name, fn) { tests.push([name, fn]); }
const settle = () => new Promise((resolve) => setImmediate(resolve));

test("connects to the live stream", () => {
  const { eventSources } = run({});
  assert.strictEqual(eventSources.length, 1);
  assert.strictEqual(eventSources[0].url, "/api/events");
});

test("shows a message before any scope has reported", () => {
  const { app } = run({ events: [["overview", overview([])]] });
  assert.ok(app.textContent.includes("No scope has reported yet."), app.textContent);
});

test("groups scopes by environment and then datacenter, unhealthiest group first", () => {
  const scopes = [
    scope("alpha", "Healthy", { environment: "prod", datacenter: "a" }),
    scope("bravo", "Unhealthy", { environment: "test", datacenter: "b" }),
    scope("charlie", "Healthy", { environment: "prod", datacenter: "b" }),
  ];
  const { app } = run({ events: [["overview", overview(scopes)]] });
  const text = app.textContent;
  assert.ok(text.indexOf("environment: test") < text.indexOf("environment: prod"), "test (unhealthy) must come before prod: " + text);
  assert.ok(text.includes("datacenter: a") && text.includes("datacenter: b"), text);
  const headings = app.find((n) => /^h[2-6]$/.test(n.tagName)).map((n) => n.tagName);
  assert.ok(headings.includes("h3"), "a second grouping level uses a smaller heading: " + headings);
});

test("puts scopes without the label in a group called 'not set', last", () => {
  const scopes = [scope("alpha", "Healthy", { environment: "prod" }), scope("loose", "Unhealthy", {})];
  const { app } = run({ events: [["overview", overview(scopes, ["environment"])]] });
  const text = app.textContent;
  assert.ok(text.includes("environment: not set"), text);
  assert.ok(text.indexOf("environment: prod") < text.indexOf("environment: not set"), "scopes without the label come last: " + text);
});

test("shows flat tiles when grouping is off", () => {
  const { app } = run({ events: [["overview", overview([scope("alpha", "Healthy", {}), scope("bravo", "Degraded", {})], [])]] });
  assert.strictEqual(app.find((n) => n.className.startsWith("tile ")).length, 2);
  assert.strictEqual(app.find((n) => /^h[3-6]$/.test(n.tagName)).length, 0);
});

test("each status is shown as an icon and a word", () => {
  const { app } = run({ events: [["overview", overview([scope("alpha", "Degraded", {})], [])]] });
  const status = app.find((n) => n.className.startsWith("status "))[0];
  assert.ok(status.textContent.includes("Degraded"));
  assert.ok(status.find((n) => n.tagName === "svg").length === 1);
});

test("narrows the scopes to the filter in the address", () => {
  const scopes = [scope("alpha", "Healthy", { environment: "prod" }), scope("bravo", "Healthy", { environment: "test" })];
  const { app } = run({ search: "?filter=environment:prod", events: [["overview", overview(scopes, ["environment"])]] });
  assert.ok(app.textContent.includes("alpha") && !app.textContent.includes("bravo"), app.textContent);
});

test("says so when the filter matches nothing", () => {
  const { app } = run({ search: "?filter=environment:nope", events: [["overview", overview([scope("alpha", "Healthy", { environment: "prod" })], ["environment"])]] });
  assert.ok(app.textContent.includes("No scope matches the filter."), app.textContent);
});

test("offers a choice of the outermost label when there are several values", () => {
  const scopes = [scope("alpha", "Healthy", { environment: "prod" }), scope("bravo", "Healthy", { environment: "test" })];
  const { app } = run({ events: [["overview", overview(scopes, ["environment"])]] });
  const select = app.find((n) => n.tagName === "select")[0];
  assert.ok(select, "a select is rendered");
  assert.deepStrictEqual(select.children.map((o) => o.textContent), ["All", "prod", "test"]);
});

test("offers no choice when there is only one value", () => {
  const { app } = run({ events: [["overview", overview([scope("alpha", "Healthy", { environment: "prod" })], ["environment"])]] });
  assert.strictEqual(app.find((n) => n.tagName === "select").length, 0);
});

test("shows names from agents as text, never as markup", () => {
  const evil = '<img src=x onerror="alert(1)">';
  const { app } = run({ events: [["overview", overview([scope(evil, "Healthy", { environment: evil })], ["environment"])]] });
  assert.ok(app.textContent.includes(evil), "shown literally");
  assert.strictEqual(app.find((n) => n.tagName === "img").length, 0, "no element was created from it");
});

test("links a tile to the scope view with an escaped name", () => {
  const { app } = run({ events: [["overview", overview([scope("orders platform", "Healthy", {})], [])]] });
  const link = app.find((n) => n.className.startsWith("tile "))[0];
  assert.strictEqual(link.href, "#/scopes/orders%20platform");
});

test("lists the alerts that are firing", () => {
  const alerts = [{ key: "k", rule: "service-down", subject: "shop/orders", severity: "Critical", since: new Date(Date.now() - 90000).toISOString() }];
  const { app } = run({ events: [["overview", overview([scope("alpha", "Healthy", {})], [], alerts)]] });
  assert.ok(app.textContent.includes("shop/orders") && app.textContent.includes("Critical") && app.textContent.includes("service-down"), app.textContent);
});

test("says when no alert is firing", () => {
  const { app } = run({ events: [["overview", overview([scope("alpha", "Healthy", {})], [])]] });
  assert.ok(app.textContent.includes("No alerts are firing."));
});

test("announces a scope whose health changed", () => {
  const { announcer, eventSources } = run({ events: [["overview", overview([scope("alpha", "Healthy", {})], [])]] });
  eventSources[0].listeners.overview({ data: JSON.stringify(overview([scope("alpha", "Unhealthy", {})], [])) });
  assert.strictEqual(announcer.textContent, "alpha is now Unhealthy");
});

test("announces nothing for the first overview", () => {
  const { announcer } = run({ events: [["overview", overview([scope("alpha", "Unhealthy", {})], [])]] });
  assert.strictEqual(announcer.textContent, "");
});

test("a keep-alive event keeps the stale warning away", () => {
  const { stale, eventSources } = run({ events: [["overview", overview([], [])]] });
  eventSources[0].listeners["keep-alive"]({ data: "{}" });
  assert.strictEqual(stale.hidden, true);
});

test("marks a tile whose agent is not connected, in words", () => {
  const gone = scope("alpha", "Unknown", {}, { agentConnected: false, lastSeen: new Date(Date.now() - 300000).toISOString() });
  const { app } = run({ events: [["overview", overview([gone], [])]] });
  const text = app.textContent;
  assert.ok(text.includes("Agent not connected, last heard 5 min ago"), text);
  assert.ok(text.includes("Unknown"), text);
});

test("says nothing about the agent for a scope whose agent is connected", () => {
  const { app } = run({ events: [["overview", overview([scope("alpha", "Healthy", {})], [])]] });
  assert.ok(!app.textContent.includes("not connected"), app.textContent);
});

test("shows a banner in the scope view when the agent is not connected", async () => {
  const detail = { name: "shop", health: "Unknown", labels: {}, agentConnected: false, lastSeen: new Date(Date.now() - 120000).toISOString(),
    services: [{ name: "orders", health: "Healthy", liveInstances: 1, healthyInstances: 1, instances: [{ id: "orders-1", lifecycle: "Running", health: "Healthy", labels: {} }] }] };
  const { app } = run({ hash: "#/scopes/shop", scopeResponses: { shop: detail }, events: [["overview", overview([scope("shop", "Unknown", {}, { agentConnected: false })], [])]] });
  await settle(); await settle();
  const text = app.textContent;
  assert.ok(text.includes("The agent of this scope is not connected (last heard 2 min ago)"), text);
  assert.ok(text.includes("orders-1"), "the last known instances are still listed: " + text);
});

test("shows no banner in the scope view when the agent is connected", async () => {
  const detail = { name: "shop", health: "Healthy", labels: { environment: "prod" }, agentConnected: true, services: [] };
  const { app } = run({ hash: "#/scopes/shop", scopeResponses: { shop: detail }, events: [["overview", overview([scope("shop", "Healthy", {})], [])]] });
  await settle(); await settle();
  assert.ok(!app.textContent.includes("not connected"), app.textContent);
  assert.ok(app.textContent.includes("environment=prod"), app.textContent);
});

test("says so when the scope in the address is not known", async () => {
  const { app } = run({ hash: "#/scopes/nowhere", events: [["overview", overview([], [])]] });
  await settle(); await settle();
  assert.ok(app.textContent.includes("The scope 'nowhere' is not known."), app.textContent);
});

(async () => {
  for (const [name, fn] of tests) {
    try { await fn(); console.log("ok   -", name); } catch (e) { failures++; console.log("FAIL -", name, "\n      ", e.message); }
  }
  if (failures > 0) { console.log(failures + " failed"); process.exit(1); }
  console.log("all passed");
})();
