// A minimal DOM stand-in, enough to run the page script and read back what it renders.
// The path of the script comes from the MONITORING_UI_SCRIPT environment variable.
const fs = require("fs");
const vm = require("vm");

class FakeNode {
  constructor(tag) { this.tagName = tag.toLowerCase(); this.children = []; this.attributes = {}; this.className = ""; this.dataset = {}; this.listeners = {}; this._text = ""; this.hidden = false; this.value = ""; }
  appendChild(child) { this.children.push(child); return child; }
  replaceChildren(...nodes) { this.children = nodes; this._text = ""; }
  setAttribute(name, value) { this.attributes[name] = value; }
  addEventListener(type, fn) { (this.listeners[type] ||= []).push(fn); }
  set textContent(value) { this.children = []; this._text = String(value); }
  get textContent() { return this._text + this.children.map((c) => c.textContent).join(""); }
  set href(value) { this.attributes.href = value; }
  get href() { return this.attributes.href; }
  find(predicate, found = []) { if (predicate(this)) found.push(this); this.children.forEach((c) => c.find && c.find(predicate, found)); return found; }
}

function run({ search = "", hash = "", events = [], pathname = "/", scopeResponses = {} }) {
  const byId = {};
  for (const id of ["app", "stale", "announcer", "theme-toggle", "home"]) byId[id] = new FakeNode("div");
  const eventSources = [];
  const document = {
    getElementById: (id) => byId[id],
    createElement: (tag) => new FakeNode(tag),
    createElementNS: (ns, tag) => new FakeNode(tag),
    createTextNode: (text) => { const n = new FakeNode("#text"); n._text = text; return n; },
    documentElement: { dataset: {} },
    body: { classList: { add() {} } },
  };
  const context = {
    document, URLSearchParams, URL, console, setInterval: () => 0, Date, Map, Set, Math, Object, Array, JSON, encodeURIComponent, decodeURIComponent, RegExp,
    location: { pathname, hash, search, href: "http://controller" + pathname + search },
    history: { replaceState() {} },
    matchMedia: () => ({ matches: false }),
    localStorage: { getItem: () => null, setItem() {} },
    EventSource: class { constructor(url) { this.url = url; this.listeners = {}; eventSources.push(this); } addEventListener(t, f) { this.listeners[t] = f; } },
    fetch: async (url) => { const body = scopeResponses[decodeURIComponent(url.replace("/api/scopes/", ""))]; return body ? { status: 200, json: async () => body } : { status: 404, json: async () => ({}) }; },
  };
  context.window = { addEventListener(type, fn) { (context.hashListeners ||= []).push(fn); } };
  vm.createContext(context);
  vm.runInContext(fs.readFileSync(process.env.MONITORING_UI_SCRIPT, "utf8"), context);
  for (const [name, data] of events) eventSources[0].listeners[name]({ data: JSON.stringify(data) });
  return { app: byId.app, stale: byId.stale, announcer: byId.announcer, eventSources, context };
}
module.exports = { run };
