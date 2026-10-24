# Releases

Nothing has been published yet. There are no packages on NuGet, no images in a registry and no charts in an OCI registry; what exists is the source, and the pipelines that prove it builds, publishes with Native AOT and passes its tests (see [Testing](testing)). Publishing is a deliberate step the owner takes, tracked in [issue 56](https://github.com/basmulder03/Monitoring/issues/56).

## What a release will contain

| Artifact | Form |
| --- | --- |
| Client library and extensions | NuGet packages (`Monitoring.Client`, `Monitoring.Protocol`, `Monitoring.Agent` and the others) |
| Sidecar, agent, controller, injector | Container images, Native AOT, one per component |
| Helm charts | OCI artifacts: controller, agent, sidecar library chart, injector |
| Windows agent | A zip or an installer for the service |

## How versions work

Semantic versioning. The protocol has its own version, carried in `Hello`: a peer one version away is served, so a rollout can run old and new components side by side. A change that cannot be served that way is a major version of the protocol and is called out in the changelog.

## Before the first release

- The name, the licence (Apache-2.0) and the package names are checked once more.
- The speed and size budgets are green over a run of nights (see [Speed budgets](performance)).
- The owner agrees to publish.
