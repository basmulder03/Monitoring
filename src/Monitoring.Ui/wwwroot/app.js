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
  let selectedNode = null;
  const layouts = new Map();

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

  // The scopes to show are narrowed by ?filter=key:value (repeatable), which also keeps a wall display on one environment.
  function readFilters() {
    const map = new Map();
    for (const entry of params.getAll("filter")) {
      const colon = entry.indexOf(":");
      if (colon > 0) map.set(entry.slice(0, colon), entry.slice(colon + 1));
    }
    return map;
  }

  const filters = readFilters();

  function matchesFilters(scope) {
    for (const [key, value] of filters) {
      if ((scope.labels[key] || "") !== value) return false;
    }
    return true;
  }

  const SEVERITY = ["Healthy", "Unknown", "Degraded", "Unhealthy"];

  function worstOf(scopes) {
    let worst = "Healthy";
    for (const scope of scopes) {
      if (SEVERITY.indexOf(scope.health) > SEVERITY.indexOf(worst)) worst = scope.health;
    }
    return worst;
  }

  function tiles(scopes) {
    const list = el("ul", "tiles");
    for (const scope of scopes) {
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
      if (!scope.agentConnected) {
        // Said in words as well as shown grey: the counts above are the last known, not current.
        link.appendChild(el("span", "disconnected", "Agent not connected" + (scope.lastSeen ? ", last heard " + age(scope.lastSeen) + " ago" : "")));
      }
      item.appendChild(link);
      list.appendChild(item);
    }
    return list;
  }

  // Groups scopes by the first label, then by the next inside each group; scopes without the label go last.
  function groups(scopes, keys, depth) {
    if (keys.length === 0) return tiles(scopes);

    const byValue = new Map();
    for (const scope of scopes) {
      const value = scope.labels[keys[0]] || null;
      if (!byValue.has(value)) byValue.set(value, []);
      byValue.get(value).push(scope);
    }

    const ordered = [...byValue.entries()].sort(([nameA, a], [nameB, b]) => {
      if (nameA === null || nameB === null) return nameA === null ? 1 : -1;
      const bySeverity = SEVERITY.indexOf(worstOf(b)) - SEVERITY.indexOf(worstOf(a));
      return bySeverity !== 0 ? bySeverity : nameA.localeCompare(nameB);
    });

    const container = el("div");
    for (const [value, members] of ordered) {
      const section = el("section", "group");
      const heading = el("h" + Math.min(depth + 2, 6));
      heading.appendChild(status(worstOf(members)));
      heading.appendChild(document.createTextNode(" " + keys[0] + ": " + (value === null ? "not set" : value)));
      section.appendChild(heading);
      section.appendChild(groups(members, keys.slice(1), depth + 1));
      container.appendChild(section);
    }
    return container;
  }

  // Lets the reader pick one value of the outermost label, for example a single environment, or all of them.
  function filterControl() {
    const key = overview.groupBy[0];
    const values = [...new Set(overview.scopes.map((scope) => scope.labels[key]).filter((value) => value))].sort();
    if (!key || values.length < 2) return null;

    const wrapper = el("div", "filter");
    const label = el("label", "", "Show " + key + " ");
    label.htmlFor = "filter-select";
    const select = el("select");
    select.id = "filter-select";
    const all = el("option", "", "All");
    all.value = "";
    select.appendChild(all);
    for (const value of values) {
      const option = el("option", "", value);
      option.value = value;
      select.appendChild(option);
    }
    select.value = filters.get(key) || "";
    select.addEventListener("change", () => {
      if (select.value) filters.set(key, select.value); else filters.delete(key);
      const url = new URL(location.href);
      url.searchParams.delete("filter");
      for (const [k, v] of filters) url.searchParams.append("filter", k + ":" + v);
      history.replaceState(null, "", url);
      render();
    });
    wrapper.appendChild(label);
    wrapper.appendChild(select);
    return wrapper;
  }

  function renderOverview() {
    const root = el("div");
    root.appendChild(el("h2", "", "Scopes"));

    const visible = overview ? overview.scopes.filter(matchesFilters) : [];
    if (visible.length === 0) {
      root.appendChild(el("p", "muted", overview && overview.scopes.length > 0 ? "No scope matches the filter." : "No scope has reported yet."));
    } else {
      const control = filterControl();
      if (control) root.appendChild(control);
      root.appendChild(groups(visible, overview.groupBy, 1));
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

    if (!scope.agentConnected) {
      root.appendChild(el("p", "banner", "The agent of this scope is not connected" +
        (scope.lastSeen ? " (last heard " + age(scope.lastSeen) + " ago)" : "") + ". What is shown is the state it last reported."));
    }

    const labels = Object.entries(scope.labels).map(([key, value]) => key + "=" + value).join(", ");
    if (labels) root.appendChild(el("p", "muted", labels));

    if (scope.services.length === 0) {
      root.appendChild(el("p", "muted", "This scope has no services."));
    }

    for (const service of scope.services) {
      const details = el("details", "service");
      remember(details, "service:" + scope.name + "/" + service.name, service.health !== "Healthy");
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
        const name = el("td");
        name.appendChild(renderInstance(scope.name, instance));
        row.appendChild(name);
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

  // The view is rebuilt on every update, so what a person opened is remembered here instead of in the elements.
  const opened = new Map();

  function remember(details, key, openByDefault) {
    details.open = opened.has(key) ? opened.get(key) : openByDefault;
    details.addEventListener("toggle", () => opened.set(key, details.open));
  }

  function renderInstance(scopeName, instance) {
    const details = el("details", "instance");
    remember(details, "instance:" + scopeName + "/" + instance.id, false);
    const summary = el("summary", "", instance.id);
    if (instance.version) summary.appendChild(el("span", "muted", " " + instance.version));
    details.appendChild(summary);

    if (instance.checks.length > 0) {
      details.appendChild(el("h4", "", "Health checks"));
      const list = el("ul", "checks");
      for (const check of instance.checks) {
        const item = el("li");
        item.appendChild(status(check.health));
        item.appendChild(el("strong", "", " " + check.name));
        if (check.description) item.appendChild(el("span", "muted", " " + check.description));
        list.appendChild(item);
      }
      details.appendChild(list);
    }

    if (instance.containers.length > 0) {
      details.appendChild(el("h4", "", "Containers"));
      const table = el("table", "containers");
      const head = el("tr");
      for (const title of ["Container", "Role", "State", "Health", "Restarts", "Image"]) head.appendChild(el("th", "", title));
      table.appendChild(head);
      for (const container of instance.containers) {
        const row = el("tr");
        row.appendChild(el("td", "", container.name));
        row.appendChild(el("td", "", roleText(container.role)));
        row.appendChild(el("td", "", stateText(container)));
        const health = el("td");
        health.appendChild(status(container.health));
        row.appendChild(health);
        row.appendChild(el("td", "", String(container.restarts) + lastRunText(container)));
        row.appendChild(el("td", "muted", container.image || ""));
        table.appendChild(row);
      }
      details.appendChild(table);
    }

    if (instance.metrics.length > 0) {
      details.appendChild(el("p", "muted", plural(instance.metrics.length, "metric", "metrics") + " reported"));
    }

    return details;
  }

  function roleText(role) {
    return { App: "Application", MonitoringSidecar: "Monitoring sidecar", OtherSidecar: "Sidecar", Init: "Init" }[role] || role;
  }

  function stateText(container) {
    const detail = container.reason ? ": " + container.reason : "";
    const exit = container.phase === "Terminated" && container.exitCode !== undefined ? " (exit " + container.exitCode + ")" : "";
    return container.phase + detail + exit;
  }

  function lastRunText(container) {
    if (container.restarts === 0 || !container.lastReason) return "";
    return " (last: " + container.lastReason + (container.lastExitCode !== undefined ? ", exit " + container.lastExitCode : "") + ")";
  }

  function show(content) {
    // Keep the focus where it is when only the data changed; the view is rebuilt in place.
    app.replaceChildren(content);
  }

  // ----- Dependencies -----------------------------------------------------------------------------------------------

  const NODE_WIDTH = 190;
  const NODE_HEIGHT = 52;
  const COLUMN_GAP = 110;
  const ROW_GAP = 28;
  const MARGIN = 20;
  const SVG_NS = "http://www.w3.org/2000/svg";

  function svgEl(tag, attributes, text) {
    const node = document.createElementNS(SVG_NS, tag);
    for (const [name, value] of Object.entries(attributes || {})) node.setAttribute(name, value);
    if (text !== undefined) node.textContent = text;
    return node;
  }

  // Places each node in a column after everything that calls it. The result depends only on which nodes and edges exist,
  // so the picture stays where it is while the numbers change, and only moves when the topology does.
  function layout(graph) {
    const signature = graph.nodes.map((n) => n.id).join("|") + "#" + graph.edges.map((e) => e.from + ">" + e.to).join("|");
    const known = layouts.get("current");
    if (known && known.signature === signature) return known.positions;

    const rank = new Map(graph.nodes.map((n) => [n.id, 0]));
    // Longest path from the callers; the limit stops a cycle (two services calling each other) from looping for ever.
    for (let pass = 0; pass < graph.nodes.length; pass++) {
      let changed = false;
      for (const edge of graph.edges) {
        if (edge.from === edge.to) continue;
        if (rank.get(edge.to) < rank.get(edge.from) + 1 && rank.get(edge.from) + 1 < graph.nodes.length) {
          rank.set(edge.to, rank.get(edge.from) + 1);
          changed = true;
        }
      }
      if (!changed) break;
    }

    const columns = new Map();
    for (const node of graph.nodes) {
      const column = rank.get(node.id);
      if (!columns.has(column)) columns.set(column, []);
      columns.get(column).push(node);
    }

    const positions = new Map();
    let height = 0;
    let width = 0;
    for (const [column, nodes] of [...columns.entries()].sort((a, b) => a[0] - b[0])) {
      nodes.sort((a, b) => a.id.localeCompare(b.id));
      nodes.forEach((node, row) => {
        positions.set(node.id, { x: MARGIN + column * (NODE_WIDTH + COLUMN_GAP), y: MARGIN + row * (NODE_HEIGHT + ROW_GAP) });
      });
      height = Math.max(height, MARGIN * 2 + nodes.length * (NODE_HEIGHT + ROW_GAP) - ROW_GAP);
      width = MARGIN * 2 + (column + 1) * (NODE_WIDTH + COLUMN_GAP) - COLUMN_GAP;
    }

    positions.size_ = { width, height };
    layouts.set("current", { signature, positions });
    return positions;
  }

  const KIND_TEXT = { Service: "Service", Database: "Database", Queue: "Queue", External: "External system" };

  function renderGraph(graph) {
    const positions = layout(graph);
    const size = positions.size_;
    const maxCalls = Math.max(1, ...graph.edges.map((e) => e.calls));
    const connected = (id) => graph.edges.some((e) => (e.from === selectedNode && e.to === id) || (e.to === selectedNode && e.from === id));

    const svg = svgEl("svg", { class: "graph", viewBox: "0 0 " + size.width + " " + size.height, role: "group", "aria-label": "Dependency graph" });
    svg.appendChild(svgEl("title", {}, "Dependency graph"));
    const defs = svgEl("defs");
    const marker = svgEl("marker", { id: "arrow", viewBox: "0 0 10 10", refX: "9", refY: "5", markerWidth: "7", markerHeight: "7", orient: "auto-start-reverse" });
    marker.appendChild(svgEl("path", { d: "M0 0 L10 5 L0 10 z", class: "arrow" }));
    defs.appendChild(marker);
    svg.appendChild(defs);

    for (const edge of graph.edges) {
      const from = positions.get(edge.from);
      const to = positions.get(edge.to);
      const x1 = from.x + NODE_WIDTH;
      const y1 = from.y + NODE_HEIGHT / 2;
      const x2 = to.x;
      const y2 = to.y + NODE_HEIGHT / 2;
      const bend = Math.max(40, Math.abs(x2 - x1) / 2);
      const failing = edge.errors > 0;
      const dim = selectedNode !== null && edge.from !== selectedNode && edge.to !== selectedNode;
      const group = svgEl("g", { class: "edge" + (failing ? " failing" : "") + (dim ? " dim" : "") });
      const path = svgEl("path", {
        d: "M" + x1 + " " + y1 + " C" + (x1 + bend) + " " + y1 + " " + (x2 - bend) + " " + y2 + " " + x2 + " " + y2,
        "stroke-width": String(1.5 + 5 * Math.log10(1 + edge.calls) / Math.log10(1 + maxCalls)),
        "marker-end": "url(#arrow)",
        fill: "none",
      });
      path.appendChild(svgEl("title", {}, edgeText(edge, graph)));
      group.appendChild(path);
      if (failing) group.appendChild(svgEl("text", { x: String((x1 + x2) / 2), y: String((y1 + y2) / 2 - 6), class: "edge-label", "text-anchor": "middle" }, plural(edge.errors, "error", "errors")));
      svg.appendChild(group);
    }

    for (const node of graph.nodes) {
      const at = positions.get(node.id);
      const dim = selectedNode !== null && node.id !== selectedNode && !connected(node.id);
      const health = node.health ? " — " + (LABELS[node.health] || "Unknown") : "";
      const group = svgEl("g", {
        class: "node " + node.kind.toLowerCase() + (node.health ? " " + node.health.toLowerCase() : "") + (node.id === selectedNode ? " selected" : "") + (dim ? " dim" : ""),
        transform: "translate(" + at.x + " " + at.y + ")",
        tabindex: "0",
        role: "button",
        "aria-pressed": node.id === selectedNode ? "true" : "false",
        "aria-label": node.name + ", " + KIND_TEXT[node.kind] + health,
      });
      group.appendChild(svgEl("rect", { width: String(NODE_WIDTH), height: String(NODE_HEIGHT), rx: "6" }));
      group.appendChild(svgEl("text", { x: "10", y: "22", class: "node-name" }, fit(node.name, 24)));
      group.appendChild(svgEl("text", { x: "10", y: "40", class: "node-kind" }, KIND_TEXT[node.kind] + health));
      const select = () => { selectedNode = selectedNode === node.id ? null : node.id; render(); };
      group.addEventListener("click", select);
      group.addEventListener("keydown", (event) => {
        if (event.key === "Enter" || event.key === " ") { event.preventDefault(); select(); }
      });
      svg.appendChild(group);
    }

    return svg;
  }

  function fit(text, max) {
    return text.length <= max ? text : text.slice(0, max - 1) + "…";
  }

  function nameOf(id, graph) {
    const node = graph.nodes.find((n) => n.id === id);
    return node ? (node.scope ? node.scope + "/" + node.name : node.name) : id;
  }

  function edgeText(edge, graph) {
    const average = edge.calls > 0 ? Math.round(edge.durationSumMs / edge.calls) : 0;
    return nameOf(edge.from, graph) + " → " + nameOf(edge.to, graph) + " (" + edge.type + "): " +
      plural(edge.calls, "call", "calls") + ", " + plural(edge.errors, "error", "errors") + ", " + average + " ms on average";
  }

  function renderDependencies(graph) {
    const root = el("div");
    root.appendChild(el("h2", "", "Dependencies"));
    if (graph.nodes.length === 0) {
      root.appendChild(el("p", "muted", "No calls between services have been reported yet."));
      return root;
    }

    const frame = el("div", "graph-frame");
    frame.appendChild(renderGraph(graph));
    root.appendChild(frame);

    // The same facts as text, for screen readers and for anyone who wants the numbers.
    const table = el("table", "instances");
    const head = el("tr");
    for (const title of ["From", "To", "Type", "Calls", "Errors", "Average"]) head.appendChild(el("th", "", title));
    table.appendChild(head);
    for (const edge of graph.edges) {
      const row = el("tr");
      row.appendChild(el("td", "", nameOf(edge.from, graph)));
      row.appendChild(el("td", "", nameOf(edge.to, graph)));
      row.appendChild(el("td", "", edge.type));
      row.appendChild(el("td", "", String(edge.calls)));
      row.appendChild(el("td", "", String(edge.errors)));
      row.appendChild(el("td", "", (edge.calls > 0 ? Math.round(edge.durationSumMs / edge.calls) : 0) + " ms"));
      table.appendChild(row);
    }
    root.appendChild(table);
    return root;
  }

  let dependenciesRequest = 0;

  async function showDependencies() {
    const request = ++dependenciesRequest;
    try {
      const response = await fetch("/api/topology");
      if (request !== dependenciesRequest) return;
      show(renderDependencies(await response.json()));
    } catch (error) {
      if (request === dependenciesRequest) show(el("p", "muted", "The dependencies could not be loaded."));
    }
  }

  function routeScopeName() {
    const match = /^#\/scopes\/(.+)$/.exec(location.hash);
    return match ? decodeURIComponent(match[1]) : null;
  }

  async function render() {
    markNavigation();
    if (location.hash === "#/dependencies") {
      await showDependencies();
      return;
    }

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

  function markNavigation() {
    for (const link of document.querySelectorAll("nav a")) {
      const current = link.getAttribute("href") === "#/dependencies" ? location.hash === "#/dependencies" : location.hash !== "#/dependencies";
      if (current) link.setAttribute("aria-current", "page"); else link.removeAttribute("aria-current");
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
