namespace Plover.Agent.Features.ControllerLink;

/// <summary>Opens a connection to a controller replica.</summary>
/// <param name="address">The replica as <c>host:port</c>.</param>
/// <param name="ct">Cancels the attempt.</param>
/// <returns>The connection; the caller disposes it.</returns>
internal delegate ValueTask<Stream> ConnectAsync(string address, CancellationToken ct);
