"""Writes the animated SVGs in public/animations from the lists of steps below: python3 docs/scripts/animations.py

Each step is one message (or a few) travelling between two boxes, a caption, and optional marks that cover a box with a state. The loop is the steps one after
another. Plain SVG with SMIL, so it needs no script and works inside an <img>; edit a list and run this again.
"""
import os
SLOT = 2.2

def svg(path, title, nodes, steps, width=760, height=270):
    """nodes: name -> (cx, cy). steps: list of dict(caption, msgs=[(label, from, to, color)], marks=[(node, text, color)])."""
    n = len(steps); T = n * SLOT
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}" role="img" aria-label="{title}" font-family="system-ui, sans-serif" font-size="14">',
           f'<title>{title}</title>']
    # connections between every pair used by a message
    pairs = {tuple(sorted((a, b))) for st in steps for (_, a, b, _) in st.get('msgs', [])}
    for a, b in pairs:
        (x1, y1), (x2, y2) = nodes[a], nodes[b]
        out.append(f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="#8b949e" stroke-width="2" stroke-dasharray="4 4"/>')
    for name, (x, y) in nodes.items():
        out.append(f'<rect x="{x-62}" y="{y-24}" width="124" height="48" rx="8" fill="#e7eefc" stroke="#4c6ef5" stroke-width="2"/>')
        out.append(f'<text x="{x}" y="{y+5}" text-anchor="middle" fill="#1b1f24" font-weight="600">{name}</text>')

    def window(i):
        a = i * SLOT / T; b = (i + 1) * SLOT / T
        return a, b

    for i, st in enumerate(steps):
        a, b = window(i)
        # marks (state overlays) shown during the step
        for node, text, color in st.get('marks', []):
            x, y = nodes[node]
            out.append(f'<g opacity="0"><rect x="{x-62}" y="{y-24}" width="124" height="48" rx="8" fill="{color}" opacity=".88"/>'
                       f'<text x="{x}" y="{y+5}" text-anchor="middle" fill="#fff" font-weight="600">{text}</text>'
                       f'<animate attributeName="opacity" values="0;0;1;1;0;0" keyTimes="0;{a:.4f};{a+.01:.4f};{b-.01:.4f};{b:.4f};1" dur="{T}s" repeatCount="indefinite"/></g>')
        # caption
        out.append(f'<text x="{width/2}" y="{height-22}" text-anchor="middle" fill="#6e7681" opacity="0">{i+1}. {st["caption"]}'
                   f'<animate attributeName="opacity" values="0;0;1;1;0;0" keyTimes="0;{a:.4f};{a+.02:.4f};{b-.02:.4f};{b:.4f};1" dur="{T}s" repeatCount="indefinite"/></text>')
        for label, src, dst, color in st.get('msgs', []):
            (x1, y1), (x2, y2) = nodes[src], nodes[dst]
            # travel during the middle 70% of the step
            s = a + (b - a) * .1; e = a + (b - a) * .8
            kt = f'0;{s:.4f};{e:.4f};1'
            out.append(f'<g opacity="0"><circle r="8" fill="{color}"><animate attributeName="cx" values="{x1};{x1};{x2};{x2}" keyTimes="{kt}" dur="{T}s" repeatCount="indefinite"/>'
                       f'<animate attributeName="cy" values="{y1};{y1};{y2};{y2}" keyTimes="{kt}" dur="{T}s" repeatCount="indefinite"/></circle>'
                       f'<text text-anchor="middle" fill="{color}" font-weight="700"><animate attributeName="x" values="{x1};{x1};{x2};{x2}" keyTimes="{kt}" dur="{T}s" repeatCount="indefinite"/>'
                       f'<animate attributeName="y" values="{y1-16};{y1-16};{y2-16};{y2-16}" keyTimes="{kt}" dur="{T}s" repeatCount="indefinite"/>{label}</text>'
                       f'<animate attributeName="opacity" values="0;0;1;1;0;0" keyTimes="0;{a:.4f};{s:.4f};{e+.02:.4f};{b:.4f};1" dur="{T}s" repeatCount="indefinite"/></g>')
    out.append('</svg>')
    open(path, 'w').write('\n'.join(out) + '\n')

D = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'public', 'animations') + os.sep
BLUE, GREEN, ORANGE, PURPLE = '#1c7ed6', '#2f9e44', '#e8590c', '#9c36b5'
svg(D + 'registration.svg', 'A sidecar registers, the agent collects and the controller pushes to the dashboard',
    {'Sidecar': (90, 110), 'Agent': (275, 110), 'Controller': (460, 110), 'Browser': (645, 110)},
    [dict(caption='The sidecar says hello: instance, service, version', msgs=[('Hello', 'Sidecar', 'Agent', BLUE)]),
     dict(caption='The agent answers with the heartbeat interval', msgs=[('Config', 'Agent', 'Sidecar', GREEN)]),
     dict(caption='The agent asks for the health checks', msgs=[('Pull health', 'Agent', 'Sidecar', ORANGE)]),
     dict(caption='The sidecar answers; the instance is now Running', msgs=[('Health', 'Sidecar', 'Agent', BLUE)]),
     dict(caption='Heartbeats follow every 5 s; a changed health triggers a new pull', msgs=[('Heartbeat', 'Sidecar', 'Agent', PURPLE)]),
     dict(caption='The agent sends only what changed', msgs=[('Delta', 'Agent', 'Controller', GREEN)]),
     dict(caption='The controller pushes the new overview to every open page', msgs=[('Overview', 'Controller', 'Browser', BLUE)])],
    height=210)
