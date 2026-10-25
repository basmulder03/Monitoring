{{- define "plover-agent.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "plover-agent.labels" -}}
app.kubernetes.io/name: {{ include "plover-agent.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version }}
{{- end -}}

{{- define "plover-agent.selector" -}}
app.kubernetes.io/name: {{ include "plover-agent.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}
