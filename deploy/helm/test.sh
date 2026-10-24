#!/usr/bin/env bash
# Lints the charts, runs their unit tests (tests/*_test.yaml next to each chart, with helm-unittest) and checks that the injector and the sidecar template agree.
# Install the plugin once with: helm plugin install https://github.com/helm-unittest/helm-unittest --verify=false
set -euo pipefail
cd "$(dirname "$0")"
root="$PWD"

command -v helm >/dev/null 2>&1 || { echo "FAIL: helm is needed" >&2; exit 1; }
helm plugin list | grep -q '^unittest' || { echo "FAIL: the helm-unittest plugin is needed; see the top of this file" >&2; exit 1; }

helm lint monitoring-agent --set 'controller.addresses={controller:9440}' --set token.existingSecret=agent-token
helm lint monitoring-controller --set 'agentTokens[0].scope=*' --set 'agentTokens[0].tokenHash=abc'

helm dependency update test/sample-app >/dev/null
helm unittest monitoring-agent monitoring-controller monitoring-injector test/sample-app

# The sidecar the injector adds must be the one the library chart includes.
if python3 -c 'import yaml' 2>/dev/null; then
  python3 "$root/../injector/check-definition.py" || { echo "FAIL: the injector and the Helm template add different sidecars" >&2; exit 1; }
else
  echo "skipped: the check that the injector and the template add the same sidecar needs python3 with PyYAML"
fi

echo "Charts passed"
