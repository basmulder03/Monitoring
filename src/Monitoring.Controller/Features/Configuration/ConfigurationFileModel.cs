namespace Monitoring.Controller.Features.Configuration;

/// <summary>The settings that can be given at every layer; a setting that is left out keeps what the layer below says.</summary>
/// <param name="PullSeconds">How often an agent pulls the metrics of its applications.</param>
/// <param name="EdgeFlushSeconds">How often an agent pulls the calls of its applications to their dependencies.</param>
internal sealed record SettingsLayer(int? PullSeconds = null, int? EdgeFlushSeconds = null);

/// <summary>Which agents an override applies to; every condition that is given must hold.</summary>
/// <param name="Scope">The scope the agent reports as.</param>
/// <param name="Labels">Labels the scope must have, with exactly these values.</param>
/// <param name="Service">A service in the scope, which narrows what the override sets to the applications of that service.</param>
internal sealed record OverrideMatch(string? Scope = null, Dictionary<string, string>? Labels = null, string? Service = null);

/// <summary>Settings for the agents that match.</summary>
/// <param name="Match">Which agents.</param>
/// <param name="Set">What they get.</param>
internal sealed record OverrideRule(OverrideMatch Match, SettingsLayer Set);

/// <summary>The content of the central configuration file.</summary>
/// <param name="Defaults">Settings for every agent.</param>
/// <param name="Overrides">Settings for the agents that match, applied in order of how specific they are.</param>
/// <param name="Overridable">The settings a local override on the agent may change.</param>
internal sealed record ConfigurationFileModel(SettingsLayer? Defaults = null, List<OverrideRule>? Overrides = null, List<string>? Overridable = null);
