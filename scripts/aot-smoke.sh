#!/usr/bin/env bash
# Starts the published (Native AOT) controller and agent and checks that the agent shows up on the controller's dashboard API.
# Usage: scripts/aot-smoke.sh <folder with the published controller, agent and sidecar folders>
# These are failures that only appear in the published binaries: trimmed or missing code, reflection, source generators.
set -euo pipefail

root="${1:?Give the folder that holds Controller, Agent and Sidecar, as published}"
token="smoke-token"
hash="$(printf '%s' "$token" | sha256sum | cut -d' ' -f1)"
agent_port=$((20000 + RANDOM % 10000))
web_port=$((30000 + RANDOM % 10000))
logs="$(mktemp -d)"
pids=()

cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  wait 2>/dev/null || true
  if [ "${failed:-0}" = 1 ]; then
    for log in "$logs"/*.log; do echo "--- $log"; cat "$log"; done
  fi
  rm -rf "$logs"
}
trap 'failed=1; cleanup' ERR
trap cleanup EXIT

export PLOVER_Controller__Listen__Address=127.0.0.1
export PLOVER_Controller__Listen__Port="$agent_port"
export PLOVER_Controller__Web__Address=127.0.0.1
export PLOVER_Controller__Web__Port="$web_port"
export PLOVER_Controller__AgentTokens__0__Scope="*"
export PLOVER_Controller__AgentTokens__0__TokenHash="$hash"
"$root/Controller/Plover.Controller" >"$logs/controller.log" 2>&1 &
pids+=($!)

for _ in $(seq 1 50); do
  curl -fsS "http://127.0.0.1:$web_port/api/overview" >/dev/null 2>&1 && break
  sleep 0.2
done
curl -fsS "http://127.0.0.1:$web_port/api/overview" >/dev/null

PLOVER_Agent__ScopeName=smoke PLOVER_Agent__Token="$token" PLOVER_Agent__Controllers="127.0.0.1:$agent_port" \
  "$root/Agent/Plover.Agent" >"$logs/agent.log" 2>&1 &
pids+=($!)

# The sidecar has no application to ask, so it only has to start and keep running.
PLOVER_Sidecar__Agent="127.0.0.1:1" "$root/Sidecar/Plover.Sidecar" >"$logs/sidecar.log" 2>&1 &
sidecar=$!
pids+=("$sidecar")

for _ in $(seq 1 50); do
  curl -fsS "http://127.0.0.1:$web_port/api/overview" | grep -q '"name":"smoke"' && found=1 && break
  sleep 0.2
done
[ "${found:-0}" = 1 ] || { echo "The agent did not show up on the controller"; false; }

# The dashboard page and its script must be served by the published controller too.
curl -fsS "http://127.0.0.1:$web_port/" | grep -q '<div id="root">'
curl -fsS "http://127.0.0.1:$web_port/app.js" >/dev/null
curl -fsS "http://127.0.0.1:$web_port/api/topology" >/dev/null
curl -fsS "http://127.0.0.1:$web_port/api/packages" >/dev/null
curl -fsS "http://127.0.0.1:$web_port/api/sbom" >/dev/null
curl -fsS "http://127.0.0.1:$web_port/api/vulnerabilities" >/dev/null

kill -0 "$sidecar" 2>/dev/null || { echo "The sidecar stopped"; false; }

# The injector answers an admission review over TLS, and adds the sidecar for a pod of a namespace that asked.
injector_port=$((40000 + RANDOM % 10000))
openssl req -x509 -newkey rsa:2048 -nodes -days 1 -subj "/CN=localhost" -keyout "$logs/injector.key" -out "$logs/injector.crt" >/dev/null 2>&1
PLOVER_Injector__SidecarImage=plover-sidecar:smoke PLOVER_Injector__Certificate="$logs/injector.crt" PLOVER_Injector__Key="$logs/injector.key" \
  PLOVER_Injector__Web__Address=127.0.0.1 PLOVER_Injector__Web__Port="$injector_port" \
  "$root/Injector/Plover.Injector" >"$logs/injector.log" 2>&1 &
pids+=($!)
for _ in $(seq 1 50); do
  curl -fsSk "https://127.0.0.1:$injector_port/healthz" >/dev/null 2>&1 && break
  sleep 0.2
done
review='{"apiVersion":"admission.k8s.io/v1","kind":"AdmissionReview","request":{"uid":"smoke","object":{"metadata":{"name":"p"},"spec":{"containers":[{"name":"app","image":"a:1"}]}}}}'
answer="$(curl -fsSk -H 'Content-Type: application/json' -d "$review" "https://127.0.0.1:$injector_port/mutate/namespace")"
echo "$answer" | grep -q '"patchType":"JSONPatch"' || { echo "The injector did not answer with a patch: $answer"; false; }
echo "AOT smoke test passed"
