using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plover.Controller.Features.Configuration;

/// <summary>Reads the central configuration file when the controller starts and again whenever its content changes.</summary>
/// <remarks>
/// A file that cannot be read or checked is rejected and the configuration in use stays in force; at startup that means the built-in defaults,
/// and the controller keeps trying, so a fixed file is picked up without a restart.
/// </remarks>
/// <param name="configuration">Receives each configuration that was accepted.</param>
/// <param name="options">The file and how often to look at it.</param>
/// <param name="time">Clock for the interval.</param>
/// <param name="logger">Receives what was rejected and why.</param>
internal sealed class ConfigurationReloadService(
    EffectiveConfiguration configuration, ConfigurationOptions options, TimeProvider time, ILogger<ConfigurationReloadService> logger) : BackgroundService
{
    private PeriodicTimer? _timer;
    private string? _lastRejected;

    /// <summary>Loads the file once before anything else starts, so no agent is served the defaults while a file exists.</summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes once the first load has been tried.</returns>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        Load();
        _timer = new PeriodicTimer(options.PollInterval, time);
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
        if (options.File is null)
        {
            return;
        }

        try
        {
            while (await _timer!.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                Load();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
    }

    /// <summary>Reads the file and applies it when it is valid. Never throws.</summary>
    internal void Load()
    {
        if (options.File is null)
        {
            return;
        }

        try
        {
            var text = File.ReadAllText(options.File);
            configuration.Replace(ParsedConfiguration.Parse(text));
            _lastRejected = null;
        }
        catch (Exception ex) when (ex is ConfigurationRejectedException or IOException or UnauthorizedAccessException)
        {
            // Said once per distinct problem, not at every look at the file.
            if (_lastRejected != ex.Message)
            {
                _lastRejected = ex.Message;
                logger.LogError("The central configuration '{File}' is not used: {Reason}. What was in force stays in force.", options.File, ex.Message);
            }
        }
    }
}