svg(D + 'recovery.svg', 'A controller replica restarts and is complete again from one snapshot',
    {'Agent': (110, 120), 'Replica 1': (600, 60), 'Replica 2': (600, 190)},
    [dict(caption='Every agent feeds all replicas', msgs=[('Delta', 'Agent', 'Replica 1', GREEN), ('Delta', 'Agent', 'Replica 2', GREEN)]),
     dict(caption='Replica 1 restarts and loses its state; replica 2 keeps serving', msgs=[('Delta', 'Agent', 'Replica 2', GREEN)], marks=[('Replica 1', 'restarting', '#c92a2a')]),
     dict(caption='The agent reconnects, with backoff and jitter', msgs=[('Hello', 'Agent', 'Replica 1', BLUE)], marks=[('Replica 1', 'empty', '#c92a2a')]),
     dict(caption='The replica answers with the configuration', msgs=[('Config', 'Replica 1', 'Agent', GREEN)], marks=[('Replica 1', 'empty', '#c92a2a')]),
     dict(caption='The agent sends its whole scope', msgs=[('Snapshot', 'Agent', 'Replica 1', ORANGE)], marks=[('Replica 1', 'filling', '#e67700')]),
     dict(caption='The replica acknowledges; it is complete again without anyone acting', msgs=[('Ack', 'Replica 1', 'Agent', PURPLE)])],
    height=270)

svg(D + 'edges.svg', 'Call counters travel from the library to the agent and become edges in the controller',
    {'Library': (80, 110), 'Sidecar': (270, 110), 'Agent': (460, 110), 'Controller': (650, 110)},
    [dict(caption='Every finished client call updates a counter: calls, errors, latency', marks=[('Library', 'counting', '#2f9e44')]),
     dict(caption='The agent asks for the metrics, edges included, on a timer', msgs=[('Pull', 'Agent', 'Sidecar', ORANGE)]),
     dict(caption='The sidecar scrapes them from the library', msgs=[('Scrape', 'Sidecar', 'Library', ORANGE)]),
     dict(caption='The counters travel back; nothing was sampled', msgs=[('Edges', 'Library', 'Sidecar', BLUE), ('Edges', 'Sidecar', 'Agent', BLUE)]),
     dict(caption='The agent sends them on as edges, never as spans', msgs=[('Edges', 'Agent', 'Controller', GREEN)]),
     dict(caption='The controller resolves each callee with its registry; unknown ones become external nodes', marks=[('Controller', 'resolving', '#e67700')])],
    height=210)
svg(D + 'alert.svg', 'An instance turns unhealthy, an alert fires after its wait, and resolves when the instance recovers',
    {'Agent': (110, 120), 'Controller': (380, 120), 'Dashboard': (650, 50), 'Channels': (650, 190)},
    [dict(caption='An instance turns unhealthy: the agent sends the change', msgs=[('Delta', 'Agent', 'Controller', ORANGE)]),
     dict(caption='The rule sees it, but waits (30 s by default) so a blip does not alert', marks=[('Controller', 'waiting', '#e67700')]),
     dict(caption='Still unhealthy: the alert fires, with a stable key', msgs=[('Alert', 'Controller', 'Dashboard', '#c92a2a'), ('Notify', 'Controller', 'Channels', '#c92a2a')], marks=[('Controller', 'firing', '#c92a2a')]),
     dict(caption='The instance recovers: another delta', msgs=[('Delta', 'Agent', 'Controller', GREEN)]),
     dict(caption='The alert resolves, in the panel and in the channels', msgs=[('Resolved', 'Controller', 'Dashboard', GREEN), ('Resolved', 'Controller', 'Channels', GREEN)])],
    height=270)
