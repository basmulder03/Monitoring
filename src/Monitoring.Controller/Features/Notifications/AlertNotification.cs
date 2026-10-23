using Monitoring.Controller.Features.Alerting;

namespace Monitoring.Controller.Features.Notifications;

/// <summary>What an alert channel is told: an alert started, or it ended.</summary>
/// <param name="Firing"><see langword="true"/> when the alert started, <see langword="false"/> when it ended.</param>
/// <param name="Alert">The alert; for one that ended, as it was when it was last seen.</param>
public sealed record AlertNotification(bool Firing, Alert Alert);

/// <summary>A channel that alerts are sent to, such as a webhook, a chat or email.</summary>
public interface IAlertSink
{
    /// <summary>The name of the channel, which names it in the log and in what the controller remembers of it.</summary>
    string Name { get; }

    /// <summary>Sends one notification.</summary>
    /// <param name="notification">What to tell.</param>
    /// <param name="ct">Cancels the send.</param>
    /// <returns>A task that completes when the channel has accepted it.</returns>
    /// <exception cref="HttpRequestException">The channel could not be reached or refused it; the notification is sent again later.</exception>
    Task SendAsync(AlertNotification notification, CancellationToken ct);
}

/// <summary>Tells whether this controller replica is the one that sends alerts, so that several replicas do not send everything several times.</summary>
public interface ILeaderElection
{
    /// <summary>Whether this replica sends now.</summary>
    bool IsLeader { get; }
}
