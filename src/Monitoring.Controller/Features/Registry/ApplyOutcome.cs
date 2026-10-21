namespace Monitoring.Controller.Features.Registry;

/// <summary>What the registry did with a <see cref="Monitoring.Protocol.Messages.Delta"/>.</summary>
public enum ApplyOutcome : byte
{
    /// <summary>The delta followed the previous message and was applied.</summary>
    Applied,

    /// <summary>The delta was applied, but one or more earlier messages were missed; the next snapshot repairs any drift.</summary>
    AppliedWithGap,

    /// <summary>The delta was already applied (its sequence is not newer than the last one) and was ignored.</summary>
    Duplicate,

    /// <summary>The scope has not sent a snapshot yet, so the delta was ignored; the agent should send a snapshot.</summary>
    SnapshotRequired,
}
