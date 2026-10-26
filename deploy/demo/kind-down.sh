#!/usr/bin/env bash
# Deletes the cluster of the live demo.
kind delete cluster --name plover-demo
rm -f "${KUBECONFIG_DEMO:-$HOME/.kube/plover-demo.config}"
