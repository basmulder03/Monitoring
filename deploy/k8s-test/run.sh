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
build Injector monitoring-injector
build SampleApp monitoring-sampleapp samples/Monitoring.SampleApp

step "Cluster"
kind delete cluster --name "$cluster" >/dev/null 2>&1 || true
kind create cluster --name "$cluster" --wait 120s >/dev/null
for image in monitoring-controller monitoring-agent monitoring-sidecar monitoring-injector monitoring-sampleapp; do
  kind load docker-image "$image:dev" --name "$cluster" >/dev/null
done

step "Controller"
kubectl create namespace monitoring >/dev/null
# The agents prove who they are with their service account, checked by the API server of the cluster: no secret is handed out.
helm install monitoring-controller "$root/deploy/helm/monitoring-controller" -n monitoring \
  --set image.repository=monitoring-controller --set image.tag=dev --set image.pullPolicy=Never \
  --set serviceAccountAuth.enabled=true \
  --set notifications.oneSender=true --set 'notifications.webhooks[0].url=http://127.0.0.1:9/nobody-listens' \
  --set state.sqlite.enabled=true >/dev/null
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

step "One controller replica sends alerts, chosen through a lease"
holder() { kubectl get lease monitoring-controller-alerts -n monitoring -o jsonpath='{.spec.holderIdentity}' 2>/dev/null; }
renewed() { kubectl get lease monitoring-controller-alerts -n monitoring -o jsonpath='{.spec.renewTime}' 2>/dev/null; }
eventually 30 holder
[ -n "$(holder)" ] || fail "no replica holds the lease for sending alerts"
case "$(holder)" in monitoring-controller-0|monitoring-controller-1) ;; *) fail "the lease is held by '$(holder)', which is not a controller replica" ;; esac
first="$(renewed)"
sleep 8
[ "$(renewed)" != "$first" ] || fail "the holder of the lease does not renew it"
kubectl exec -n monitoring monitoring-controller-0 -- true >/dev/null 2>&1 || true
echo "ok"

step "What the application says about itself reached the dashboard"
detail="$(api /api/scopes/shop)"
grep -q '"version":"1.0.0"' <<<"$detail" || fail "the version of the application is missing"
grep -q '"name":"switch"' <<<"$detail" || fail "the health check of the application is missing"
grep -q '"role":"MonitoringSidecar"' <<<"$detail" || fail "the sidecar is not shown as a container"
grep -q '"role":"App"' <<<"$detail" || fail "the application is not shown as a container"
echo "ok"

step "A pod says which Kubernetes services select it"
# The service is named differently from the application, as it often is; the agent finds it by its selector.
kubectl apply -n shop -f - >/dev/null <<'YAML'
apiVersion: v1
kind: Service
metadata: { name: orders-front }
spec:
  selector: { app: orders }
  ports: [{ port: 80 }]
YAML
selected() { api /api/scopes/shop | grep -q '"k8s.services":"orders-front"'; }
eventually 120 selected || fail "the pod of the application does not say that the service orders-front selects it"
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

step "The injector adds the sidecar to the pods of a namespace that asks, and only to those"
injector_values=(--set image.repository=monitoring-injector --set image.tag=dev --set image.pullPolicy=Never
  --set sidecar.image.repository=monitoring-sidecar --set sidecar.image.tag=dev --set sidecar.imagePullPolicy=Never
  --set sidecar.agentAddress=monitoring-agent:9441)
helm install monitoring-injector "$root/deploy/helm/monitoring-injector" -n monitoring "${injector_values[@]}" >/dev/null
kubectl rollout status deployment/monitoring-injector -n monitoring --timeout=120s >/dev/null || fail "the injector did not become ready"

