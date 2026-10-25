#!/usr/bin/env bash
# Two clusters and one controller: the controller (two replicas) and an agent in cluster A, and agents with a sample application in both clusters, the one in B
# reaching the controller across clusters over tls with a token. Then the failover of the agents is tested on both: a crashed holder, and a rollout.
# Needs docker, kind, kubectl, helm, openssl and curl. Both clusters share the docker network of kind, so the nodes of A are reachable from the pods of B.
#
#   deploy/k8s-test/multicluster.sh              builds the images, creates both clusters, tests, deletes them
#   KEEP=1 deploy/k8s-test/multicluster.sh       leaves the clusters for a look
#   SKIP_BUILD=1 deploy/k8s-test/multicluster.sh uses the images plover-*:dev that are there
#   SKIP_FAILOVER=1 deploy/k8s-test/multicluster.sh skips the failover of the agents and goes on to the two controllers
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
a=plover-a
b=plover-b
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
  ka logs -n plover plover-controller-0 --tail=30 2>&1 | head -40 || true
  kb logs -n shop-b -l app.kubernetes.io/name=plover-agent --tail=30 --prefix 2>&1 | head -60 || true
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
  build Controller plover-controller
  build Agent plover-agent
  build Sidecar plover-sidecar
  build SampleApp plover-sampleapp samples/Plover.SampleApp
fi

step "Two clusters"
for name in "$a" "$b"; do
  kind delete cluster --name "$name" >/dev/null 2>&1 || true
  kind create cluster --name "$name" --wait 120s >/dev/null
  for image in plover-controller plover-agent plover-sidecar plover-sampleapp; do
    kind load docker-image "$image:dev" --name "$name" >/dev/null
  done
done
node_a="$(docker inspect -f '{{(index .NetworkSettings.Networks "kind").IPAddress}}' "$a-control-plane")"
echo "the node of A is $node_a"

step "Certificates and the token"
# One name for the controller, whichever address an agent reaches it on: the agents are told to expect it (transport.serverName).
openssl req -x509 -newkey rsa:2048 -nodes -days 2 -subj "/CN=plover-test-ca" -keyout "$work/ca.key" -out "$work/ca.crt" 2>/dev/null
openssl req -newkey rsa:2048 -nodes -subj "/CN=plover-controller" -keyout "$work/tls.key" -out "$work/tls.csr" 2>/dev/null
printf 'subjectAltName=DNS:plover-controller\nextendedKeyUsage=serverAuth\n' >"$work/ext"
openssl x509 -req -in "$work/tls.csr" -CA "$work/ca.crt" -CAkey "$work/ca.key" -CAcreateserial -days 2 -extfile "$work/ext" -out "$work/tls.crt" 2>/dev/null
token="$(head -c 24 /dev/urandom | base64 | tr -d '=+/')"
hash="$(printf '%s' "$token" | sha256sum | cut -d' ' -f1)"

step "Controller in A, one address per replica"
ka create namespace plover >/dev/null
ka create secret generic controller-tls -n plover --from-file=tls.crt="$work/tls.crt" --from-file=tls.key="$work/tls.key" >/dev/null
# A replica is reached from the other cluster through its own node port; the agents learn all of them from the controller and connect to each.
for i in 0 1; do
  ka apply -n plover -f - >/dev/null <<YAML
apiVersion: v1
kind: Service
metadata: { name: plover-controller-$i-external }
spec:
  type: NodePort
  selector: { statefulset.kubernetes.io/pod-name: plover-controller-$i }
  ports: [{ port: 9440, targetPort: 9440, nodePort: 3044$i }]
YAML
done
helm --kube-context "kind-$a" install plover-controller "$root/deploy/helm/plover-controller" -n plover \
  --set image.repository=plover-controller --set image.tag=dev --set image.pullPolicy=Never \
  --set "agentTokens[0].scope=*" --set "agentTokens[0].tokenHash=$hash" \
  --set transport.mode=tls --set transport.existingSecret=controller-tls \
  --set "replicaAddresses={$node_a:30440,$node_a:30441}" >/dev/null
ka rollout status statefulset/plover-controller -n plover --timeout=180s >/dev/null || fail "the controller did not become ready"

