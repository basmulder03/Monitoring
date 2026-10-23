#!/usr/bin/env bash
# Runs the real thing on a throwaway Kubernetes cluster (kind): the images, the charts, a sample application with the sidecar, and checks
# what the dashboard API of the controller says. Needs docker, kind, kubectl and helm on the PATH.
#
#   deploy/k8s-test/run.sh          builds the images, creates the cluster, tests, deletes the cluster
#   KEEP=1 deploy/k8s-test/run.sh   leaves the cluster for a look
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
cluster="monitoring-test"
web_forward=18080
pids=()

step() { echo; echo "== $*"; }
fail() { echo "FAIL: $*" >&2; diagnose; exit 1; }

diagnose() {
  echo "--- pods"; kubectl get pods -A 2>&1 | head -30
  for pod in monitoring-controller-0 monitoring-agent; do
    echo "--- logs of $pod"; kubectl logs -n monitoring "$pod" --tail=30 2>&1 | head -40 || true
  done
  kubectl logs -n shop deploy/monitoring-agent --tail=40 2>&1 | head -50 || true
}

cleanup() {
  for pid in "${pids[@]:-}"; do [ -n "$pid" ] && kill "$pid" 2>/dev/null || true; done
  if [ "${KEEP:-0}" != 1 ]; then kind delete cluster --name "$cluster" >/dev/null 2>&1 || true; fi
}
trap cleanup EXIT

# Asks the dashboard API through a port-forward; the argument is the path.
api() { curl -fsS "http://127.0.0.1:$web_forward$1"; }

# Waits until a command succeeds, for at most the given seconds.
eventually() {
  local seconds="$1"; shift
  for _ in $(seq 1 "$seconds"); do
    if "$@" >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  return 1
}

step "Images"
build() { docker build -q --build-arg PROJECT="$1" ${3:+--build-arg PROJECT_PATH="$3"} -t "$2:dev" -f "$root/deploy/Dockerfile" "$root" >/dev/null; }
build Controller monitoring-controller
build Agent monitoring-agent
build Sidecar monitoring-sidecar
build SampleApp monitoring-sampleapp samples/Monitoring.SampleApp

step "Cluster"
kind delete cluster --name "$cluster" >/dev/null 2>&1 || true
kind create cluster --name "$cluster" --wait 120s >/dev/null
for image in monitoring-controller monitoring-agent monitoring-sidecar monitoring-sampleapp; do
  kind load docker-image "$image:dev" --name "$cluster" >/dev/null
done

step "Controller"
kubectl create namespace monitoring >/dev/null
# The agents prove who they are with their service account, checked by the API server of the cluster: no secret is handed out.
helm install monitoring-controller "$root/deploy/helm/monitoring-controller" -n monitoring \
  --set image.repository=monitoring-controller --set image.tag=dev --set image.pullPolicy=Never \
  --set serviceAccountAuth.enabled=true >/dev/null
kubectl rollout status statefulset/monitoring-controller -n monitoring --timeout=180s >/dev/null \
  || fail "the controller did not become ready"

step "Agent and the sample application"
kubectl create namespace shop >/dev/null
helm install monitoring-agent "$root/deploy/helm/monitoring-agent" -n shop \
  --set image.repository=monitoring-agent --set image.tag=dev --set image.pullPolicy=Never \
  --set 'controller.addresses={monitoring-controller.monitoring.svc:9440}' --set auth.mode=serviceAccount \
  --set scope.labels.environment=test --set scope.labels.datacenter=kind >/dev/null
( cd "$root/deploy/helm/test/sample-app" && helm dependency update >/dev/null )
helm install sample "$root/deploy/helm/test/sample-app" -n shop \
  --set image.repository=monitoring-sampleapp --set image.tag=dev \
  --set monitoring.image.repository=monitoring-sidecar --set monitoring.image.tag=dev --set monitoring.image.pullPolicy=Never \
  --set monitoring.agentAddress=monitoring-agent:9441 >/dev/null
kubectl patch deployment orders -n shop --type=json \
  -p '[{"op":"add","path":"/spec/template/spec/containers/0/imagePullPolicy","value":"Never"}]' >/dev/null
kubectl rollout status deployment/orders -n shop --timeout=180s >/dev/null || fail "the sample application did not become ready"

