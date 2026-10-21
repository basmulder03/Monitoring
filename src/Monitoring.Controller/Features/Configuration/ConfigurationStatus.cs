namespace Monitoring.Controller.Features.Configuration;

/// <summary>Whether the agent of a scope uses the configuration the controller would give it now.</summary>
/// <param name="Applied">The version the agent says it uses; <see langword="null"/> when it has not said, for example right after connecting.</param>
/// <param name="Expected">The version the controller would give the agent at this moment.</param>
public sealed record ConfigurationStatus(string? Applied, string Expected)
{
    /// <summary>
    /// <see langword="true"/> when the agent uses the expected configuration. An agent that has not said which one it uses is not
    /// counted as out of date, because nothing is known to be wrong.
    /// </summary>
    public bool InSync => Applied is null || Applied == Expected;
}
