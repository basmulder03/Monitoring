using Monitoring.Protocol.Messages;

namespace Monitoring.Agent.Features.ControllerLink;

/// <summary>Runs one session with a controller replica over an open connection.</summary>
/// <param name="replica">The replica as <c>host:port</c>, which the session names its measurements after.</param>
/// <param name="stream">The connection.</param>
/// <param name="onConfig">Called with every <see cref="Config"/> the replica sends.</param>
/// <param name="ct">Ends the session when the agent shuts down.</param>
/// <returns>How the session ended.</returns>
internal delegate Task<SessionResult> RunSessionAsync(string replica, Stream stream, Action<Config> onConfig, CancellationToken ct);
