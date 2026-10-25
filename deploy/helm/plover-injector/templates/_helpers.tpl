{{- define "plover-injector.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "plover-injector.labels" -}}
app.kubernetes.io/name: {{ include "plover-injector.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version }}
{{- end -}}

{{- define "plover-injector.selector" -}}
app.kubernetes.io/name: {{ include "plover-injector.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{/* The name of the secret with the certificate of the webhook. */}}
{{- define "plover-injector.secret" -}}
{{- if eq .Values.tls.mode "existingSecret" -}}
{{- required "tls.existingSecret is needed with tls.mode existingSecret" .Values.tls.existingSecret -}}
{{- else -}}
{{- include "plover-injector.name" . }}-tls
{{- end -}}
{{- end -}}

{{/* The DNS names the API server reaches the webhook by. */}}
{{- define "plover-injector.dnsNames" -}}
{{- $name := include "plover-injector.name" . -}}
{{ $name }}.{{ .Release.Namespace }}.svc
{{ $name }}.{{ .Release.Namespace }}.svc.cluster.local
{{- end -}}
