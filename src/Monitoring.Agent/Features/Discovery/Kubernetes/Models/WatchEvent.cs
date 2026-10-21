using System.Text.Json;

namespace Monitoring.Agent.Features.Discovery.Kubernetes.Models;

/// <summary>One line of a watch stream: what happened and to which object.</summary>
/// <param name="Type">ADDED, MODIFIED, DELETED, BOOKMARK or ERROR.</param>
/// <param name="Object">The pod, or for ERROR a <see cref="Status"/>; read according to <paramref name="Type"/>.</param>
internal sealed record WatchEvent(string Type, JsonElement Object);
