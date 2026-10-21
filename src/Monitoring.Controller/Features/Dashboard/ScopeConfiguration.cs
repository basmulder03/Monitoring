namespace Monitoring.Controller.Features.Dashboard;

/// <summary>Which configuration the agent of a scope uses compared with what it should use.</summary>
/// <param name="Applied">The version the agent says it uses; <see langword="null"/> when it has not said.</param>
/// <param name="Expected">The version the controller would give the agent now.</param>
/// <param name="InSync"><see langword="false"/> when the agent says it uses another version than the expected one, for example because a change has not reached it.</param>
public sealed record ScopeConfiguration(string? Applied, string Expected, bool InSync);
