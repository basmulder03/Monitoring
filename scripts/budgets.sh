#!/usr/bin/env bash
# Starts the published (Native AOT) controller, agent and sidecar and checks how long they take to start and how much memory they hold once idle.
# Usage: scripts/budgets.sh <folder with the published controller, agent and sidecar folders>
# The limits are about two and a half times what was measured (see docs/testing.md), so only a real regression fails, not the noise of a shared runner.
set -euo pipefail

root="${1:?Give the folder that holds Controller, Agent and Sidecar, as published}"

# Start time in milliseconds until ready, and resident memory in MB after the settle time.
start_limit_ms=1500
controller_mb=80
agent_mb=48
sidecar_mb=40
settle_seconds=3

token="budget-token"
hash="$(printf '%s' "$token" | sha256sum | cut -d' ' -f1)"
agent_port=$((20000 + RANDOM % 10000))
web_port=$((30000 + RANDOM % 10000))
logs="$(mktemp -d)"
pids=()
failures=0

cleanup() {
  for pid in "${pids[@]}"; do kill "$pid" 2>/dev/null || true; done
  wait 2>/dev/null || true
  rm -rf "$logs"
}
trap cleanup EXIT

now_ms() { echo $(( $(date +%s%N) / 1000000 )); }
rss_mb() { awk '/VmRSS/ { printf "%d", $2 / 1024 }' "/proc/$1/status"; }

# Waits until a command succeeds and returns how many milliseconds that took.
ready_after() {
  local began
  began="$(now_ms)"
  for _ in $(seq 1 500); do
    if "$@" >/dev/null 2>&1; then echo $(( $(now_ms) - began )); return 0; fi
    sleep 0.01
  done
  echo 99999
}

check() { # name measured limit unit
  if [ "$2" -gt "$3" ]; then
    echo "OVER BUDGET: $1 is $2 $4, the limit is $3 $4"
    failures=$((failures + 1))
  else
    echo "ok: $1 is $2 $4 (limit $3)"
  fi
}

export PLOVER_Controller__Listen__Address=127.0.0.1
export PLOVER_Controller__Listen__Port="$agent_port"
export PLOVER_Controller__Web__Address=127.0.0.1
export PLOVER_Controller__Web__Port="$web_port"
export PLOVER_Controller__AgentTokens__0__Scope="*"
export PLOVER_Controller__AgentTokens__0__TokenHash="$hash"
"$root/Controller/Plover.Controller" >"$logs/controller.log" 2>&1 &
controller=$!
pids+=("$controller")
controller_ms="$(ready_after curl -fsS "http://127.0.0.1:$web_port/api/overview")"

started() { grep -q "Application started" "$1"; }

PLOVER_Agent__ScopeName=budget PLOVER_Agent__Token="$token" PLOVER_Agent__Controllers="127.0.0.1:$agent_port" \
  "$root/Agent/Plover.Agent" >"$logs/agent.log" 2>&1 &
agent=$!
pids+=("$agent")
agent_ms="$(ready_after started "$logs/agent.log")"

PLOVER_Sidecar__Agent="127.0.0.1:1" "$root/Sidecar/Plover.Sidecar" >"$logs/sidecar.log" 2>&1 &
sidecar=$!
pids+=("$sidecar")
sidecar_ms="$(ready_after started "$logs/sidecar.log")"

sleep "$settle_seconds"

check "controller start time" "$controller_ms" "$start_limit_ms" ms
check "agent start time" "$agent_ms" "$start_limit_ms" ms
check "sidecar start time" "$sidecar_ms" "$start_limit_ms" ms
check "controller memory" "$(rss_mb "$controller")" "$controller_mb" MB
check "agent memory" "$(rss_mb "$agent")" "$agent_mb" MB
check "sidecar memory" "$(rss_mb "$sidecar")" "$sidecar_mb" MB

[ "$failures" = 0 ] || { echo "$failures budget(s) exceeded"; exit 1; }
echo "Budgets met"
