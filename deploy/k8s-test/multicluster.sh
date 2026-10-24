#!/usr/bin/env bash
# Two clusters and one controller: the controller (two replicas) and an agent in cluster A, and agents with a sample application in both clusters, the one in B
# reaching the controller across clusters over tls with a token. Then the failover of the agents is tested on both: a crashed holder, and a rollout.
# Needs docker, kind, kubectl, helm, openssl and curl. Both clusters share the docker network of kind, so the nodes of A are reachable from the pods of B.
#
#   deploy/k8s-test/multicluster.sh              builds the images, creates both clusters, tests, deletes them
#   KEEP=1 deploy/k8s-test/multicluster.sh       leaves the clusters for a look
#   SKIP_BUILD=1 deploy/k8s-test/multicluster.sh uses the images monitoring-*:dev that are there
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
a=monitoring-a
b=monitoring-b
web_forward=18081
work="$(mktemp -d)"
pids=()
export LC_ALL=C

step() { echo; echo "== $*"; }
fail() { echo "FAIL: $*" >&2; diagnose; exit 1; }
ka() { kubectl --context "kind-$a" "$@"; }
kb() { kubectl --context "kind-$b" "$@"; }

diagnose() {
  for ctx in "$a" "$b"; do
    echo "--- pods in $ctx"; kubectl --context "kind-$ctx" get pods -A 2>&1 | head -30
  done
  ka logs -n monitoring monitoring-controller-0 --tail=30 2>&1 | head -40 || true
  kb logs -n shop-b -l app.kubernetes.io/name=monitoring-agent --tail=30 --prefix 2>&1 | head -60 || true
}

cleanup() {
  for pid in "${pids[@]:-}"; do [ -n "$pid" ] && kill "$pid" 2>/dev/null || true; done
  if [ "${KEEP:-0}" != 1 ]; then
    kind delete cluster --name "$a" >/dev/null 2>&1 || true
    kind delete cluster --name "$b" >/dev/null 2>&1 || true
  fi
  rm -rf "$work"
}
trap cleanup EXIT

api() { curl -fsS "http://127.0.0.1:$web_forward$1"; }
eventually() {
  local seconds="$1"; shift
  for _ in $(seq 1 "$seconds"); do
    if "$@" >/dev/null 2>&1; then return 0; fi
    sleep 1
  done
  return 1
}
now() { date +%s; }

if [ "${SKIP_BUILD:-0}" != 1 ]; then
  step "Images"
  build() { docker build -q --build-arg PROJECT="$1" ${3:+--build-arg PROJECT_PATH="$3"} -t "$2:dev" -f "$root/deploy/Dockerfile" "$root" >/dev/null; }
  build Controller monitoring-controller
  build Agent monitoring-agent
  build Sidecar monitoring-sidecar
  build SampleApp monitoring-sampleapp samples/Monitoring.SampleApp
fi

step "Two clusters"
for name in "$a" "$b"; do
  kind delete cluster --name "$name" >/dev/null 2>&1 || true
  kind create cluster --name "$name" --wait 120s >/dev/null
  for image in monitoring-controller monitoring-agent monitoring-sidecar monitoring-sampleapp; do
    kind load docker-image "$image:dev" --name "$name" >/dev/null
  done
done
node_a="$(docker inspect -f '{{(index .NetworkSettings.Networks "kind").IPAddress}}' "$a-control-plane")"
echo "the node of A is $node_a"

step "Certificates and the token"
# One name for the controller, whichever address an agent reaches it on: the agents are told to expect it (transport.serverName).
openssl req -x509 -newkey rsa:2048 -nodes -days 2 -subj "/CN=monitoring-test-ca" -keyout "$work/ca.key" -out "$work/ca.crt" 2>/dev/null
openssl req -newkey rsa:2048 -nodes -subj "/CN=monitoring-controller" -keyout "$work/tls.key" -out "$work/tls.csr" 2>/dev/null
printf 'subjectAltName=DNS:monitoring-controller\nextendedKeyUsage=serverAuth\n' >"$work/ext"
openssl x509 -req -in "$work/tls.csr" -CA "$work/ca.crt" -CAkey "$work/ca.key" -CAcreateserial -days 2 -extfile "$work/ext" -out "$work/tls.crt" 2>/dev/null
token="$(head -c 24 /dev/urandom | base64 | tr -d '=+/')"
hash="$(printf '%s' "$token" | sha256sum | cut -d' ' -f1)"

