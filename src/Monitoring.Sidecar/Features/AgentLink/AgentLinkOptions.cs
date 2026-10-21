using Monitoring.Protocol.Framing;

namespace Monitoring.Sidecar.Features.AgentLink;

/// <summary>Settings of the link to the agent of the namespace.</summary>
public sealed class AgentLinkOptions
{
    /// <summary>The address of the agent as <c>host:port</c>, normally the agent's Service in the same namespace.</summary>
    public string AgentAddress { get; set; } = "";

    /// <summary>
    /// Id of this instance. In Kubernetes the host name of a pod is its name, which is also what the agent sees in the pod watch,
    /// so the two can be matched without any configuration.
    /// </summary>
    public string InstanceId { get; set; } = Environment.MachineName;

    /// <summary>Free-form labels sent with the registration.</summary>
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>Shortest wait before connecting to the agent again.</summary>
    public TimeSpan ReconnectMinDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Longest wait before connecting to the agent again; repeated failures grow the wait up to this.</summary>
    public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait for the agent's answer to the registration.</summary>
    public TimeSpan ConfigReplyTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Largest frame accepted from the agent.</summary>
    public int MaxFrameBytes { get; set; } = FrameCodec.DefaultMaxFrameBytes;
}
