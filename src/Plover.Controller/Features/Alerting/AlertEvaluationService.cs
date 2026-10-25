using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plover.Controller.Features.SelfMonitoring;

namespace Plover.Controller.Features.Alerting;

/// <summary>Background service that evaluates the alert rules on a fixed interval.</summary>
/// <param name="evaluator">Runs the rules.</param>
/// <param name="options">Provides the evaluation interval.</param>
/// <param name="time">Clock for the interval.</param>
/// <param name="logger">Receives evaluation failures.</param>
/// <param name="telemetry">Told how long each run took; optional.</param>
internal sealed class AlertEvaluationService(
    IAlertEvaluator evaluator,
    AlertingOptions options,
    TimeProvider time,
    ILogger<AlertEvaluationService> logger,
    ISystemTelemetry? telemetry = null) : BackgroundService
{
    private PeriodicTimer? _timer;

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Created here rather than in ExecuteAsync, which the host may run on another thread: the interval
        // starts counting when the service starts, not when that thread gets going.
        _timer = new PeriodicTimer(options.EvaluationInterval, time);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    var started = time.GetTimestamp();
                    evaluator.Evaluate();
                    telemetry?.AlertsEvaluated(time.GetElapsedTime(started));
                }
                catch (Exception ex)
                {
                    // A failing rule must not silence all alerting; the next tick tries again.
                    logger.LogError(ex, "Evaluating the alert rules failed");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
    }
}
