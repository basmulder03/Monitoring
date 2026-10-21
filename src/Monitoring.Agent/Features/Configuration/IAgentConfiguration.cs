using Monitoring.Protocol.Messages;

namespace Monitoring.Agent.Features.Configuration;

/// <summary>The configuration the controller last handed to this agent, which every part of the agent reads its intervals from.</summary>
/// <remarks>
/// Until a controller has answered, and after a restart while none can be reached, the built-in defaults apply. When the controller is
/// unreachable later on, the last configuration stays in force.
/// </remarks>
public interface IAgentConfiguration
{
    /// <summary>The configuration in force.</summary>
    Config Current { get; }

    /// <summary>Starts using a configuration a controller sent. A controller sending the same one again changes nothing.</summary>
    /// <param name="config">The configuration of a controller replica.</param>
    void Apply(Config config);
}
