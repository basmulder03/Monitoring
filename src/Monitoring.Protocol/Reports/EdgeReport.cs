namespace Monitoring.Protocol.Reports;

/// <summary>The calls an application made to one thing it depends on, counted since it started.</summary>
/// <param name="Type">What kind of dependency: <c>HTTP</c>, <c>DB</c> or <c>MQ</c>.</param>
/// <param name="Target">Where the calls went: <c>host:port</c> for HTTP, the server and database for a database, the queue or topic for a broker. The controller resolves it to a service.</param>
/// <param name="Calls">How many calls completed.</param>
/// <param name="Errors">How many of them failed.</param>
/// <param name="DurationSumMs">The time all calls took together, in milliseconds.</param>
/// <param name="Buckets">How many calls took at most each of <see cref="EdgesReport.BucketBoundsMs"/>, not cumulative, plus one more for the calls that took longer than the last bound.</param>
public sealed record EdgeReport(string Type, string Target, long Calls, long Errors, double DurationSumMs, IReadOnlyList<long> Buckets);
