#!/usr/bin/env bash
# The system on a real Kubernetes (kind) for a live demo: the images built from this checkout, a controller with two replicas, an agent and the injector, and three applications
# that get their sidecar from the injector. Then it keeps the dashboard of the controller on http://127.0.0.1:18080 until it is stopped.
# Started by the app host (dotnet run --project tests/Plover.AppHost -- --Kind=true); also runs on its own. Running it again reuses the cluster.
#
#   SKIP_BUILD=1   use the images plover-*:dev that are there
#   WINDOWS=1      also let a Windows machine report in: a static agent token (WINDOWS_TOKEN) and the agent port of the controller on a node port, for the VM of --Windows=true
#   deploy/demo/kind-down.sh   deletes the cluster
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cluster=plover-demo
export KUBECONFIG="${KUBECONFIG_DEMO:-$HOME/.kube/$cluster.config}"
step() { echo; echo "== $*"; }

if [ "${SKIP_BUILD:-0}" != 1 ]; then
  step "Images (the first build takes several minutes)"
  build() { docker build -q --build-arg PROJECT="$1" ${3:+--build-arg PROJECT_PATH="$3"} -t "$2:dev" -f "$root/deploy/Dockerfile" "$root" >/dev/null; }
  build Controller plover-controller; build Agent plover-agent; build Sidecar plover-sidecar; build Injector plover-injector
  build SampleApp plover-sampleapp samples/Plover.SampleApp
fi

step "Cluster"
kind get clusters | grep -qx "$cluster" || kind create cluster --name "$cluster" --kubeconfig "$KUBECONFIG" --wait 120s
kind export kubeconfig --name "$cluster" --kubeconfig "$KUBECONFIG" >/dev/null
for image in plover-controller plover-agent plover-sidecar plover-injector plover-sampleapp; do kind load docker-image "$image:dev" --name "$cluster" >/dev/null; done

step "Controller, agent and injector"
token_args=()
if [ "${WINDOWS:-0}" = 1 ]; then
  # An agent outside the cluster is told to connect to every replica by its own address: the node of the cluster, one node port per replica. The VM sits on the docker network of kind.
  node="$(docker inspect "$cluster-control-plane" -f '{{(index .NetworkSettings.Networks "kind").IPAddress}}')"
  token_args=(--set 'agentTokens[0].scope=*' --set "agentTokens[0].tokenHash=$(printf '%s' "$WINDOWS_TOKEN" | sha256sum | cut -d' ' -f1)"
    --set "replicaAddresses[0]=$node:30440" --set "replicaAddresses[1]=$node:30441")
fi
kubectl create namespace plover --dry-run=client -o yaml | kubectl apply -f - >/dev/null
kubectl create namespace shop --dry-run=client -o yaml | kubectl apply -f - >/dev/null
local_image() { echo --set image.repository="$1" --set image.tag=dev --set image.pullPolicy=Never; }
# shellcheck disable=SC2046
helm upgrade --install plover-controller "$root/deploy/helm/plover-controller" -n plover $(local_image plover-controller) \
  --set serviceAccountAuth.enabled=true --set notifications.oneSender=true --set 'notifications.webhooks[0].url=http://127.0.0.1:9/nobody-listens' \
  --set state.sqlite.enabled=true "${token_args[@]}" >/dev/null
kubectl rollout status statefulset/plover-controller -n plover --timeout=180s >/dev/null
# shellcheck disable=SC2046
helm upgrade --install plover-agent "$root/deploy/helm/plover-agent" -n shop $(local_image plover-agent) \
  --set 'controller.addresses={plover-controller.plover.svc:9440}' --set auth.mode=serviceAccount \
  --set scope.labels.environment=demo --set scope.labels.datacenter=kind >/dev/null
helm upgrade --install plover-injector "$root/deploy/helm/plover-injector" -n plover $(local_image plover-injector) \
  --set sidecar.image.repository=plover-sidecar --set sidecar.image.tag=dev --set sidecar.imagePullPolicy=Never --set sidecar.agentAddress=plover-agent:9441 >/dev/null
kubectl rollout status deployment/plover-injector -n plover --timeout=120s >/dev/null
kubectl label namespace shop plover/inject=true --overwrite >/dev/null

step "Applications"
for app in orders payments catalog; do
  kubectl apply -n shop -f - >/dev/null <<YAML
apiVersion: apps/v1
kind: Deployment
metadata: { name: $app }
spec:
  replicas: 1
  selector: { matchLabels: { app: $app } }
  template:
    metadata: { labels: { app: $app } }
    spec:
      containers:
        - name: $app
          image: plover-sampleapp:dev
          imagePullPolicy: Never
          env:
            - { name: SERVICE_NAME, value: $app }
            - { name: DEMO_CONTROLS, value: "1" }
            - { name: ASPNETCORE_URLS, value: "http://+:8080" }
YAML
  kubectl rollout status deployment/$app -n shop --timeout=180s >/dev/null
  # The webhook may not be trusted yet when the first pod starts; a pod without its sidecar is replaced until it has one.
  for _ in $(seq 1 20); do
    kubectl get pod -n shop -l "app=$app" -o jsonpath='{.items[*].spec.initContainers[*].name}' | grep -q plover-sidecar && break
    kubectl rollout restart deployment/$app -n shop >/dev/null
    kubectl rollout status deployment/$app -n shop --timeout=180s >/dev/null
    sleep 3
  done
done

if [ "${WINDOWS:-0}" = 1 ]; then
  for replica in 0 1; do
    kubectl apply -f - >/dev/null <<YAML
apiVersion: v1
kind: Service
metadata: { name: plover-controller-demo-$replica, namespace: plover }
spec:
  type: NodePort
  selector: { statefulset.kubernetes.io/pod-name: plover-controller-$replica }
  ports: [{ port: 9440, targetPort: agents, nodePort: 3044$replica }]
YAML
  done
fi

step "Dashboard on http://127.0.0.1:18080 (kubeconfig: $KUBECONFIG)"
# A pod that is killed ends the forward; the next one goes to whichever replica is up.
while true; do kubectl port-forward -n plover svc/plover-controller 18080:8080 || true; sleep 1; done
