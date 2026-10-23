using Microsoft.Extensions.Logging.Abstractions;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Notifications;
using Monitoring.Controller.Features.StateStore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Notifications;

public class AlertNotifierTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Alert Fire(string subject, AlertSeverity severity = AlertSeverity.Warning) =>
        new($"rule:{subject}", "rule", subject, severity, new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));

    private sealed class FakeAlerts : IAlertState
    {
        public List<Alert> Active { get; } = [];

        public bool Ready { get; set; } = true;

        public bool IsReady => Ready;

        public IReadOnlyList<Alert> GetActiveAlerts() => [.. Active];
    }

    private sealed class RecordingSink(string name = "sink") : IAlertSink
    {
        public List<string> Sent { get; } = [];

        public bool Fails { get; set; }

        public string Name => name;

        public Task SendAsync(AlertNotification notification, CancellationToken ct)
        {
            if (Fails)
            {
                throw new HttpRequestException("unreachable");
            }

            Sent.Add($"{(notification.Firing ? "firing" : "resolved")} {notification.Alert.Subject}");
            return Task.CompletedTask;
        }
    }

    private sealed class Leader : ILeaderElection
    {
        public bool IsLeader { get; set; } = true;
    }

    private static AlertNotifier Notifier(FakeAlerts alerts, IStateStore store, Leader? leader = null, string minimum = "warning", params IAlertSink[] sinks) =>
        new(alerts, sinks, leader ?? new Leader(), new NotificationOptions { MinSeverity = minimum }, store, NullLogger<AlertNotifier>.Instance);

    private static IStateStore Store() => new ServiceCollection().AddStateStore().BuildServiceProvider().GetRequiredService<IStateStore>();

    [Fact]
    public async Task A_channel_hears_about_an_alert_once_when_it_starts_and_once_when_it_ends()
    {
        var alerts = new FakeAlerts();
        var sink = new RecordingSink();
        var notifier = Notifier(alerts, Store(), sinks: sink);

        alerts.Active.Add(Fire("shop/orders"));
        await notifier.TickAsync(Ct);
        await notifier.TickAsync(Ct);
        alerts.Active.Clear();
        await notifier.TickAsync(Ct);
        await notifier.TickAsync(Ct);

        sink.Sent.ShouldBe(["firing shop/orders", "resolved shop/orders"]);
    }

    [Fact]
    public async Task Nothing_is_sent_by_a_replica_that_is_not_the_leader_or_while_the_alerts_are_not_judged_yet()
    {
        var alerts = new FakeAlerts { Ready = false };
        alerts.Active.Add(Fire("shop/orders"));
        var sink = new RecordingSink();
        var leader = new Leader { IsLeader = false };
        var notifier = Notifier(alerts, Store(), leader, sinks: sink);

        await notifier.TickAsync(Ct);
        alerts.Ready = true;
        await notifier.TickAsync(Ct);

        sink.Sent.ShouldBeEmpty();

        leader.IsLeader = true;
        await notifier.TickAsync(Ct);
        sink.Sent.ShouldBe(["firing shop/orders"]);
    }

    [Fact]
    public async Task A_channel_that_cannot_be_reached_is_tried_again_and_does_not_hold_up_the_others()
    {
        var alerts = new FakeAlerts();
        alerts.Active.Add(Fire("shop/orders"));
        var down = new RecordingSink("down") { Fails = true };
        var up = new RecordingSink("up");
        var notifier = Notifier(alerts, Store(), sinks: [down, up]);

        await notifier.TickAsync(Ct);
        down.Fails = false;
        await notifier.TickAsync(Ct);

        up.Sent.ShouldBe(["firing shop/orders"]);
        down.Sent.ShouldBe(["firing shop/orders"]);
    }

    [Fact]
    public async Task Only_alerts_of_the_lowest_severity_and_above_are_sent()
    {
        var alerts = new FakeAlerts();
        alerts.Active.Add(Fire("shop/a", AlertSeverity.Warning));
        alerts.Active.Add(Fire("shop/b", AlertSeverity.Critical));
        var sink = new RecordingSink();

        await Notifier(alerts, Store(), minimum: "critical", sinks: sink).TickAsync(Ct);

        sink.Sent.ShouldBe(["firing shop/b"]);
    }

    [Fact]
    public async Task A_restarted_controller_does_not_tell_again_what_was_told_but_tells_what_ended_meanwhile()
    {
        var store = Store();
        var alerts = new FakeAlerts();
        alerts.Active.Add(Fire("shop/orders"));
        alerts.Active.Add(Fire("shop/billing"));
        var first = new RecordingSink();
        await Notifier(alerts, store, sinks: first).TickAsync(Ct);

        // The controller restarts; billing recovered while it was away.
        alerts.Active.RemoveAll(alert => alert.Subject == "shop/billing");
        var second = new RecordingSink();
        await Notifier(alerts, store, sinks: second).TickAsync(Ct);

        first.Sent.ShouldBe(["firing shop/orders", "firing shop/billing"]);
        second.Sent.ShouldBe(["resolved shop/billing"]);
    }
}
