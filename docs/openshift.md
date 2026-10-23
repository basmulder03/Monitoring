# Running on OpenShift

The charts in `deploy/helm` run on OpenShift 4.16 and later without changes. This guide covers what OpenShift does differently from plain Kubernetes: the user the containers run as, how the socket between the sidecar and the application stays usable, how traffic gets in and out, and which network policies to add. It describes only what the charts and the images do today.

## The restricted security context

OpenShift starts each container with an arbitrary user id from a range of the namespace, always in group 0. Nothing has to be set for that to work:

- The charts set `runAsNonRoot`, the `RuntimeDefault` seccomp profile, no privilege escalation and no capabilities, and they set **no** `runAsUser`, so OpenShift is free to pick one. Do not set `runAsUser`; the `restricted-v2` security context constraint would reject a fixed id outside the range of the namespace.
- The images default to user 1654, but nothing depends on that: they write nothing outside the volumes they are given, so any user of group 0 works.
- The sidecar's root file system is read-only.

## The socket between the sidecar and the application

The sidecar and the application talk through a Unix socket on a shared in-memory `emptyDir`. The application's client creates the socket with read and write for its user and for its group, and the volume belongs to the group of the pod. Because OpenShift runs the sidecar and the application as different users of the same group 0, both can use it. The `monitoring-sidecar` library chart mounts the same volume in both containers; if you write the pod yourself, do the same.

## Native sidecars

The sidecar is a native sidecar (an init container with `restartPolicy: Always`), which needs Kubernetes 1.29, so OpenShift 4.16 or later. On an older cluster set `monitoring.nativeSidecar: false` in the application chart. The sidecar then becomes a second container and retries until the application is up.

## Getting traffic in

The charts create Services only. Routes carry HTTP and TLS, nothing else, so they fit the two ways in:

**The dashboard.** A normal Route to the `monitoring-controller` Service, port `web`. Limit it to your internal network with the `haproxy.router.openshift.io/ip_whitelist` annotation.

```yaml
apiVersion: route.openshift.io/v1
kind: Route
metadata:
  name: monitoring
  annotations:
    haproxy.router.openshift.io/ip_whitelist: 10.0.0.0/8
spec:
  to: { kind: Service, name: monitoring-controller }
  port: { targetPort: web }
  tls: { termination: edge, insecureEdgeTerminationPolicy: Redirect }
```

**Agents in other clusters.** An agent has to reach every replica, so each replica needs a Route of its own, with **passthrough** termination, so the agent's TLS ends at the controller. That needs transport mode `tls` or `mtls` on both sides (see `docs/configuration.md`, "Transport security"). Create a Service per replica (select the pod with `statefulset.kubernetes.io/pod-name`), a Route per Service, and list the hosts in the controller chart:

```yaml
# values of the controller chart
transport: { mode: mtls, existingSecret: controller-tls }
replicaAddresses:
  - monitoring-0.apps.example.com:443
  - monitoring-1.apps.example.com:443
```

```yaml
apiVersion: route.openshift.io/v1
kind: Route
metadata:
  name: monitoring-0
spec:
  host: monitoring-0.apps.example.com
  to: { kind: Service, name: monitoring-controller-0 }   # a Service you create for the pod monitoring-controller-0
  port: { targetPort: agents }
  tls: { termination: passthrough }
```

**Agents in the same cluster** use the headless Service of the stateful set, which the charts already wire up. Mode `none` works there.

## Network policies

If the namespaces have a default-deny policy, three flows must be allowed. Adjust the namespace names to yours.

Applications to the agent, in the monitored namespace (the sidecars connect to port 9441):

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: allow-sidecars-to-agent
spec:
  podSelector:
    matchLabels: { app.kubernetes.io/name: monitoring-agent }
  ingress:
    - from: [{ podSelector: {} }]
      ports: [{ port: 9441 }]
```

The agent to the controller replicas and to the Kubernetes API, in the monitored namespace:

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: allow-agent-egress
spec:
  podSelector:
    matchLabels: { app.kubernetes.io/name: monitoring-agent }
  policyTypes: [Egress]
  egress:
    - to: [{ namespaceSelector: { matchLabels: { kubernetes.io/metadata.name: monitoring } } }]
      ports: [{ port: 9440 }]
    - ports: [{ port: 443 }, { port: 6443 }]   # the API server
    - to: [{ namespaceSelector: {} }]
      ports: [{ port: 53, protocol: UDP }, { port: 5353, protocol: UDP }]
```

Agents and the router to the controller, in the namespace of the controller:

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: allow-agents-and-router
spec:
  podSelector:
    matchLabels: { app.kubernetes.io/name: monitoring-controller }
  ingress:
    - ports: [{ port: 9440 }]
    - from: [{ namespaceSelector: { matchLabels: { policy-group.network.openshift.io/ingress: "" } } }]
      ports: [{ port: 8080 }]
```

## Windows hosts

Windows servers report through an agent that runs as a Windows service and connect to the same controller. How to install it, with a script and with Ansible, is in [windows.md](windows.md). Hosts outside the cluster reach the controller through the passthrough routes described above, which needs transport mode `tls` or `mtls`.
