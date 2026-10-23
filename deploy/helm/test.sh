#!/usr/bin/env bash
# Lints and renders the charts, and checks what the rendered output must contain. Uses helm when it is installed, else Docker.
set -euo pipefail
cd "$(dirname "$0")"
root="$PWD"

if command -v helm >/dev/null 2>&1; then
  helm() { command helm "$@"; }
else
  # The whole folder is mounted, and the working folder kept, so charts can refer to each other.
  helm() { docker run --rm -v "$root:/w" -w "/w${PWD#"$root"}" alpine/helm "$@"; }
fi

fail() { echo "FAIL: $*" >&2; exit 1; }
has() { grep -q -- "$1" <<<"$2" || fail "$3"; }
lacks() { ! grep -q -- "$1" <<<"$2" || fail "$3"; }

agent=(--set 'controller.addresses={controller:9440}' --set token.existingSecret=agent-token)
controller=(--set 'agentTokens[0].scope=*' --set 'agentTokens[0].tokenHash=abc')

helm lint monitoring-agent "${agent[@]}"
helm lint monitoring-controller "${controller[@]}"

# The agent: one replica, a role that only reads pods, and settings that reach the executable.
out="$(helm template t monitoring-agent "${agent[@]}" --set scope.labels.environment=prod)"
has 'replicas: 1' "$out" "the agent must run once per namespace"
has 'resources: \["pods"\]' "$out" "the agent needs to read pods"
lacks 'clusterrole' "$out" "the agent must not need cluster-wide rights"
has 'MONITORING_Agent__Labels__environment' "$out" "labels must reach the agent"
has 'runAsNonRoot: true' "$out" "the agent must not run as root"
has "controller.addresses" "$(helm template t monitoring-agent 2>&1 || true)" "a missing controller address must be refused"

# Service account authentication: a projected token made for the controller on the agent, no secret, and on the controller a right to review tokens.
out="$(helm template t monitoring-agent --set 'controller.addresses={controller:9440}' --set auth.mode=serviceAccount --set auth.audience=mon)"
has 'serviceAccountToken' "$out" "the agent needs a projected token"
has 'audience: "mon"' "$out" "the token must be made for the controller"
has 'MONITORING_Agent__TokenFile' "$out" "the agent must read the token from its file"
lacks 'secretKeyRef' "$out" "service account authentication needs no secret"
lacks 'MONITORING_Agent__Token$' "$out" "a fixed token must not be set as well"

# The controller: a stateful set with an address per replica.
out="$(helm template t monitoring-controller "${controller[@]}")"
has 'kind: StatefulSet' "$out" "the controller needs an address per replica"
has 'monitoring-controller-1.monitoring-controller-headless' "$out" "every replica must have its own address"
has 'clusterIP: None' "$out" "the headless service is missing"
out="$(helm template t monitoring-controller "${controller[@]}" --set 'replicaAddresses={a.example:443,b.example:443}')"
has 'b.example:443' "$out" "given replica addresses must be used"
lacks 'headless.default.svc' "$out" "given replica addresses must replace the generated ones"
has "agentTokens" "$(helm template t monitoring-controller 2>&1 || true)" "a controller without tokens must be refused"
out="$(helm template t monitoring-controller --set serviceAccountAuth.enabled=true --set serviceAccountAuth.scopes.shop-prod=shop)"
has 'system:auth-delegator' "$out" "the controller must be allowed to review tokens"
has 'MONITORING_Controller__ServiceAccounts__Enabled' "$out" "the controller must be told to accept service accounts"
has 'ServiceAccounts__Scopes__shop-prod' "$out" "a configured scope for a namespace must reach the controller"
has 'serviceAccountName: monitoring-controller' "$out" "the controller must run as the service account that may review tokens"
lacks 'MONITORING_Controller__AgentTokens' "$out" "no agent tokens are needed with service account authentication"
lacks 'system:auth-delegator' "$(helm template t monitoring-controller "${controller[@]}")" "the controller must not get the right when it is not used"

# The sidecar template, in a chart that uses it.
(
  cd test/sample-app
  helm dependency update >/dev/null
  out="$(helm template t .)"
  has 'restartPolicy: Always' "$out" "a native sidecar needs restartPolicy Always"
  has 'MONITORING_SOCKET' "$out" "the application must be told where its socket is"
  out="$(helm template t . --set monitoring.nativeSidecar=false)"
  lacks 'restartPolicy: Always' "$out" "the fallback must be a normal container"
  lacks 'initContainers' "$out" "the fallback must not use init containers"
  has 'name: monitoring-sidecar' "$out" "the fallback must still have the sidecar"
  out="$(helm template t . --set monitoring.enabled=false)"
  lacks 'monitoring' "$out" "a disabled sidecar must leave no trace"
)

echo "Charts passed"
