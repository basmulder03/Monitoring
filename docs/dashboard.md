<script setup>
import { withBase } from 'vitepress'
</script>

# The dashboard in motion

These recordings show the real dashboard, served by a real controller. The agents behind it are made up (`samples/Plover.Demo`), but they speak the real protocol and play the same short incident again and again.

## Overview: scopes turn degraded and recover

<video :src="withBase('/recordings/overview.webm')" controls loop muted autoplay playsinline preload="metadata" aria-describedby="overview-text"></video>

<p id="overview-text">Each scope is a tile, grouped by environment and data center, with the least healthy group first. A tile and the groups above it take the worst health of what is inside them, so one failing instance shows from the top. Every state has its own icon and word as well as a colour.</p>

## Scope: the instance that fails, and the rollout that replaces it

<video :src="withBase('/recordings/scope.webm')" controls loop muted autoplay playsinline preload="metadata" aria-describedby="scope-text"></video>

<p id="scope-text">Inside a scope, a failing instance goes from Running to Terminating to Gone while its replacement starts. The service with the problem opens by itself. A removed instance stays visible as Gone for ten minutes (the demo shortens that), so you can see what disappeared.</p>

## Alerts: acknowledge and silence

<video :src="withBase('/recordings/silence.webm')" controls loop muted autoplay playsinline preload="metadata" aria-describedby="silence-text"></video>

<p id="silence-text">An operator marks an alert as seen, so everyone can tell somebody is on it, and silences another for an hour with a reason. The silence appears under the alerts with who made it and when it ends, and can be taken back. Only a person who signed in as an operator sees these buttons. Here the demo host believes the headers of a proxy, which is how it would work behind one.</p>

## Dependencies: who calls whom

<video :src="withBase('/recordings/dependencies.webm')" controls loop muted autoplay playsinline preload="metadata" aria-describedby="dependencies-text"></video>

<p id="dependencies-text">The graph comes from the call counters in the libraries, and no traces are stored. Services, databases and queues are nodes. An edge gets thicker with more calls and turns red when calls fail. The layout only moves when the topology changes, so nodes stay put on a wall display.</p>

## Try it and record it yourself

```sh
DEMO_SIGNED_IN=1 dotnet run --project samples/Plover.Demo     # dashboard on http://127.0.0.1:5080
cd docs && npm run record                                          # writes docs/public/recordings/*.webm
```

The components and icons of the dashboard are listed in [Dashboard components and icons](reference/ui). The [design](design#dashboard-ui) explains how it is made to be read from across a room.
