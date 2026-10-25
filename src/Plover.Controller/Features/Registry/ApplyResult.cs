namespace Plover.Controller.Features.Registry;

/// <summary>Result of applying a message from an agent.</summary>
/// <param name="Outcome">What the registry did with the message.</param>
/// <param name="AcknowledgedSequence">
/// The last sequence number applied for the scope, which the replica acknowledges back to the agent;
/// <see langword="null"/> when nothing has been applied for the scope yet.
/// </param>
public sealed record ApplyResult(ApplyOutcome Outcome, long? AcknowledgedSequence);
