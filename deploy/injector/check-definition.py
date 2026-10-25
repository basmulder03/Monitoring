#!/usr/bin/env python3
"""Checks that the Helm template of the sidecar and the injector add the same sidecar to a pod.

The injector is tested against sidecar.golden.json (tests/Plover.Injector.Tests/DefinitionTests.cs); this checks the template against the same file, so the
two cannot drift apart. It renders the sample application, which has the values the golden file was made with, and compares the pieces that the sidecar adds.

Usage: deploy/injector/check-definition.py        (needs helm and PyYAML)
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile

import yaml

root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
# Rendered from a copy, so the packaged dependency of an earlier run (which can belong to another user) never gets in the way.
workdir = tempfile.mkdtemp()
shutil.copytree(os.path.join(root, "deploy", "helm", "test"), os.path.join(workdir, "test"), ignore=shutil.ignore_patterns("charts"))
shutil.copytree(os.path.join(root, "deploy", "helm", "plover-sidecar"), os.path.join(workdir, "plover-sidecar"))
chart = os.path.join(workdir, "test", "sample-app")
subprocess.run(["helm", "dependency", "update"], cwd=chart, check=True, stdout=subprocess.DEVNULL)
rendered = subprocess.run(["helm", "template", "t", "."], cwd=chart, check=True, capture_output=True, text=True).stdout

deployment = next(d for d in yaml.safe_load_all(rendered) if d and d.get("kind") == "Deployment")
spec = deployment["spec"]["template"]["spec"]
app = next(c for c in spec["containers"] if c["name"] != "plover-sidecar")
actual = {
    "sidecar": next(c for c in spec["initContainers"] if c["name"] == "plover-sidecar"),
    "volume": next(v for v in spec["volumes"] if v["name"] == "plover-socket"),
    "appEnv": next(e for e in app["env"] if e["name"] == "PLOVER_SOCKET"),
    "appMount": next(m for m in app["volumeMounts"] if m["name"] == "plover-socket"),
}

with open(os.path.join(root, "deploy", "injector", "sidecar.golden.json")) as handle:
    golden = json.load(handle)

# Lists of environment variables are compared in order, because the order is part of the definition.
if actual != golden:
    for key in golden:
        if actual.get(key) != golden[key]:
            print(f"The Helm template differs from the golden file in '{key}':")
            print("  template:", json.dumps(actual.get(key), sort_keys=True))
            print("  golden:  ", json.dumps(golden[key], sort_keys=True))
    sys.exit(1)

print("The Helm template and the injector add the same sidecar.")
