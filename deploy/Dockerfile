# One Dockerfile for the executables: docker build --build-arg PROJECT=Controller -f deploy/Dockerfile .
# PROJECT is Controller, Agent, Sidecar or Injector (or SampleApp with PROJECT_PATH=samples/Plover.SampleApp). The result is a Native AOT binary on a minimal image that runs as a non-root user.
# On OpenShift, which starts the container with an arbitrary user of group 0, nothing needs to be written outside the volumes it is given.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
ARG PROJECT_PATH=src/Plover.${PROJECT}
ARG TARGETARCH
ARG PUBLISH_ARGS=""
RUN apt-get update && apt-get install -y --no-install-recommends clang zlib1g-dev && rm -rf /var/lib/apt/lists/*

# The dashboard of the controller is built with Node, which the build of Plover.Ui calls. The image of Node is only used as a source for it.
COPY --from=node:24-bookworm-slim /usr/local/bin/node /usr/local/bin/node
COPY --from=node:24-bookworm-slim /usr/local/lib/node_modules /usr/local/lib/node_modules
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm

WORKDIR /src
COPY Directory.Build.props global.json Plover.slnx ./
COPY src ./src
COPY samples ./samples
RUN case "${TARGETARCH:-amd64}" in arm64) rid=linux-arm64 ;; *) rid=linux-x64 ;; esac \
    && dotnet publish "${PROJECT_PATH}" -c Release -r "$rid" -o /out ${PUBLISH_ARGS} \
    && rm -f /out/*.dbg /out/*.pdb /out/*.xml \
    && mkdir /native && (cp /out/*.so /native/ 2>/dev/null || true)

# The runtime dependencies image has no shell, no package manager and a non-root user.
FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled
ARG PROJECT
COPY --from=build /out/Plover.${PROJECT} /app/plover
# Native libraries that a binary loads by name, such as SQLite of the controller; empty for the others.
COPY --from=build /native/ /app/
USER $APP_UID
ENTRYPOINT ["/app/plover"]
