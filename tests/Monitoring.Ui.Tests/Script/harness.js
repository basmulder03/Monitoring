// Runs the real page (index.html and app.js) in jsdom, with the browser features jsdom does not have replaced by simple fakes.
// The folder with index.html and app.js comes from the MONITORING_UI_DIR environment variable.
const fs = require("fs");
const path = require("path");
const { JSDOM } = require("jsdom");

function open({ url = "http://controller/", scopeResponses = {}, topology = { nodes: [], edges: [] } } = {}) {
  const dir = process.env.MONITORING_UI_DIR;
  const html = fs.readFileSync(path.join(dir, "index.html"), "utf8");
  const script = fs.readFileSync(path.join(dir, "app.js"), "utf8");

  // Scripts in the page are not run by jsdom here; the page script is evaluated once everything it needs is in place.
  const dom = new JSDOM(html, { url, runScripts: "outside-only", pretendToBeVisual: true });
  const { window } = dom;

  const eventSources = [];
  window.EventSource = class {
    constructor(address) { this.url = address; this.listeners = {}; eventSources.push(this); }
    addEventListener(type, listener) { this.listeners[type] = listener; }
  };
  window.fetch = async (address) => {
    if (address === "/api/topology") return { status: 200, json: async () => topology };
    const name = decodeURIComponent(address.replace("/api/scopes/", ""));
    const body = scopeResponses[name];
    return body ? { status: 200, json: async () => body } : { status: 404, json: async () => ({}) };
  };
  window.matchMedia = () => ({ matches: false });

  // No timers of its own: the checks that run every second or every 30 seconds are called by the tests when needed.
  const timers = [];
  window.setInterval = (callback) => { timers.push(callback); return timers.length; };

  window.eval(script);

  const page = {
    window,
    document: window.document,
    app: window.document.getElementById("app"),
    stale: window.document.getElementById("stale"),
    announcer: window.document.getElementById("announcer"),
    timers,
    send(name, data) { eventSources[0].listeners[name]({ data: JSON.stringify(data) }); },
    eventSources,
    // Lets pending promises (the scope view fetches its data) finish.
    async settle() { for (let i = 0; i < 4; i++) await new Promise((resolve) => setImmediate(resolve)); },
  };
  return page;
}

module.exports = { open };
