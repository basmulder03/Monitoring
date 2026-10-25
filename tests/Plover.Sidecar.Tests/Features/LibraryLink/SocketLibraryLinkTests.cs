using Microsoft.Extensions.Logging.Abstractions;
using Plover.Protocol.Health;
using Plover.Protocol.Messages;
using Plover.Protocol.Reports;
using Plover.Sidecar.Features.LibraryLink;
using Plover.Sidecar.Tests.Shared;
using Shouldly;
using Xunit;

namespace Plover.Sidecar.Tests.Features.LibraryLink;

public class SocketLibraryLinkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetDescriptionAsync_returns_what_the_application_says_about_itself()
    {
        // Arrange
        await using var app = new FakeApplication { Description = new InstanceDescription("orders", "2.1.0") }.Start();
        using var link = LinkTo(app);

        // Act
        var description = await link.GetDescriptionAsync(Ct);

        // Assert
        description!.ServiceName.ShouldBe("orders");
        description.Version.ShouldBe("2.1.0");
    }

    [Fact]
    public async Task GetHealthAsync_returns_the_health_report_of_the_application()
    {
        // Arrange
        await using var app = new FakeApplication { Health = new HealthReport(HealthState.Degraded, [new HealthCheckResult("cache", HealthState.Degraded, "cold")]) }.Start();
        using var link = LinkTo(app);

        // Act
        var health = await link.GetHealthAsync(Ct);

        // Assert
        health!.Health.ShouldBe(HealthState.Degraded);
        health.Checks.Single().Name.ShouldBe("cache");
    }

    [Fact]
    public async Task PullAsync_returns_the_answer_of_the_application_with_its_cursor()
    {
        // Arrange
        await using var app = new FakeApplication { Metrics = new MetricsReport([new MetricSeries("orders.placed", new Dictionary<string, string>(), 9)]) }.Start();
        using var link = LinkTo(app);

        // Act
        var answer = await link.PullAsync(PullKind.Metrics, 5, Ct);

        // Assert
        var metrics = ReportPayload.ReadMetrics(answer!.PayloadJson)!;
        metrics.Series.Single().Name.ShouldBe("orders.placed");
        metrics.Series.Single().Value.ShouldBe(9);
        answer.Cursor.ShouldBe(5);
    }

    [Fact]
    public async Task The_link_asks_several_questions_over_one_connection()
    {
        // Arrange
        await using var app = new FakeApplication().Start();
        using var link = LinkTo(app);

        // Act
        await link.GetDescriptionAsync(Ct);
        await link.GetHealthAsync(Ct);
        var again = await link.GetHealthAsync(Ct);

        // Assert
        again.ShouldNotBeNull();
        app.QuestionsAsked.ShouldBe(3);
    }

    [Fact]
    public async Task The_link_returns_nothing_when_the_application_is_not_there()
    {
        // Arrange
        using var link = new SocketLibraryLink(
            new LibraryLinkOptions { SocketPath = Path.Combine(Path.GetTempPath(), $"nothing-{Guid.NewGuid():N}.sock") },
            TimeProvider.System,
            NullLogger<SocketLibraryLink>.Instance);

        // Act
        var health = await link.GetHealthAsync(Ct);

        // Assert
        health.ShouldBeNull();
    }

    [Fact]
    public async Task The_link_returns_nothing_when_the_application_does_not_answer_in_time()
    {
        // Arrange
        await using var app = new FakeApplication { Silent = true }.Start();
        using var link = LinkTo(app, callTimeout: TimeSpan.FromMilliseconds(150));

        // Act
        var health = await link.GetHealthAsync(Ct);

        // Assert
        health.ShouldBeNull();
    }

    [Fact]
    public async Task The_link_finds_the_application_again_after_it_restarted()
    {
        // Arrange
        var app = new FakeApplication { Description = new InstanceDescription("orders", "1.0.0") }.Start();
        using var link = LinkTo(app);
        (await link.GetDescriptionAsync(Ct)).ShouldNotBeNull();
        await app.StopAsync();
        (await link.GetDescriptionAsync(Ct)).ShouldBeNull();

        // Act
        await using var restarted = new FakeApplication { Description = new InstanceDescription("orders", "1.1.0") }.Start();
        File.Move(restarted.SocketPath, app.SocketPath, overwrite: true);
        var description = await link.GetDescriptionAsync(Ct);

        // Assert
        description!.Version.ShouldBe("1.1.0");
    }

    [Fact]
    public async Task The_link_ignores_a_late_answer_to_an_earlier_question()
    {
        // Arrange: the first question is answered only after the link gave up on it
        await using var app = new FakeApplication
        {
            FirstAnswerDelay = TimeSpan.FromMilliseconds(400),
            Description = new InstanceDescription("first-answer"),
        }.Start();
        using var link = LinkTo(app, callTimeout: TimeSpan.FromMilliseconds(150));
        (await link.GetDescriptionAsync(Ct)).ShouldBeNull();
        app.Description = new InstanceDescription("second-answer");
        await Task.Delay(500, Ct);

        // Act
        var description = await link.GetDescriptionAsync(Ct);

        // Assert
        description!.ServiceName.ShouldBe("second-answer");
    }

    [Fact]
    public async Task The_link_stops_when_asked_to()
    {
        // Arrange
        await using var app = new FakeApplication { Silent = true }.Start();
        using var link = LinkTo(app, callTimeout: TimeSpan.FromSeconds(30));
        using var cancelled = new CancellationTokenSource();
        var asking = link.GetHealthAsync(cancelled.Token);
        await Task.Delay(100, Ct);

        // Act
        await cancelled.CancelAsync();

        // Assert
        await Should.ThrowAsync<OperationCanceledException>(async () => await asking);
    }

    private static SocketLibraryLink LinkTo(FakeApplication app, TimeSpan? callTimeout = null) =>
        new(
            new LibraryLinkOptions { SocketPath = app.SocketPath, CallTimeout = callTimeout ?? TimeSpan.FromSeconds(5) },
            TimeProvider.System,
            NullLogger<SocketLibraryLink>.Instance);
}
