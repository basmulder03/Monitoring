using Monitoring.Protocol.Framing;

namespace Monitoring.Agent.Features.ControllerLink;

/// <summary>Settings of the controller link slice.</summary>
public sealed class ControllerLinkOptions
{
    /// <summary>
    /// Bootstrap controller address as <c>host:port</c>, plus an optional fallback list. The controller replies with the
    /// individual address of every replica, and the agent then connects to all of them.
    /// </summary>
    public List<string> ControllerAddresses { get; set; } = [];

    /// <summary>The per-scope token that proves who the agent is; the controller only holds its hash.</summary>
    public string? Token { get; set; }

    /// <summary>Id of this agent in the <c>Hello</c> message.</summary>
    public string AgentId { get; set; } = Environment.MachineName;

    /// <summary>Version of this agent in the <c>Hello</c> message, or <see langword="null"/> when unknown.</summary>
    public string? AgentVersion { get; set; }

    /// <summary>Free-form labels sent in the <c>Hello</c> message, such as <c>environment</c> and <c>datacenter</c>.</summary>
    public Dictionary<string, string> Labels { get; set; } = [];

    /// <summary>Shortest wait before reconnecting to a replica.</summary>
    public TimeSpan ReconnectMinDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Longest wait before reconnecting to a replica; repeated failures grow the wait up to this.</summary>
    public TimeSpan ReconnectMaxDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How long to wait for the controller's <c>Config</c> after sending the <c>Hello</c>.</summary>
    public TimeSpan ConfigReplyTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How often a full snapshot is sent to repair any drift between agent and controller.</summary>
    public TimeSpan SnapshotInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Largest frame accepted from the controller.</summary>
    public int MaxFrameBytes { get; set; } = FrameCodec.DefaultMaxFrameBytes;
}