# The same for both clusters: an agent pair that competes for a lease, and a sample application.
install_scope() { # context namespace
  local ctx="$1" ns="$2"
  kubectl --context "$ctx" create namespace "$ns" >/dev/null
  kubectl --context "$ctx" create secret generic agent-token -n "$ns" --from-literal=token="$token" >/dev/null
  kubectl --context "$ctx" create secret generic agent-ca -n "$ns" --from-file=ca.crt="$work/ca.crt" >/dev/null
  helm --kube-context "$ctx" install plover-agent "$root/deploy/helm/plover-agent" -n "$ns" \
    --set image.repository=plover-agent --set image.tag=dev --set image.pullPolicy=Never \
    --set "controller.addresses={$node_a:30440,$node_a:30441}" --set token.existingSecret=agent-token \
    --set transport.mode=tls --set transport.existingSecret=agent-ca --set transport.serverName=plover-controller \
    --set highAvailability.enabled=true --set scope.labels.environment=test --set "scope.labels.datacenter=${ctx#kind-}" >/dev/null
  ( cd "$root/deploy/helm/test/sample-app" && helm dependency update >/dev/null )
  helm --kube-context "$ctx" install sample "$root/deploy/helm/test/sample-app" -n "$ns" \
    --set image.repository=plover-sampleapp --set image.tag=dev \
    --set plover.image.repository=plover-sidecar --set plover.image.tag=dev --set plover.image.pullPolicy=Never \
    --set plover.agentAddress=plover-agent:9441 >/dev/null
  kubectl --context "$ctx" patch deployment orders -n "$ns" --type=json \
    -p '[{"op":"add","path":"/spec/template/spec/containers/0/imagePullPolicy","value":"Never"}]' >/dev/null
  kubectl --context "$ctx" rollout status deployment/orders -n "$ns" --timeout=180s >/dev/null || fail "the sample application in $ns did not become ready"
}

step "Agents and a sample application in both clusters"
install_scope "kind-$a" shop-a
install_scope "kind-$b" shop-b

ka port-forward -n plover pod/plover-controller-0 "$web_forward:8080" >/dev/null 2>&1 &
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
api /api/overview | grep -q '"datacenter":"plover-b"' || fail "the labels of cluster B did not arrive"
echo "ok"

# --- agent failover, once per cluster
holder() { # context namespace
  kubectl --context "$1" get lease plover-agent -n "$2" -o jsonpath='{.spec.holderIdentity}' 2>/dev/null
}
ready_agents() { # context namespace
  kubectl --context "$1" get pods -n "$2" -l app.kubernetes.io/name=plover-agent \
    -o jsonpath='{range .items[*]}{.status.containerStatuses[0].ready}{"\n"}{end}' | grep -c true || true
}

# Looks at the scope every second in the background, so that afterwards it can be said for how many seconds the application was not shown as healthy.
watch_start() { : >"$work/watch"; ( while true; do if healthy "$1"; then echo ok; else echo bad; fi >>"$work/watch"; sleep 1; done ) & watcher=$!; pids+=($watcher); }
watch_stop() { kill "$watcher" 2>/dev/null || true; wait "$watcher" 2>/dev/null || true; grep -c bad "$work/watch" || true; }

if [ "${SKIP_FAILOVER:-0}" != 1 ]; then
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
  kubectl --context "$ctx" rollout status deployment/plover-agent -n "$ns" --timeout=120s >/dev/null || fail "the agents of $ns did not recover"

  step "$ns: a rollout of the agents never leaves the scope without one"
  watch_start "$ns"
  kubectl --context "$ctx" rollout restart deployment/plover-agent -n "$ns" >/dev/null
  kubectl --context "$ctx" rollout status deployment/plover-agent -n "$ns" --timeout=180s >/dev/null || fail "the rollout of the agents in $ns did not finish"
  eventually 120 healthy "$ns" || fail "the application in $ns was not healthy again within two minutes of a rollout of the agents (it says: $(summary "$ns" || echo nothing))"
  bad="$(watch_stop)"
  echo "the application was not shown as healthy for $bad s"
  [ "$bad" -le 60 ] || fail "the application in $ns was out of sight for $bad s during a rollout of the agents"
  eventually 60 two_ready || fail "$ns has $(ready_agents "$ctx" "$ns") ready agents after the rollout"
done
fi

step "A lost controller replica changes nothing for the scope of the other cluster"
ka delete pod plover-controller-1 -n plover --grace-period=0 --force >/dev/null 2>&1
healthy shop-b || fail "the scope of cluster B went away with one controller replica"
ka rollout status statefulset/plover-controller -n plover --timeout=180s >/dev/null
echo "ok"

# --- controllers in both clusters, and one sender between them
step "A controller in each cluster, every agent reporting to all four replicas"
node_b="$(docker inspect -f '{{(index .NetworkSettings.Networks "kind").IPAddress}}' "$b-control-plane")"

# What the controllers send is collected by a small receiver in cluster A that both clusters reach through a node port, and that prints every body it gets.
docker image inspect python:3-alpine >/dev/null 2>&1 || docker pull -q python:3-alpine >/dev/null
# One platform only: kind cannot load the multi-platform image of a registry as it is.
docker save --platform linux/amd64 python:3-alpine -o "$work/python.tar" 2>/dev/null || docker save python:3-alpine -o "$work/python.tar"
kind load image-archive "$work/python.tar" --name "$a" >/dev/null
ka apply -f - >/dev/null <<'YAML'
apiVersion: v1
kind: Pod
metadata: { name: receiver, labels: { app: receiver } }
spec:
  containers:
    - name: receiver
      image: python:3-alpine
      imagePullPolicy: Never
      command: [python, -u, -c]
      args:
        - |
          import http.server
          class H(http.server.BaseHTTPRequestHandler):
              def do_POST(self):
                  print(self.rfile.read(int(self.headers.get("Content-Length", 0))).decode(), flush=True)
                  self.send_response(204); self.end_headers()
              def log_message(self, *a): pass
          http.server.ThreadingHTTPServer(("0.0.0.0", 8000), H).serve_forever()
