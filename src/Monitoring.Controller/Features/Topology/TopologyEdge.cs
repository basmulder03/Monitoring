namespace Monitoring.Controller.Features.Topology;

/// <summary>The calls from one node to another, added up over all instances of the caller.</summary>
/// <param name="From">Id of the calling node.</param>
/// <param name="To">Id of the node that was called.</param>
/// <param name="Type">How it was called: <c>HTTP</c>, <c>DB</c> or <c>MQ</c>.</param>
/// <param name="Calls">How many calls the instances have made since they started.</param>
/// <param name="Errors">How many of those failed.</param>
/// <param name="DurationSumMs">The time all calls took together, in milliseconds.</param>
public sealed record TopologyEdge(string From, string To, string Type, long Calls, long Errors, double DurationSumMs);
