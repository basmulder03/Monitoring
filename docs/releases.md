# Releases

Nothing has been published yet. There are no packages on NuGet, no images in a registry and no charts in an OCI registry; what exists is the source, and the pipelines that prove it builds, publishes with Native AOT and passes its tests (see [Testing](testing)). Publishing is a deliberate step the owner takes: pushing a version tag runs `.github/workflows/release.yml` (see [How to release](#how-to-release)). It is tracked in [issue 56](https://github.com/basmulder03/Plover/issues/56).

## What a release will contain

| Artifact | Form |
| --- | --- |
| Client library | NuGet packages `Plover.Client` and `Plover.Protocol` |
| Sidecar, agent, controller, injector | Container images on `ghcr.io/basmulder03/plover-<name>`, Native AOT, amd64, one per component |
| Helm charts | OCI artifacts under `oci://ghcr.io/basmulder03/charts`: controller, agent, sidecar library chart, injector |
| Windows agent | A zip with the agent and `install-agent.ps1`, attached to the GitHub release |

## How versions work

Semantic versioning. The protocol has its own version, carried in `Hello`: a peer one version away is served, so a rollout can run old and new components side by side. A change that cannot be served that way is a major version of the protocol and is called out in the changelog.

## Before the first release

- The name, the licence (Apache-2.0) and the package names are checked once more.
- The speed and size budgets are green over a run of nights (see [Speed budgets](performance)).
- The owner agrees to publish.

## How to release

1. Put the changes of the version under a `## [x.y.z]` heading in `CHANGELOG.md`. Its text becomes the release notes, and the release fails without it.
2. Tag a commit of `main` whose CI is green and push the tag: `git tag v1.0.0 && git push origin v1.0.0`. A tag with a dash, such as `v0.2.0-rc.1`, is published as a prerelease and does not move the `latest` image tag.
3. The workflow creates the GitHub release and then, in parallel, packs the two NuGet packages, builds and pushes the four images, packages and pushes the four charts, and zips the Windows agent. The charts and packages take the version of the tag, and the charts default to the image of that version.

The workflow does not run the tests again, which is why the tag goes on a green commit.

### NuGet, once

The NuGet job uses trusted publishing, so no API key is stored. On nuget.org, under your account's *Trusted Publishing*, add a policy for owner `basmulder03`, repository `Plover` and workflow file `release.yml`. Then set the repository variable `NUGET_USER` to your nuget.org profile name. A policy belongs to one repository and one workflow file, so one made for another project does not cover this one. Until `NUGET_USER` is set, the packages are attached to the GitHub release and not pushed. The first push of a new package id may need the id to be reserved or the policy to be created while the package is new; nuget.org's page explains the current rules.
