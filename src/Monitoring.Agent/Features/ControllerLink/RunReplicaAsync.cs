using Monitoring.Protocol.Messages;

namespace Monitoring.Agent.Features.ControllerLink;

/// <summary>Keeps one controller replica connected until cancelled.</summary>
/// <param name="address">The replica as <c>host:port</c>.</param>
/// <param name="onConfig">Called with every <see cref="Config"/> the replica sends.</param>
/// <param name="ct">Stops the connection.</param>
/// <returns>A task that completes when the connection loop has ended.</returns>
internal delegate Task RunReplicaAsync(string address, Action<Config> onConfig, CancellationToken ct);
