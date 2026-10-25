namespace Plover.Protocol.Versioning;

/// <summary>The wire protocol version, announced in <see cref="Messages.Hello"/>.</summary>
public static class ProtocolVersion
{
    /// <summary>The version this build speaks. Increased whenever the message set changes incompatibly.</summary>
    public const int Current = 2;

    /// <summary>The first version that understands <see cref="Messages.HistoryRequest"/>; a peer below it must not be sent one, because it would close the connection.</summary>
    public const int HistoryRequests = 2;

    /// <summary>Tells whether a peer can be served: adjacent versions must work together during rollouts.</summary>
    /// <param name="remoteVersion">The version announced by the peer.</param>
    /// <returns><see langword="true"/> when the peer is at most one version away from <see cref="Current"/>.</returns>
    public static bool IsCompatible(int remoteVersion) => Math.Abs((long)remoteVersion - Current) <= 1;
}
