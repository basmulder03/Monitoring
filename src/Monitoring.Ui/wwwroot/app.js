"use strict";

(function () {
  // Everything shown comes from agents, so it is only ever put into the page as text, never as markup.
  const STALE_AFTER_MS = 15000;
  const LABELS = { Healthy: "Healthy", Degraded: "Degraded", Unhealthy: "Unhealthy", Unknown: "Unknown" };

  const app = document.getElementById("app");
  const stale = document.getElementById("stale");
  const announcer = document.getElementById("announcer");
  const params = new URLSearchParams(location.search);

  let overview = null;
  let lastSeen = Date.now();
  let previousHealth = new Map();
  let scopeRequest = 0;

  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  function icon(id) {
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    svg.setAttribute("class", "icon");
    svg.setAttribute("aria-hidden", "true");
    const use = document.createElementNS("http://www.w3.org/2000/svg", "use");
    use.setAttribute("href", "#i-" + id);
    svg.appendChild(use);
    return svg;
  }

  // A status is always an icon and a word, never a colour alone.
  function status(health) {
    const kind = (LABELS[health] ? health : "Unknown").toLowerCase();
    const node = el("span", "status " + kind);
    node.appendChild(icon(kind));
    node.appendChild(el("span", "", LABELS[health] || "Unknown"));
    return node;
  }

  function age(since) {
    const seconds = Math.max(0, Math.round((Date.now() - new Date(since).getTime()) / 1000));
    if (seconds < 60) return seconds + " s";
    if (seconds < 3600) return Math.floor(seconds / 60) + " min";
    return Math.floor(seconds / 3600) + " h " + Math.floor((seconds % 3600) / 60) + " min";
  }

  function plural(count, one, many) {
    return count + " " + (count === 1 ? one : many);
  }

  function renderOverview() {
    const root = el("div");
    root.appendChild(el("h2", "", "Scopes"));

    if (!overview || overview.scopes.length === 0) {
      root.appendChild(el("p", "muted", "No scope has reported yet."));
    } else {
      const list = el("ul", "tiles");
      for (const scope of overview.scopes) {
        const item = el("li");
        const link = el("a", "tile " + scope.health.toLowerCase());
        link.href = "#/scopes/" + encodeURIComponent(scope.name);
        const title = el("span", "name");
        title.appendChild(icon("scope"));
        title.appendChild(document.createTextNode(" " + scope.name));
        link.appendChild(title);
        link.appendChild(status(scope.health));
        link.appendChild(el("span", "muted",
          plural(scope.services, "service", "services") + ", " + plural(scope.instances, "instance", "instances") +
          (scope.problems > 0 ? ", " + scope.problems + " not healthy" : "")));
        item.appendChild(link);
        list.appendChild(item);
      }
      root.appendChild(list);
    }

    root.appendChild(renderAlerts());
    return root;
  }

  function renderAlerts() {
    const section = el("section");
    const heading = el("h2");
    heading.appendChild(icon("bell"));
    heading.appendChild(document.createTextNode(" Alerts"));
    section.appendChild(heading);

    if (!overview || overview.alerts.length === 0) {
      section.appendChild(el("p", "muted", "No alerts are firing."));
      return section;
    }

    const list = el("ul", "alerts");
    for (const alert of overview.alerts) {
      const item = el("li", "alert");
      item.appendChild(el("span", "severity " + alert.severity.toLowerCase(), alert.severity));
      item.appendChild(el("span", "", alert.subject));
      item.appendChild(el("span", "muted", alert.rule + ", for " + age(alert.since)));
      list.appendChild(item);
    }

    section.appendChild(list);
    return section;
  }

  function renderScope(scope) {
    const root = el("div");
    const back = el("a", "", "← All scopes");
    back.href = "#/";
    root.appendChild(back);
    const heading = el("h2", "", scope.name + " ");
    heading.appendChild(status(scope.health));
    root.appendChild(heading);

    if (scope.services.length === 0) {
      root.appendChild(el("p", "muted", "This scope has no services."));
    }

    for (const service of scope.services) {
      const details = el("details", "service");
      details.open = service.health !== "Healthy";
      const summary = el("summary");
      summary.appendChild(el("strong", "", service.name));
      summary.appendChild(status(service.health));
      summary.appendChild(el("span", "muted", service.healthyInstances + " of " + plural(service.liveInstances, "instance", "instances") + " healthy"));
      details.appendChild(summary);

      const table = el("table", "instances");
      const head = el("tr");
      for (const title of ["Instance", "State", "Health", "Labels"]) head.appendChild(el("th", "", title));
      table.appendChild(head);
      for (const instance of service.instances) {
        const row = el("tr");
        row.appendChild(el("td", "", instance.id));
        row.appendChild(el("td", "", instance.lifecycle));
        const health = el("td");
        health.appendChild(status(instance.health));
        row.appendChild(health);
        row.appendChild(el("td", "muted", Object.entries(instance.labels).map(([k, v]) => k + "=" + v).join(", ")));
        table.appendChild(row);
      }
      details.appendChild(table);
      root.appendChild(details);
    }

    return root;
  }

  function show(content) {
    // Keep the focus where it is when only the data changed; the view is rebuilt in place.
    app.replaceChildren(content);
  }

  function routeScopeName() {
    const match = /^#\/scopes\/(.+)$/.exec(location.hash);
    return match ? decodeURIComponent(match[1]) : null;
  }

  async function render() {
    const scopeName = routeScopeName();
    if (scopeName === null) {
      show(renderOverview());
      return;
    }

    const request = ++scopeRequest;
    try {
      const response = await fetch("/api/scopes/" + encodeURIComponent(scopeName));
      if (request !== scopeRequest) return;
      if (response.status === 404) {
        const missing = el("div");
        const back = el("a", "", "← All scopes");
        back.href = "#/";
        missing.appendChild(back);
        missing.appendChild(el("p", "", "The scope '" + scopeName + "' is not known."));
        show(missing);
        return;
      }
      show(renderScope(await response.json()));
    } catch (error) {
      if (request === scopeRequest) show(el("p", "muted", "The scope could not be loaded."));
    }
  }

  // Tell screen reader users what changed, in words, without moving the focus.
  function announceChanges() {
    const now = new Map(overview.scopes.map((scope) => [scope.name, scope.health]));
    const messages = [];
    for (const [name, health] of now) {
      const before = previousHealth.get(name);
      if (before !== undefined && before !== health) messages.push(name + " is now " + (LABELS[health] || health));
    }
    previousHealth = now;
    if (messages.length > 0) announcer.textContent = messages.join(". ");
  }

  function touch() {
    lastSeen = Date.now();
    stale.hidden = true;
  }

  function checkStale() {
    const seconds = Math.round((Date.now() - lastSeen) / 1000);
    if (seconds * 1000 >= STALE_AFTER_MS) {
      stale.textContent = "The data is " + seconds + " seconds old. The connection to the controller may be lost.";
      stale.hidden = false;
    }
  }

  function connect() {
    const source = new EventSource("/api/events");
    source.addEventListener("overview", (event) => {
      touch();
      overview = JSON.parse(event.data);
      announceChanges();
      render();
    });
    source.addEventListener("keep-alive", touch);
  }

  function applyTheme() {
    const forced = params.get("theme");
    let theme = forced === "dark" || forced === "light" ? forced : null;
    if (!theme) {
      try { theme = localStorage.getItem("theme"); } catch (error) { theme = null; }
    }
    if (theme === "dark" || theme === "light") document.documentElement.dataset.theme = theme;

    document.getElementById("theme-toggle").addEventListener("click", () => {
      const dark = document.documentElement.dataset.theme
        ? document.documentElement.dataset.theme === "dark"
        : matchMedia("(prefers-color-scheme: dark)").matches;
      const next = dark ? "light" : "dark";
      document.documentElement.dataset.theme = next;
      try { localStorage.setItem("theme", next); } catch (error) { /* a remembered theme is a convenience only */ }
    });
  }

  if (location.pathname === "/tv") document.body.classList.add("tv");
  applyTheme();
  window.addEventListener("hashchange", render);
  setInterval(checkStale, 1000);
  setInterval(() => { if (overview && routeScopeName() === null) render(); }, 30000);
  connect();
})();
