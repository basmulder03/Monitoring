using System.Diagnostics;

namespace Plover.Client.Features.Tracing;

/// <summary>
/// Makes the messages an application sends and takes show up in the dependency graph and carry the trace context from the sender to the receiver.
/// A Native AOT application cannot be patched at run time, so the application calls these around its own send and receive; they know nothing about
/// the broker library, which keeps the client free of one: you pass the code that sets and reads a property of a message.
/// </summary>
/// <example>
/// <code>
/// using var send = MonitoredMessaging.StartSend(MonitoredMessaging.ActiveMq, "orders", (name, value) => message.Properties.SetString(name, value));
/// try { producer.Send(message); } catch (Exception ex) { send.Fail(ex); throw; }
///
/// using var process = MonitoredMessaging.StartProcess(MonitoredMessaging.ActiveMq, "orders", name => message.Properties.GetString(name));
/// </code>
/// </example>
public static class MonitoredMessaging
{
    /// <summary>The activity source of the messages, which <see cref="PloverOptionsExtensions.UseActiveMq"/> makes the client listen to.</summary>
    public const string SourceName = "Plover.Client.Messaging";

    /// <summary>The system name for ActiveMQ and Artemis.</summary>
    public const string ActiveMq = "activemq";

    /// <summary>The name of the message property that carries the trace context, as W3C Trace Context defines it.</summary>
    public const string TraceParent = "traceparent";

    private static readonly ActivitySource Source = new(SourceName);

    /// <summary>Counts a message sent to a queue or topic, and puts the trace context into it so the receiver continues the trace.</summary>
    /// <param name="system">The kind of broker, such as <see cref="ActiveMq"/>.</param>
    /// <param name="destination">The name of the queue or topic; it becomes a node of its own in the graph.</param>
    /// <param name="setProperty">Sets a property of the message that is about to be sent.</param>
    /// <returns>The operation to dispose after the send, or <see langword="null"/> when nobody listens, in which case nothing was set.</returns>
    public static Activity? StartSend(string system, string destination, Action<string, string> setProperty)
    {
        var activity = Source.StartActivity($"send {destination}", ActivityKind.Producer);
        if (activity is null)
        {
            return null;
        }

        Tag(activity, system, destination, "publish");
        if (activity.Id is { } id && activity.IdFormat == ActivityIdFormat.W3C)
        {
            setProperty(TraceParent, id);
        }

        return activity;
    }

    /// <summary>Counts a message taken from a queue or topic, as work the application does for it, continuing the trace of the sender.</summary>
    /// <param name="system">The kind of broker, such as <see cref="ActiveMq"/>.</param>
    /// <param name="destination">The name of the queue or topic.</param>
    /// <param name="getProperty">Reads a property of the message that was received; returns <see langword="null"/> when it has none.</param>
    /// <returns>The operation to dispose when the message is dealt with, or <see langword="null"/> when nobody listens.</returns>
    public static Activity? StartProcess(string system, string destination, Func<string, string?> getProperty)
    {
        // A malformed context from the message is no reason to lose the message: the trace just starts here.
        ActivityContext.TryParse(getProperty(TraceParent), null, out var parent);
        var activity = Source.StartActivity($"process {destination}", ActivityKind.Consumer, parent);
        if (activity is not null)
        {
            Tag(activity, system, destination, "process");
        }

        return activity;
    }

    /// <summary>Marks the operation as failed, for example when the broker refused the message or handling it threw.</summary>
    /// <param name="activity">The operation returned by <see cref="StartSend"/> or <see cref="StartProcess"/>; <see langword="null"/> does nothing.</param>
    /// <param name="exception">What went wrong.</param>
    public static void Fail(this Activity? activity, Exception exception)
    {
        activity?.SetTag("error.type", exception.GetType().FullName);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    private static void Tag(Activity activity, string system, string destination, string operation)
    {
        activity.SetTag("messaging.system", system);
        activity.SetTag("messaging.destination.name", destination);
        activity.SetTag("messaging.operation.type", operation);
    }
}

/// <summary>Registrations on the options of the Plover client.</summary>
public static class PloverOptionsExtensions
{
    /// <summary>Makes the client listen to the messages that the application sends and takes through <see cref="MonitoredMessaging"/>.</summary>
    /// <param name="options">The options to extend.</param>
    /// <returns>The same options, for chaining.</returns>
    public static LocalServer.PloverOptions UseActiveMq(this LocalServer.PloverOptions options)
    {
        if (!options.TraceSources.Contains(MonitoredMessaging.SourceName))
        {
            options.TraceSources.Add(MonitoredMessaging.SourceName);
        }

        return options;
    }
}
