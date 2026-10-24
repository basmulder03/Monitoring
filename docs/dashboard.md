<script setup>
import { withBase } from 'vitepress'
</script>

# The dashboard in motion

These recordings are of the real dashboard, served by a real controller. The agents behind it are made up (`samples/Monitoring.Demo`): they speak the real protocol and play the same short incident again and again, so what you see is what the controller and the page do with it. The text under each recording says what to watch for.

## Overview: scopes turn degraded and recover

<video :src="withBase('/recordings/overview.webm')" controls loop muted autoplay playsinline preload="metadata" aria-describedby="overview-text"></video>

<p id="overview-text">Each scope is a tile, grouped by environment and data center, with the least healthy group first. A tile and the groups above it take the worst health of what is inside them, so one failing instance is visible from the top. Health is never shown by colour alone: every state has its own icon and word.</p>

## Scope: the instance that fails, and the rollout that replaces it

<video :src="withBase('/recordings/scope.webm')" controls loop muted autoplay playsinline preload="metadata" aria-describedby="scope-text"></video>

<p id="scope-text">Inside a scope an instance goes from Running to Terminating to Gone while its replacement starts. The service that has a problem opens by itself. A removed instance stays visible as Gone for a while (10 minutes by default; the demo shortens it), so it is clear what disappeared.</p>

## Dependencies: who calls whom

<video :src="withBase('/recordings/dependencies.webm')" controls loop muted autoplay playsinline preload="metadata" aria-describedby="dependencies-text"></video>

<p id="dependencies-text">The graph is built from the call counters of the libraries, not from stored traces. Services, databases and queues are nodes; the thickness of an edge is the call volume and an edge with errors turns red. The layout only moves when the topology changes, so nodes stay put on a wall display.</p>

## Try it and record it yourself

```sh
dotnet run --project samples/Monitoring.Demo     # dashboard on http://127.0.0.1:5080
cd docs && npm run record                         # writes docs/public/recordings/*.webm
```

The components and icons the dashboard is made of are listed in [Dashboard components and icons](reference/ui); how it is built to be read from across a room is in the [design](design#dashboard-ui).
