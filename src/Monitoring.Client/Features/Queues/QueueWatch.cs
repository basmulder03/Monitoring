namespace Monitoring.Client.Features.Queues;

/// <summary>What a message queue holds at one moment.</summary>
/// <param name="Depth">Messages waiting.</param>
/// <param name="Consumers">Consumers attached, or <see langword="null"/> when the broker does not tell.</param>
/// <param name="DeadLetters">Messages in the dead-letter queue of it, or <see langword="null"/> when there is none or it is not known.</param>
public sealed record QueueStats(long Depth, long? Consumers = null, long? DeadLetters = null);

/// <summary>A queue whose depth, consumers and dead letters are reported as metrics. The application knows how to ask its broker; the monitoring only asks the application.</summary>
/// <param name="Name">The name that tells the queue apart in the metrics, for example <c>orders</c>.</param>
/// <param name="Read">Gets the numbers each time the metrics are read, which is every few seconds: answer from a cache or a cheap call, not a slow one. May return <see langword="null"/> while they are not known.</param>
public sealed record QueueWatch(string Name, Func<QueueStats?> Read);
