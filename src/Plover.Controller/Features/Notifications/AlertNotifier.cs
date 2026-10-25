using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plover.Controller.Features.Alerting;
using Plover.Controller.Features.StateStore;

namespace Plover.Controller.Features.Notifications;

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<Alert>))]
internal sealed partial class NotifiedJsonContext : JsonSerializerContext;

/// <summary>
/// Tells the channels when an alert starts and when it ends. It compares the alerts that are firing with what each channel was last told, so a channel hears about each alert once;
/// what it was told is kept in the state store, so a restart of the controller does not repeat it. A channel that cannot be reached is tried again at the next look, and the others are not held up.
/// Nothing is sent while this replica is not the leader, or while the alerts are not yet judged (the startup grace period), because then "nothing is firing" would mean "nothing is known".
/// </summary>
/// <param name="alerts">The alerts that are firing.</param>
/// <param name="sinks">The channels.</param>
/// <param name="leader">Tells whether this replica sends.</param>
/// <param name="options">The interval and the lowest severity that is sent.</param>
/// <param name="store">Keeps what each channel was told.</param>
/// <param name="logger">Receives what could not be sent.</param>
internal sealed class AlertNotifier(IAlertState alerts, IReadOnlyList<IAlertSink> sinks, ILeaderElection leader, NotificationOptions options, IStateStore store, ILogger<AlertNotifier> logger) : BackgroundService
{
    private readonly Dictionary<string, Dictionary<string, Alert>> _told = [];
    private readonly AlertSeverity _minimum = options.MinSeverity.Equals("critical", StringComparison.OrdinalIgnoreCase) ? AlertSeverity.Critical : AlertSeverity.Warning;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Interval);
        do
        {
            await TickAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>Compares the alerts with what each channel was told and tells it the difference.</summary>
    /// <param name="ct">Cancels the sending.</param>
    /// <returns>A task that completes when every channel has been dealt with.</returns>
    public async Task TickAsync(CancellationToken ct)
    {
        if (!leader.IsLeader || !alerts.IsReady)
        {
            return;
        }

        var current = alerts.GetActiveAlerts().Where(alert => alert.Severity >= _minimum).ToDictionary(alert => alert.Key, StringComparer.Ordinal);
        foreach (var sink in sinks)
        {
            var told = ToldTo(sink);
            var changed = false;
            foreach (var alert in current.Values.Where(alert => !told.ContainsKey(alert.Key)))
            {
                if (!await TrySendAsync(sink, new AlertNotification(true, alert), ct).ConfigureAwait(false))
                {
                    break;
                }

                told[alert.Key] = alert;
                changed = true;
            }

            foreach (var gone in told.Values.Where(alert => !current.ContainsKey(alert.Key)).ToList())
            {
                if (!await TrySendAsync(sink, new AlertNotification(false, gone), ct).ConfigureAwait(false))
                {
                    break;
                }

                told.Remove(gone.Key);
                changed = true;
            }

            if (changed)
            {
                store.Append($"notified:{sink.Name}", JsonSerializer.Serialize(told.Values.ToList(), NotifiedJsonContext.Default.ListAlert), keep: 1);
            }
        }
    }

    private Dictionary<string, Alert> ToldTo(IAlertSink sink)
    {
        if (!_told.TryGetValue(sink.Name, out var told))
        {
            var saved = store.Read($"notified:{sink.Name}").LastOrDefault();
            told = saved is null ? [] : (JsonSerializer.Deserialize(saved, NotifiedJsonContext.Default.ListAlert) ?? []).ToDictionary(alert => alert.Key, StringComparer.Ordinal);
            _told[sink.Name] = told;
        }

        return told;
    }

    private async Task<bool> TrySendAsync(IAlertSink sink, AlertNotification notification, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            await sink.SendAsync(notification, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Net.Mail.SmtpException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            // The rest of this channel waits for the next look, in order, so nothing is told out of sequence.
            logger.LogWarning("Cannot tell {Sink} about {Rule} for {Subject}, will try again: {Reason}", sink.Name, notification.Alert.RuleName, notification.Alert.Subject, ex.Message);
            return false;
        }
    }
}
