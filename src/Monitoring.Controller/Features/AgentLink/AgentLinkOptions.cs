using Monitoring.Common.Transport;
using Monitoring.Protocol.Framing;

namespace Monitoring.Controller.Features.AgentLink;

/// <summary>Settings of the agent link slice.</summary>
public sealed class AgentLinkOptions
{
    /// <summary>IP address to listen on; <c>0.0.0.0</c> listens on every IPv4 interface and <c>::</c> on every IPv6 one.</summary>
    public string ListenAddress { get; set; } = "0.0.0.0";

    /// <summary>TCP port agents connect to; 0 picks a free port, which is only useful in tests.</summary>
    public int Port { get; set; } = 9440;

    /// <summary>How long a new connection may stay silent before it must have sent its <c>Hello</c>.</summary>
    public TimeSpan HelloTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Largest frame accepted from an agent, which bounds the memory one connection can claim.</summary>
    public int MaxFrameBytes { get; set; } = FrameCodec.DefaultMaxFrameBytes;

    /// <summary>
    /// The individual address of every controller replica as <c>host:port</c>, handed to agents so they connect to all of them.
    /// </summary>
    public List<string> ControllerReplicas { get; set; } = [];

    /// <summary>Version reported in <c>Config</c>; the central configuration replaces this with a content hash.</summary>
    public string ConfigVersion { get; set; } = "built-in";

    /// <summary>How connections of agents are protected; plain TCP by default, which is only for a network you trust.</summary>
    public TransportSecurityOptions Transport { get; set; } = new();
}
