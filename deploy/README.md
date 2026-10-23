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

## Helm charts

| Chart | What it installs |
| --- | --- |
| `deploy/helm/monitoring-controller` | The controller as a stateful set of identical replicas, each with an address of its own through a headless service, a service for the dashboard, and the central configuration as a config map. Tokens, aligned packages, advisories and transport security are values |
| `deploy/helm/monitoring-agent` | The agent of one namespace: a single replica, a service for the sidecars, and a role that can only read pods of its own namespace |
| `deploy/helm/monitoring-injector` | The injector: a webhook that adds the sidecar to the pods of the namespaces and pods that carry the label `monitoring/inject=true`, for applications whose chart you do not own. It needs a cluster administrator, because the webhook registration is cluster-wide. See [the injector](../docs/configuration.md#the-injector) |
| `deploy/helm/monitoring-sidecar` | A library chart: the sidecar for the chart of any application, as a native sidecar (Kubernetes 1.29 and later, OpenShift 4.16 and later) or, with `nativeSidecar: false`, as a second container |

```sh
# The controller. Two replicas; the hash of an enrollment token goes in, never the token.
helm install monitoring-controller deploy/helm/monitoring-controller \
  --set 'agentTokens[0].scope=*' --set "agentTokens[0].tokenHash=$(printf '%s' "$TOKEN" | sha256sum | cut -d' ' -f1)"

# The agent, in each namespace that is to be monitored. The token comes from a secret you create; or, with serviceAccountAuth.enabled on the controller,
# use --set auth.mode=serviceAccount instead of the secret and the token, and nothing is handed out.
kubectl create secret generic agent-token --from-literal=token="$TOKEN"
helm install monitoring-agent deploy/helm/monitoring-agent \
  --set 'controller.addresses={monitoring-controller.monitoring.svc:9440}' --set token.existingSecret=agent-token \
  --set scope.labels.environment=prod
```

An application chart gets monitored by depending on `monitoring-sidecar` and including its pieces once in the pod template; `deploy/helm/test/sample-app` shows how, and the comment at the top of `_sidecar.tpl` lists the values. The application also needs the monitoring client in its code.

- Agents inside the cluster reach the replicas through the addresses of the stateful set. For agents in other clusters, give one address per replica in `replicaAddresses`, for example the host of a route per replica; that needs `tls` or `mtls`.
- `deploy/helm/test.sh` lints and renders the charts and checks what they must contain; CI runs it. It uses Helm when installed and Docker otherwise.

On OpenShift, see [docs/openshift.md](../docs/openshift.md): the security context, Routes and network policies. 

For Windows servers, see [docs/windows.md](../docs/windows.md); `deploy/windows` holds the installation script, a script that tests it, and an Ansible playbook.
