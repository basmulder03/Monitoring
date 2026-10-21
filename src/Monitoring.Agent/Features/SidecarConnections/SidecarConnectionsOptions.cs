using Monitoring.Protocol.Framing;

namespace Monitoring.Agent.Features.SidecarConnections;

/// <summary>Settings of the sidecar connections slice.</summary>
public sealed class SidecarConnectionsOptions
{
    /// <summary>IP address to listen on; <c>0.0.0.0</c> listens on every IPv4 interface and <c>::</c> on every IPv6 one.</summary>
    public string ListenAddress { get; set; } = "0.0.0.0";

    /// <summary>TCP port sidecars connect to; 0 picks a free port, which is only useful in tests.</summary>
    public int Port { get; set; } = 9441;

    /// <summary>How long a new connection may stay silent before it must have sent its <c>Hello</c>.</summary>
    public TimeSpan HelloTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How often sidecars are told to send a heartbeat.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many heartbeats in a row may be missing before the sidecar counts as gone and its connection is closed.</summary>
    public int MissedHeartbeats { get; set; } = 3;

    /// <summary>
    /// How often the health checks of an application are pulled again even when the heartbeat shows no change, so a check that
    /// changes without changing the overall health still reaches the controller.
    /// </summary>
    public TimeSpan HealthPullInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How often the metrics of an application are pulled.</summary>
    public TimeSpan MetricsPullInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How often the dependency edges of an application are pulled.</summary>
    public TimeSpan EdgesPullInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Largest frame accepted from a sidecar, which bounds the memory one connection can claim.</summary>
    public int MaxFrameBytes { get; set; } = FrameCodec.DefaultMaxFrameBytes;
}
