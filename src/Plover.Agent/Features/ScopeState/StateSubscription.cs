using System.Threading.Channels;
using Plover.Protocol.Messages;

namespace Plover.Agent.Features.ScopeState;

/// <summary>
/// One controller connection's view of the scope: the state at the moment it subscribed, then every later delta.
/// Dispose it when the connection ends.
/// </summary>
/// <param name="Initial">Full state at the moment of subscribing; send it first.</param>
/// <param name="Deltas">
/// Every delta published after <paramref name="Initial"/>, in order. Reading throws when the subscriber fell too far
/// behind and was cut off; the connection should then be restarted from a fresh subscription.
/// </param>
/// <param name="onDispose">Detaches the subscription from the store.</param>
public sealed class StateSubscription(Snapshot Initial, ChannelReader<Delta> Deltas, Action onDispose) : IDisposable
{
    /// <summary>Full state at the moment of subscribing.</summary>
    public Snapshot Initial { get; } = Initial;

    /// <summary>Deltas published after <see cref="Initial"/>, in order.</summary>
    public ChannelReader<Delta> Deltas { get; } = Deltas;

    /// <summary>Stops delivery and detaches from the store.</summary>
    public void Dispose() => onDispose();
}
