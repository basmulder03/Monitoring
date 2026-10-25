using Plover.Protocol.Framing;

namespace Plover.Agent.Features.SidecarConnections;

/// <summary>Settings of the sidecar connections slice.</summary>
public sealed class SidecarConnectionsOptions
{
    /// <summary>Whether to listen on a TCP port for sidecars, which is what Kubernetes needs; a Windows server only needs the pipe.</summary>
    public bool TcpEnabled { get; set; } = true;

    /// <summary>IP address to listen on; <c>0.0.0.0</c> listens on every IPv4 interface and <c>::</c> on every IPv6 one.</summary>
    public string ListenAddress { get; set; } = "0.0.0.0";

    /// <summary>TCP port sidecars connect to; 0 picks a free port, which is only useful in tests.</summary>
    public int Port { get; set; } = 9441;

    /// <summary>
    /// Whether a sidecar that connects over TCP has to come from the address of the pod it says it is, as the platform reports it. Without it, anything that can reach the
    /// port can report for any pod by name. It only applies to pods the platform knows an address for, and it needs the addresses of the pods to be what the agent sees, which they are
    /// unless something in between (a service mesh or a proxy) changes the source address; turn it off then, and restrict who may reach the port instead.
    /// </summary>
    public bool VerifyPeerAddress { get; set; } = true;

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

    /// <summary>
    /// The name of the named pipe that applications on the same machine connect to, as the sidecar does on Kubernetes; <see langword="null"/> for none, which is the default.
    /// An application is matched with the service it runs in by its process id, so it only shows up when that service is on the allowlist of the Windows discovery.
    /// </summary>
    public string? PipeName { get; set; }

    /// <summary>
    /// The accounts, besides the system and the administrators and the agent's own, that may connect to the pipe: the accounts the monitored applications run as,
    /// such as <c>NT SERVICE\billing</c> or <c>DOMAIN\svc-orders</c>. Only used on Windows, where the pipe is limited to the accounts in this list.
    /// </summary>
    public List<string> PipeAllow { get; set; } = [];

    /// <summary>Largest frame accepted from a sidecar, which bounds the memory one connection can claim.</summary>
    public int MaxFrameBytes { get; set; } = FrameCodec.DefaultMaxFrameBytes;
}
