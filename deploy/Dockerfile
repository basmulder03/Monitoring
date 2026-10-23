# One Dockerfile for the three executables: docker build --build-arg PROJECT=Controller -f deploy/Dockerfile .
# PROJECT is Controller, Agent or Sidecar (or SampleApp with PROJECT_PATH=samples/Monitoring.SampleApp). The result is a Native AOT binary on a minimal image that runs as a non-root user.
# On OpenShift, which starts the container with an arbitrary user of group 0, nothing needs to be written outside the volumes it is given.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
ARG PROJECT_PATH=src/Monitoring.${PROJECT}
ARG TARGETARCH
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev && rm -rf /var/lib/apt/lists/*

# The dashboard of the controller is built with Node, which the build of Monitoring.Ui calls. The image of Node is only used as a source for it.
COPY --from=node:22-bookworm-slim /usr/local/bin/node /usr/local/bin/node
COPY --from=node:22-bookworm-slim /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm

WORKDIR /src
COPY Directory.Build.props global.json Monitoring.slnx ./
COPY src ./src
COPY samples ./samples
RUN case "${TARGETARCH:-amd64}" in arm64) rid=linux-arm64 ;; *) rid=linux-x64 ;; esac \
    && dotnet publish "${PROJECT_PATH}" -c Release -r "$rid" -o /out \
    && rm -f /out/*.dbg /out/*.pdb /out/*.xml

# The runtime dependencies image has no shell, no package manager and a non-root user.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
ARG PROJECT
COPY --from=build /out/Monitoring.${PROJECT} /app/monitoring
USER $APP_UID
ENTRYPOINT ["/app/monitoring"]
