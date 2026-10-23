using System.Diagnostics.Metrics;
using Monitoring.Client.Features.LocalServer;
using Monitoring.Client.Features.Queues;
using Shouldly;
using Xunit;

namespace Monitoring.Client.Tests.Features.Queues;

// The metrics collector listens to every meter of the process, so tests that make meters must not overlap with its tests.
[Collection("Meters")]
public class QueueMeterTests
{
    private static Dictionary<string, double> Read(params QueueWatch[] watches)
    {
        using var meter = new QueueMeter(new MonitoringOptions { Queues = [.. watches] });
        var found = new Dictionary<string, double>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == QueueMeter.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => found[$"{instrument.Name}/{tags[0].Value}"] = value);
        listener.Start();
        listener.RecordObservableInstruments();
        return found;
    }

    [Fact]
    public void Depth_consumers_and_dead_letters_are_reported_per_queue_and_what_the_broker_does_not_tell_is_left_out()
    {
        // Act
        var found = Read(
            new QueueWatch("orders", () => new QueueStats(12, 2, 3)),
            new QueueWatch("audit", () => new QueueStats(0)));

        // Assert
        found.OrderBy(pair => pair.Key).ToList().ShouldBe(new Dictionary<string, double>
        {
            ["monitoring.queue.depth/orders"] = 12,
            ["monitoring.queue.consumers/orders"] = 2,
            ["monitoring.queue.dead_letters/orders"] = 3,
            ["monitoring.queue.depth/audit"] = 0,
        }.OrderBy(pair => pair.Key).ToList());
    }

    [Fact]
    public void A_queue_that_cannot_be_read_is_left_out_and_the_others_are_still_reported()
    {
        // Act
        var found = Read(
            new QueueWatch("broken", () => throw new IOException("broker down")),
            new QueueWatch("unknown", () => null),
            new QueueWatch("orders", () => new QueueStats(5)));

        // Assert
        found.Keys.ShouldBe(["monitoring.queue.depth/orders"]);
    }
}
