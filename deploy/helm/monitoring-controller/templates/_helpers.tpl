{{- define "monitoring-controller.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "monitoring-controller.labels" -}}
app.kubernetes.io/name: {{ include "monitoring-controller.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version }}
{{- end -}}

{{- define "monitoring-controller.selector" -}}
app.kubernetes.io/name: {{ include "monitoring-controller.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{/* The address of every replica, as agents are told to connect to them. */}}
{{- define "monitoring-controller.replicaAddresses" -}}
{{- if .Values.replicaAddresses -}}
{{- range .Values.replicaAddresses }}{{ . }}{{ "\n" }}{{- end -}}
{{- else -}}
{{- $name := include "monitoring-controller.name" . -}}
{{- range $i := until (int .Values.replicas) -}}
{{ printf "%s-%d.%s-headless.%s.svc:%d" $name $i $name $.Release.Namespace (int $.Values.service.agentPort) }}{{ "\n" }}
{{- end -}}
{{- end -}}
{{- end -}}

{{/* Whether the controller needs a service account of its own: to review the tokens of agents, or to hold the lease that decides which replica sends alerts. */}}
{{- define "monitoring-controller.needsServiceAccount" -}}
{{- if or .Values.serviceAccountAuth.enabled .Values.notifications.oneSender -}}true{{- end -}}
{{- end -}}

{{/* One setting that is either given inline or read from a secret, as an environment variable. */}}
{{- define "monitoring-controller.setting" -}}
{{- $name := index . 0 -}}{{- $inline := index . 1 -}}{{- $secret := index . 2 -}}
{{- if $secret }}
- name: {{ $name }}
  valueFrom:
    secretKeyRef:
      name: {{ required "a secret reference needs a name" $secret.name }}
      key: {{ required "a secret reference needs a key" $secret.key }}
{{- else if $inline }}
- name: {{ $name }}
  value: {{ $inline | quote }}
{{- end }}
{{- end -}}
