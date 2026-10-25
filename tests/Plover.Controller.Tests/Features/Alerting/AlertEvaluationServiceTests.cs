using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Plover.Controller.Features.Alerting;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Alerting;

public class AlertEvaluationServiceTests
{
    [Fact]
    public async Task Service_evaluates_the_rules_every_interval()
    {
        // Arrange
        var fixture = new ServiceFixture().WithInterval(TimeSpan.FromSeconds(5));
        await fixture.StartAsync();

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.WaitForEvaluationsAsync(1);
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.WaitForEvaluationsAsync(2);

        // Assert
        fixture.Evaluator.Evaluations.ShouldBe(2);
    }

    [Fact]
    public async Task Service_does_not_evaluate_before_the_first_interval_has_passed()
    {
        // Arrange
        var fixture = new ServiceFixture().WithInterval(TimeSpan.FromSeconds(5));
        await fixture.StartAsync();

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(4));
        await Task.Delay(50, TestContext.Current.CancellationToken);

        // Assert
        fixture.Evaluator.Evaluations.ShouldBe(0);
    }

    [Fact]
    public async Task Service_keeps_evaluating_after_an_evaluation_failed()
    {
        // Arrange
        var fixture = new ServiceFixture().WithInterval(TimeSpan.FromSeconds(5));
        fixture.Evaluator.FailNext();
        await fixture.StartAsync();
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.WaitForEvaluationsAsync(1);

        // Act
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await fixture.WaitForEvaluationsAsync(2);

        // Assert
        fixture.Evaluator.Evaluations.ShouldBe(2);
    }

    [Fact]
    public async Task Service_stops_when_the_host_stops()
    {
        // Arrange
        var fixture = new ServiceFixture();
        await fixture.StartAsync();

        // Act
        var stop = async () => await fixture.Service.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        await stop.ShouldNotThrowAsync();
    }

    /// <summary>Builds the service over a counting evaluator and a fake clock.</summary>
    private sealed class ServiceFixture
    {
        private readonly AlertingOptions _options = new();

        public FakeTimeProvider Clock { get; } = new();

        public CountingEvaluator Evaluator { get; } = new();

        public AlertEvaluationService Service { get; private set; } = null!;

        public ServiceFixture WithInterval(TimeSpan interval)
        {
            _options.EvaluationInterval = interval;
            return this;
        }

        public async Task StartAsync()
        {
            Service = new AlertEvaluationService(Evaluator, _options, Clock, NullLogger<AlertEvaluationService>.Instance);
            await Service.StartAsync(TestContext.Current.CancellationToken);
        }

        public async Task WaitForEvaluationsAsync(int count)
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            guard.CancelAfter(TimeSpan.FromSeconds(5));
            while (Evaluator.Evaluations < count)
            {
                await Task.Delay(5, guard.Token);
            }
        }
    }

    /// <summary>Evaluator that counts its calls and can be told to fail once.</summary>
    private sealed class CountingEvaluator : IAlertEvaluator
    {
        private int _evaluations;
        private bool _failNext;

        public int Evaluations => Volatile.Read(ref _evaluations);

        public void FailNext() => _failNext = true;

        public void Evaluate()
        {
            Interlocked.Increment(ref _evaluations);
            if (_failNext)
            {
                _failNext = false;
                throw new InvalidOperationException("rule failed");
            }
        }
    }
}
