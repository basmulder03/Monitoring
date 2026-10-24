using System.Text.Json.Serialization;
using Monitoring.Agent.Features.Discovery.Kubernetes.Models;

namespace Monitoring.Agent.Features.Discovery.Kubernetes;

/// <summary>Source-generated JSON for the Kubernetes API answers the agent reads, which keeps it free of reflection for Native AOT.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PodList))]
[JsonSerializable(typeof(WatchEvent))]
[JsonSerializable(typeof(Pod))]
[JsonSerializable(typeof(EventList))]
[JsonSerializable(typeof(WorkloadList))]
[JsonSerializable(typeof(PodMetricsList))]
[JsonSerializable(typeof(ServiceList))]
[JsonSerializable(typeof(Status))]
[JsonSerializable(typeof(Lease))]
internal sealed partial class KubernetesJsonContext : JsonSerializerContext;
