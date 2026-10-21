namespace Monitoring.Sidecar.Features.AgentLink;

/// <summary>Opens a connection to the agent.</summary>
/// <param name="address">The agent as <c>host:port</c>.</param>
/// <param name="ct">Cancels the attempt.</param>
/// <returns>The connection; the caller disposes it.</returns>
internal delegate ValueTask<Stream> ConnectAsync(string address, CancellationToken ct);