kubectl port-forward -n monitoring pod/monitoring-controller-0 "$web_forward:8080" >/dev/null 2>&1 &
pids+=($!)
eventually 30 api /api/overview || fail "the dashboard of the controller cannot be reached"

step "An agent with a token the API server does not know is turned away"
# A token of the right shape (three parts) that nobody issued: the controller asks the API server, which says no.
kubectl run intruder -n shop --image=monitoring-agent:dev --image-pull-policy=Never --restart=Never \
  --env=MONITORING_Agent__ScopeName=intruder --env=MONITORING_Agent__Token=aaa.bbb.ccc \
  --env=MONITORING_Agent__Controllers=monitoring-controller.monitoring.svc:9440 >/dev/null
refused() { kubectl logs -n monitoring monitoring-controller-0 | grep -q "authentication failed"; }
eventually 60 refused || fail "the controller did not turn the intruder away"
api /api/overview | grep -q '"name":"intruder"' && fail "an agent with a made-up token got in"
kubectl delete pod intruder -n shop --wait=false >/dev/null
echo "ok"

step "The scope and its instance show up, healthy"
scope_ok() { api /api/scopes/shop | grep -q '"health":"Healthy"' && api /api/scopes/shop | grep -q '"name":"sample"'; }
eventually 90 scope_ok || fail "the scope 'shop' did not show a healthy service 'sample'"
api /api/overview | grep -q '"environment":"test"' || fail "the labels of the scope did not arrive"
echo "ok"

step "What the application says about itself reached the dashboard"
detail="$(api /api/scopes/shop)"
grep -q '"version":"1.0.0"' <<<"$detail" || fail "the version of the application is missing"
grep -q '"name":"switch"' <<<"$detail" || fail "the health check of the application is missing"
grep -q '"role":"MonitoringSidecar"' <<<"$detail" || fail "the sidecar is not shown as a container"
grep -q '"role":"App"' <<<"$detail" || fail "the application is not shown as a container"
echo "ok"

step "The packages of the application are known"
packages_ok() { api /api/packages | grep -q '"name": "Polly.Core"'; }
eventually 60 packages_ok || fail "the packages of the application did not arrive"
api /api/sbom | grep -q '"bomFormat": "CycloneDX"' || fail "the bill of materials is missing"
echo "ok"

step "A replaced pod is replaced at once, without waiting for a timeout"
old="$(kubectl get pod -n shop -l app=orders -o jsonpath='{.items[0].metadata.name}')"
kubectl delete pod "$old" -n shop --wait=false >/dev/null
replaced() { ! api /api/scopes/shop | grep -q "\"id\":\"$old\"\\([^}]*\\)\"lifecycle\":\"Running\"" && api /api/scopes/shop | grep -q '"name":"sample"'; }
kubectl rollout status deployment/orders -n shop --timeout=120s >/dev/null
eventually 60 replaced || fail "the old pod is still shown as running"
echo "ok"

step "A restarted controller replica is complete again without any action"
kubectl delete pod monitoring-controller-1 -n monitoring --wait=true >/dev/null
kubectl rollout status statefulset/monitoring-controller -n monitoring --timeout=180s >/dev/null
kill "${pids[0]}" 2>/dev/null || true
kubectl port-forward -n monitoring pod/monitoring-controller-1 "$web_forward:8080" >/dev/null 2>&1 &
pids=($!)
back() { api /api/scopes/shop | grep -q '"name":"sample"'; }
eventually 90 back || fail "the restarted replica did not get the state back from the agent"
echo "ok"

step "A rollout that is stuck shows on the pods of its deployment"
# The new pod can never start, so the old one keeps serving and the deployment is short of updated pods.
kubectl patch deployment orders -n shop --type=json \
  -p '[{"op":"replace","path":"/spec/template/spec/containers/0/image","value":"does-not-exist:1"}]' >/dev/null
rolling() { api /api/scopes/shop | grep -q 'Rolling out: 2 replicas exist for 1 wanted'; }
eventually 150 rolling || fail "the stuck rollout did not show on the pod that still serves"
kubectl rollout undo deployment/orders -n shop >/dev/null
kubectl rollout status deployment/orders -n shop --timeout=120s >/dev/null || fail "the rollout could not be undone"
echo "ok"

echo
echo "Kubernetes test passed"