# A plain application, without anything of monitoring in its manifest but the client in its code, in a namespace that has not asked yet.
plain() { # name [label]
  kubectl create deployment "$1" -n shop --image=monitoring-sampleapp:dev --dry-run=client -o json \
    | python3 -c "
import json, sys
d = json.load(sys.stdin)
pod = d['spec']['template']
pod['spec']['containers'][0]['imagePullPolicy'] = 'Never'
pod['spec']['containers'][0]['env'] = [{'name': 'SERVICE_NAME', 'value': '$1'}]
if '${2:-}':
    pod['metadata'].setdefault('labels', {})['monitoring/inject'] = '${2:-}'
json.dump(d, sys.stdout)" | kubectl apply -f - >/dev/null
}
initnames() { kubectl get pod -n shop -l "app=$1" -o jsonpath='{.items[*].spec.initContainers[*].name}'; }
plain before
kubectl rollout status deployment/before -n shop --timeout=120s >/dev/null || fail "the application did not start"
[ -z "$(initnames before)" ] || fail "a pod of a namespace that did not ask got a sidecar"

kubectl label namespace shop monitoring/inject=true >/dev/null
plain after
plain optout false
kubectl rollout status deployment/after -n shop --timeout=120s >/dev/null || fail "the application did not start after the namespace asked"
kubectl rollout status deployment/optout -n shop --timeout=120s >/dev/null || fail "the pod that opted out did not start"
[ "$(initnames after)" = "monitoring-sidecar" ] || fail "the pod of the namespace that asked did not get the sidecar (got '$(initnames after)')"
[ -z "$(initnames optout)" ] || fail "a pod that opted out got a sidecar"
shows() { api /api/scopes/shop | python3 -c "
import json, sys
d = json.load(sys.stdin)
sys.exit(0 if any(s['name'] == 'after' and any(i['health'] == 'Healthy' and any(c['role'] == 'MonitoringSidecar' for c in i['containers']) for i in s['instances']) for s in d['services']) else 1)"; }
eventually 120 shows || fail "the application that got its sidecar from the injector is not monitored"
echo "ok"

step "A pod that opts in with its own label is injected in a namespace that did not ask"
kubectl label namespace shop monitoring/inject- >/dev/null
plain labelled true
kubectl rollout status deployment/labelled -n shop --timeout=120s >/dev/null || fail "the labelled application did not start"
[ "$(initnames labelled)" = "monitoring-sidecar" ] || fail "a pod with the label did not get the sidecar (got '$(initnames labelled)')"
echo "ok"

step "A renewed certificate is picked up by the running injector, without a restart"
# An upgrade makes a new certificate and puts its authority in the registration at once. The pods of the injector are not replaced, so they must notice the new files of
# the secret, which Kubernetes brings to them after a while; until then the API server does not trust them and pods start without the sidecar, as the policy allows.
pods_before="$(kubectl get pod -n monitoring -l app.kubernetes.io/name=monitoring-injector -o jsonpath='{.items[*].metadata.name}')"
helm upgrade monitoring-injector "$root/deploy/helm/monitoring-injector" -n monitoring "${injector_values[@]}" >/dev/null
plain renewed true
renewed() {
  kubectl delete pod -n shop -l app=renewed --wait=true >/dev/null 2>&1 || true
  kubectl wait --for=condition=Ready pod -l app=renewed -n shop --timeout=60s >/dev/null 2>&1 || return 1
  [ "$(initnames renewed)" = "monitoring-sidecar" ]
}
eventually 150 renewed || fail "the injector did not start using the renewed certificate"
pods_after="$(kubectl get pod -n monitoring -l app.kubernetes.io/name=monitoring-injector -o jsonpath='{.items[*].metadata.name}')"
[ "$pods_before" = "$pods_after" ] || fail "the pods of the injector were replaced instead of picking up the certificate"
echo "ok"

step "Pods still start while the injector is down"
kubectl scale deployment/monitoring-injector -n monitoring --replicas=0 >/dev/null
kubectl wait --for=delete pod -l app.kubernetes.io/name=monitoring-injector -n monitoring --timeout=60s >/dev/null 2>&1 || true
plain late true
kubectl rollout status deployment/late -n shop --timeout=120s >/dev/null || fail "a pod could not start while the injector was down"
[ -z "$(initnames late)" ] || fail "a pod got a sidecar from an injector that is down"
echo "ok"

echo
echo "Kubernetes test passed"
