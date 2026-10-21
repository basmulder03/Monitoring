# Deployment

## Container images

One Dockerfile builds all three executables as Native AOT binaries on a minimal, non-root image (28 to 41 MB):

```sh
docker build --build-arg PROJECT=Controller -t monitoring-controller -f deploy/Dockerfile .
docker build --build-arg PROJECT=Agent      -t monitoring-agent      -f deploy/Dockerfile .
docker build --build-arg PROJECT=Sidecar    -t monitoring-sidecar    -f deploy/Dockerfile .
```

- The runtime image has no shell and no package manager. It runs as user 1654; on OpenShift an arbitrary user of group 0 works too, because nothing is written outside the volumes the container is given.
- Settings come from environment variables starting with `MONITORING_` or a mounted file; see [docs/configuration.md](../docs/configuration.md). The controller listens on 9440 (agents) and 8080 (dashboard), the agent on 9441 (sidecars).
- `--platform linux/arm64` builds an arm64 image.
- The build compiles for the architecture of the machine it runs on; the controller image also builds the dashboard, which needs no setup on your side.

Helm charts and the sidecar injector are tracked as issues and are not here yet.