step "Controller in A, one address per replica"
ka create namespace monitoring >/dev/null
ka create secret generic controller-tls -n monitoring --from-file=tls.crt="$work/tls.crt" --from-file=tls.key="$work/tls.key" >/dev/null
# A replica is reached from the other cluster through its own node port; the agents learn all of them from the controller and connect to each.
for i in 0 1; do
  ka apply -n monitoring -f - >/dev/null <<YAML
apiVersion: v1
kind: Service
metadata: { name: monitoring-controller-$i-external }
spec:
  type: NodePort
  selector: { statefulset.kubernetes.io/pod-name: monitoring-controller-$i }
  ports: [{ port: 9440, targetPort: 9440, nodePort: 3044$i }]
YAML
done
helm --kube-context "kind-$a" install monitoring-controller "$root/deploy/helm/monitoring-controller" -n monitoring \
  --set image.repository=monitoring-controller --set image.tag=dev --set image.pullPolicy=Never \
  --set "agentTokens[0].scope=*" --set "agentTokens[0].tokenHash=$hash" \
  --set transport.mode=tls --set transport.existingSecret=controller-tls \
  --set "replicaAddresses={$node_a:30440,$node_a:30441}" >/dev/null
ka rollout status statefulset/monitoring-controller -n monitoring --timeout=180s >/dev/null || fail "the controller did not become ready"

# The same for both clusters: an agent pair that competes for a lease, and a sample application.
install_scope() { # context namespace
  local ctx="$1" ns="$2"
  kubectl --context "$ctx" create namespace "$ns" >/dev/null
  kubectl --context "$ctx" create secret generic agent-token -n "$ns" --from-literal=token="$token" >/dev/null
  kubectl --context "$ctx" create secret generic agent-ca -n "$ns" --from-file=ca.crt="$work/ca.crt" >/dev/null
  helm --kube-context "$ctx" install monitoring-agent "$root/deploy/helm/monitoring-agent" -n "$ns" \
    --set image.repository=monitoring-agent --set image.tag=dev --set image.pullPolicy=Never \
    --set "controller.addresses={$node_a:30440,$node_a:30441}" --set token.existingSecret=agent-token \
    --set transport.mode=tls --set transport.existingSecret=agent-ca --set transport.serverName=monitoring-controller \
    --set highAvailability.enabled=true --set scope.labels.environment=test --set "scope.labels.datacenter=${ctx#kind-}" >/dev/null
  ( cd "$root/deploy/helm/test/sample-app" && helm dependency update >/dev/null )
  helm --kube-context "$ctx" install sample "$root/deploy/helm/test/sample-app" -n "$ns" \
    --set image.repository=monitoring-sampleapp --set image.tag=dev \
    --set monitoring.image.repository=monitoring-sidecar --set monitoring.image.tag=dev --set monitoring.image.pullPolicy=Never \
    --set monitoring.agentAddress=monitoring-agent:9441 >/dev/null
  kubectl --context "$ctx" patch deployment orders -n "$ns" --type=json \
    -p '[{"op":"add","path":"/spec/template/spec/containers/0/imagePullPolicy","value":"Never"}]' >/dev/null
  kubectl --context "$ctx" rollout status deployment/orders -n "$ns" --timeout=180s >/dev/null || fail "the sample application in $ns did not become ready"
}

step "Agents and a sample application in both clusters"
install_scope "kind-$a" shop-a
install_scope "kind-$b" shop-b

ka port-forward -n monitoring pod/monitoring-controller-0 "$web_forward:8080" >/dev/null 2>&1 &
pids+=($!)
eventually 30 api /api/overview || fail "the dashboard of the controller cannot be reached"

