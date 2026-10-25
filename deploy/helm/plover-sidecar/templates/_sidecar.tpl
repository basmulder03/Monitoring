{{/*
  The Plover sidecar for an application chart. Include the pieces in the pod template of the application:

    spec:
      initContainers:
        {{- include "plover.sidecar.initContainers" . | nindent 8 }}
      containers:
        - name: app
          env:
            {{- include "plover.sidecar.env" . | nindent 12 }}
          volumeMounts:
            {{- include "plover.sidecar.volumeMounts" . | nindent 12 }}
        {{- include "plover.sidecar.containers" . | nindent 8 }}
      volumes:
        {{- include "plover.sidecar.volumes" . | nindent 8 }}

  The values come from the application chart, under "plover":

    plover:
      enabled: true
      labels: { team: orders }            # free-form labels sent with the registration
      image: { repository: plover-sidecar, tag: "0.1.0" }
      agentAddress: plover-agent:9441  # the Service of the agent of the namespace
      nativeSidecar: true                  # false on Kubernetes before 1.29, where it becomes a second container

  The application itself needs a reference to the Plover client in its code; the sidecar only carries what the client reports.
*/}}

{{- define "plover.sidecar.enabled" -}}
{{- if and .Values.plover (default false .Values.plover.enabled) -}}true{{- end -}}
{{- end -}}

{{- define "plover.sidecar.native" -}}
{{- /* "default true false" would give true, so the key is looked at rather than its value */ -}}
{{- $m := default dict .Values.plover -}}
{{- if or (not (hasKey $m "nativeSidecar")) $m.nativeSidecar -}}true{{- end -}}
{{- end -}}

{{/* The container itself. As a native sidecar it starts before the application and stays up beside it. */}}
{{- define "plover.sidecar.container" -}}
{{- $m := .Values.plover -}}
{{- $image := default dict $m.image -}}
- name: plover-sidecar
  image: "{{ default "plover-sidecar" $image.repository }}:{{ default "latest" $image.tag }}"
  imagePullPolicy: {{ default "IfNotPresent" $image.pullPolicy }}
  {{- if include "plover.sidecar.native" . }}
  restartPolicy: Always
  {{- end }}
  securityContext:
    allowPrivilegeEscalation: false
    readOnlyRootFilesystem: true
    capabilities: { drop: ["ALL"] }
  env:
    - name: PLOVER_Sidecar__Agent
      value: {{ default "plover-agent:9441" $m.agentAddress | quote }}
    - name: PLOVER_Sidecar__Library__Socket
      value: /var/run/plover/plover.sock
    - name: PLOVER_Sidecar__Pod__Ip
      valueFrom: { fieldRef: { fieldPath: status.podIP } }
    - name: PLOVER_Sidecar__Pod__Node
      valueFrom: { fieldRef: { fieldPath: spec.nodeName } }
    - name: PLOVER_Sidecar__Pod__Namespace
      valueFrom: { fieldRef: { fieldPath: metadata.namespace } }
    {{- range $key, $value := $m.labels }}
    - name: PLOVER_Sidecar__Labels__{{ $key }}
      value: {{ $value | quote }}
    {{- end }}
  volumeMounts:
    - name: plover-socket
      mountPath: /var/run/plover
  resources: {{- toYaml (default (dict "requests" (dict "cpu" "10m" "memory" "16Mi") "limits" (dict "memory" "64Mi")) $m.resources) | nindent 4 }}
{{- end -}}

{{- define "plover.sidecar.initContainers" -}}
{{- if and (include "plover.sidecar.enabled" .) (include "plover.sidecar.native" .) -}}
{{ include "plover.sidecar.container" . }}
{{- end -}}
{{- end -}}

{{- define "plover.sidecar.containers" -}}
{{- if and (include "plover.sidecar.enabled" .) (not (include "plover.sidecar.native" .)) -}}
{{ include "plover.sidecar.container" . }}
{{- end -}}
{{- end -}}

{{/* Where the application listens for the sidecar, on a volume both containers share. */}}
{{- define "plover.sidecar.env" -}}
{{- if include "plover.sidecar.enabled" . -}}
- name: PLOVER_SOCKET
  value: /var/run/plover/plover.sock
{{- end -}}
{{- end -}}

{{- define "plover.sidecar.volumeMounts" -}}
{{- if include "plover.sidecar.enabled" . -}}
- name: plover-socket
  mountPath: /var/run/plover
{{- end -}}
{{- end -}}

{{/* Memory only, small: it holds a socket and nothing else. The group of the pod owns it, so an arbitrary user of group 0 on OpenShift can use it. */}}
{{- define "plover.sidecar.volumes" -}}
{{- if include "plover.sidecar.enabled" . -}}
- name: plover-socket
  emptyDir:
    medium: Memory
    sizeLimit: 1Mi
{{- end -}}
{{- end -}}