---
apiVersion: v1
kind: Service
metadata: { name: receiver }
spec:
  type: NodePort
  selector: { app: receiver }
  ports: [{ port: 8000, targetPort: 8000, nodePort: 30555 }]
YAML
ka wait --for=condition=Ready pod/receiver --timeout=120s >/dev/null || fail "the receiver did not start"
hooks() { ka logs receiver 2>/dev/null | grep '^{' || true; }

all_agents="$node_a:30440,$node_a:30441,$node_b:30440,$node_b:30441"
sites=("kind-$a $node_a dc1 0 $node_b" "kind-$b $node_b dc2 10 $node_a")
for site in "${sites[@]}"; do
  set -- $site; ctx="$1"; own="$2"; name="$3"; rank="$4"; other="$5"
  if [ "$ctx" = "kind-$b" ]; then
    kubectl --context "$ctx" create namespace plover >/dev/null
    kubectl --context "$ctx" create secret generic controller-tls -n plover --from-file=tls.crt="$work/tls.crt" --from-file=tls.key="$work/tls.key" >/dev/null
  fi
  for i in 0 1; do
    kubectl --context "$ctx" apply -n plover -f - >/dev/null <<YAML
apiVersion: v1
kind: Service
metadata: { name: plover-controller-$i-external }
spec:
  type: NodePort
  selector: { statefulset.kubernetes.io/pod-name: plover-controller-$i }
  ports:
    - { name: agents, port: 9440, targetPort: 9440, nodePort: 3044$i }
    - { name: web, port: 8080, targetPort: 8080, nodePort: 3008$i }
YAML
  done
  helm --kube-context "$ctx" upgrade --install plover-controller "$root/deploy/helm/plover-controller" -n plover \
    --set image.repository=plover-controller --set image.tag=dev --set image.pullPolicy=Never \
    --set "agentTokens[0].scope=*" --set "agentTokens[0].tokenHash=$hash" \
    --set transport.mode=tls --set transport.existingSecret=controller-tls \
    --set "replicaAddresses={$all_agents}" \
    --set notifications.peerSender=true --set "notifications.webhooks[0].url=http://$node_a:30555/hook" \
    --set replica.site="$name" --set replica.rank="$rank" \
    --set "peers.extra={http://$other:30080,http://$other:30081}" \
    --set 'extraEnv[0].name=PLOVER_Controller__Alerting__StartupGrace' --set 'extraEnv[0].value=00:00:05' >/dev/null
  kubectl --context "$ctx" rollout status statefulset/plover-controller -n plover --timeout=180s >/dev/null || fail "the controller of $name did not become ready"
done

kb port-forward -n plover pod/plover-controller-0 18082:8080 >/dev/null 2>&1 &
pids+=($!)
ka port-forward -n plover pod/plover-controller-0 "$web_forward:8080" >/dev/null 2>&1 &
pids+=($!)
eventually 30 curl -fsS http://127.0.0.1:18082/api/overview || fail "the dashboard of cluster B cannot be reached"

# The agents learn the other controllers from the first, when they connect again; the one in B has to know both scopes without anyone telling it.
seen_in_b() { curl -fsS http://127.0.0.1:18082/api/scopes/shop-a >/dev/null && curl -fsS http://127.0.0.1:18082/api/scopes/shop-b >/dev/null; }
eventually 150 seen_in_b || fail "the controller in cluster B does not show both scopes, so the agents did not start reporting to it"
echo "ok"

step "An alert is sent once, from one of the two sites"
ka scale deployment/orders -n shop-a --replicas=0 >/dev/null
fired() { hooks | grep -q '"status":"firing"'; }
eventually 120 fired || fail "no controller sent the alert that the service in shop-a is down"
sleep 25
keys="$(hooks | python3 -c '
import json, sys, collections
count = collections.Counter()
for line in sys.stdin:
    d = json.loads(line)
    if d["status"] == "firing":
        count[d["key"]] += 1
print(max(count.values()), len(count))
')"
echo "most times one alert was sent: ${keys% *}, alerts: ${keys#* }"
[ "${keys% *}" = 1 ] || fail "an alert was sent more than once between the two sites ($keys)"
echo "ok"

step "With the first site's controllers gone, the other site sends, and nothing is lost"
before="$(hooks | grep -c '"status":"firing"')"
# The replicas of the first site are stopped for good (not restarted), so the second site cannot see them for as long as it takes to be sure.
ka scale statefulset/plover-controller -n plover --replicas=0 >/dev/null
more() { [ "$(hooks | grep -c '"status":"firing"')" -gt "$before" ]; }
eventually 90 more || fail "the other site did not take over sending when the first was gone"
echo "ok (a new sender tells again what it has not told before; the receiver de-duplicates by key)"
ka scale statefulset/plover-controller -n plover --replicas=2 >/dev/null
ka rollout status statefulset/plover-controller -n plover --timeout=180s >/dev/null || fail "the controller of the first site did not come back"

echo
echo "Multi-cluster test passed"