# The application reports as healthy (the service 'sample' is the name it announces itself; 'orders' is the name of its pods, and stays Unhealthy while a replaced pod is remembered as gone). The answer is read into a variable first: a pipe into grep -q can end the curl early and fail under pipefail.
summary() { local body; body="$(api "/api/scopes/$1")" || return 1; python3 -c '
import json, sys
d = json.load(sys.stdin)
print(d["health"], " ".join(s["name"] + "=" + s["health"] + "/" + str(s["liveInstances"]) + ("@" + ",".join(i["version"] or "-" for i in s["instances"]) if s["name"] == "sample" else "") for s in d["services"]))' <<<"$body"; }
healthy() { local s; s="$(summary "$1")" || return 1; [[ "$s" == *" sample=Healthy/1@1.0.0"* ]]; }

step "One controller shows the scopes of both clusters"
eventually 120 healthy shop-a || fail "the scope of cluster A is not healthy"
eventually 120 healthy shop-b || fail "the scope of cluster B, across clusters, is not healthy"
api /api/overview | grep -q '"datacenter":"monitoring-b"' || fail "the labels of cluster B did not arrive"
echo "ok"

# --- agent failover, once per cluster
holder() { # context namespace
  kubectl --context "$1" get lease monitoring-agent -n "$2" -o jsonpath='{.spec.holderIdentity}' 2>/dev/null
}
ready_agents() { # context namespace
  kubectl --context "$1" get pods -n "$2" -l app.kubernetes.io/name=monitoring-agent \
    -o jsonpath='{range .items[*]}{.status.containerStatuses[0].ready}{"\n"}{end}' | grep -c true || true
}

# Looks at the scope every second in the background, so that afterwards it can be said for how many seconds the application was not shown as healthy.
watch_start() { : >"$work/watch"; ( while true; do if healthy "$1"; then echo ok; else echo bad; fi >>"$work/watch"; sleep 1; done ) & watcher=$!; pids+=($watcher); }
watch_stop() { kill "$watcher" 2>/dev/null || true; wait "$watcher" 2>/dev/null || true; grep -c bad "$work/watch" || true; }

for pair in "kind-$a shop-a" "kind-$b shop-b"; do
  set -- $pair; ctx="$1"; ns="$2"
  two_ready() { [ "$(ready_agents "$ctx" "$ns")" = 2 ]; }

  step "$ns: two agents, both ready, one holds the lease"
  eventually 60 holder "$ctx" "$ns"
  [ -n "$(holder "$ctx" "$ns")" ] || fail "no agent holds the lease in $ns"
  eventually 60 two_ready || fail "$ns has $(ready_agents "$ctx" "$ns") ready agents instead of two"
  echo "holder: $(holder "$ctx" "$ns")"

  step "$ns: a crashed holder is replaced by the other agent"
  old="$(holder "$ctx" "$ns")"
  watch_start "$ns"
  kubectl --context "$ctx" delete pod "$old" -n "$ns" --grace-period=0 --force >/dev/null 2>&1
  taken() { [ -n "$(holder "$ctx" "$ns")" ] && [ "$(holder "$ctx" "$ns")" != "$old" ]; }
  eventually 60 taken || fail "the other agent in $ns did not take the lease within 60 seconds"
  echo "new holder: $(holder "$ctx" "$ns")"
  eventually 120 healthy "$ns" || fail "the application in $ns was not healthy again within two minutes of a crashed holder (it says: $(summary "$ns" || echo nothing))"
  bad="$(watch_stop)"
  echo "the application was not shown as healthy for $bad s"
  [ "$bad" -le 60 ] || fail "the application in $ns was out of sight for $bad s after a crashed holder"
  kubectl --context "$ctx" rollout status deployment/monitoring-agent -n "$ns" --timeout=120s >/dev/null || fail "the agents of $ns did not recover"

  step "$ns: a rollout of the agents never leaves the scope without one"
  watch_start "$ns"
  kubectl --context "$ctx" rollout restart deployment/monitoring-agent -n "$ns" >/dev/null
  kubectl --context "$ctx" rollout status deployment/monitoring-agent -n "$ns" --timeout=180s >/dev/null || fail "the rollout of the agents in $ns did not finish"
  eventually 120 healthy "$ns" || fail "the application in $ns was not healthy again within two minutes of a rollout of the agents (it says: $(summary "$ns" || echo nothing))"
  bad="$(watch_stop)"
  echo "the application was not shown as healthy for $bad s"
  [ "$bad" -le 60 ] || fail "the application in $ns was out of sight for $bad s during a rollout of the agents"
  eventually 60 two_ready || fail "$ns has $(ready_agents "$ctx" "$ns") ready agents after the rollout"
done

step "A lost controller replica changes nothing for the scope of the other cluster"
ka delete pod monitoring-controller-1 -n monitoring --grace-period=0 --force >/dev/null 2>&1
healthy shop-b || fail "the scope of cluster B went away with one controller replica"
ka rollout status statefulset/monitoring-controller -n monitoring --timeout=180s >/dev/null
echo "ok"

echo
echo "Multi-cluster test passed"
