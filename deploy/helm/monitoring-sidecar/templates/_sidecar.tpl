{{/*
  The monitoring sidecar for an application chart. Include the pieces in the pod template of the application:

    spec:
      initContainers:
        {{- include "monitoring.sidecar.initContainers" . | nindent 8 }}
      containers:
        - name: app
          env:
            {{- include "monitoring.sidecar.env" . | nindent 12 }}
          volumeMounts:
            {{- include "monitoring.sidecar.volumeMounts" . | nindent 12 }}
        {{- include "monitoring.sidecar.containers" . | nindent 8 }}
      volumes:
        {{- include "monitoring.sidecar.volumes" . | nindent 8 }}

  The values come from the application chart, under "monitoring":

    monitoring:
      enabled: true
      labels: { team: orders }            # free-form labels sent with the registration
      image: { repository: monitoring-sidecar, tag: "0.1.0" }
      agentAddress: monitoring-agent:9441  # the Service of the agent of the namespace
      nativeSidecar: true                  # false on Kubernetes before 1.29, where it becomes a second container

  The application itself needs a reference to the monitoring client in its code; the sidecar only carries what the client reports.
*/}}

{{- define "monitoring.sidecar.enabled" -}}
{{- if and .Values.monitoring (default false .Values.monitoring.enabled) -}}true{{- end -}}
{{- end -}}

{{- define "monitoring.sidecar.native" -}}
{{- /* "default true false" would give true, so the key is looked at rather than its value */ -}}
{{- $m := default dict .Values.monitoring -}}
{{- if or (not (hasKey $m "nativeSidecar")) $m.nativeSidecar -}}true{{- end -}}
{{- end -}}

{{/* The container itself. As a native sidecar it starts before the application and stays up beside it. */}}
{{- define "monitoring.sidecar.container" -}}
{{- $m := .Values.monitoring -}}
{{- $image := default dict $m.image -}}
- name: monitoring-sidecar
  image: "{{ default "monitoring-sidecar" $image.repository }}:{{ default "latest" $image.tag }}"
  imagePullPolicy: {{ default "IfNotPresent" $image.pullPolicy }}
  {{- if include "monitoring.sidecar.native" . }}
  restartPolicy: Always
  {{- end }}
  securityContext:
    allowPrivilegeEscalation: false
    readOnlyRootFilesystem: true
    capabilities: { drop: ["ALL"] }
  env:
    - name: MONITORING_Sidecar__Agent
      value: {{ default "monitoring-agent:9441" $m.agentAddress | quote }}
    - name: MONITORING_Sidecar__Library__Socket
      value: /var/run/monitoring/monitoring.sock
    {{- range $key, $value := $m.labels }}
    - name: MONITORING_Sidecar__Labels__{{ $key }}
      value: {{ $value | quote }}
    {{- end }}
  volumeMounts:
    - name: monitoring-socket
      mountPath: /var/run/monitoring
  resources: {{- toYaml (default (dict "requests" (dict "cpu" "10m" "memory" "16Mi") "limits" (dict "memory" "64Mi")) $m.resources) | nindent 4 }}
{{- end -}}

{{- define "monitoring.sidecar.initContainers" -}}
{{- if and (include "monitoring.sidecar.enabled" .) (include "monitoring.sidecar.native" .) -}}
{{ include "monitoring.sidecar.container" . }}
{{- end -}}
{{- end -}}

{{- define "monitoring.sidecar.containers" -}}
{{- if and (include "monitoring.sidecar.enabled" .) (not (include "monitoring.sidecar.native" .)) -}}
{{ include "monitoring.sidecar.container" . }}
{{- end -}}
{{- end -}}

{{/* Where the application listens for the sidecar, on a volume both containers share. */}}
{{- define "monitoring.sidecar.env" -}}
{{- if include "monitoring.sidecar.enabled" . -}}
- name: MONITORING_SOCKET
  value: /var/run/monitoring/monitoring.sock
{{- end -}}
{{- end -}}

{{- define "monitoring.sidecar.volumeMounts" -}}
{{- if include "monitoring.sidecar.enabled" . -}}
- name: monitoring-socket
  mountPath: /var/run/monitoring
{{- end -}}
{{- end -}}

{{/* Memory only, small: it holds a socket and nothing else. The group of the pod owns it, so an arbitrary user of group 0 on OpenShift can use it. */}}
{{- define "monitoring.sidecar.volumes" -}}
{{- if include "monitoring.sidecar.enabled" . -}}
- name: monitoring-socket
  emptyDir:
    medium: Memory
    sizeLimit: 1Mi
{{- end -}}
{{- end -}}
