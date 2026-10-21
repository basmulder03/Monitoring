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
