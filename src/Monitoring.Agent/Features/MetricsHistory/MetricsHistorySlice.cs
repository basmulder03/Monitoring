using Microsoft.Extensions.DependencyInjection;

namespace Monitoring.Agent.Features.MetricsHistory;

/// <summary>Registers the metrics history slice.</summary>
public static class MetricsHistorySlice
{
    /// <summary>Adds the history. Add it before the slices that record into it and answer from it.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="MetricsHistoryOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A size is not positive.</exception>
    public static IServiceCollection AddMetricsHistory(this IServiceCollection services, Action<MetricsHistoryOptions>? configure = null)
    {
        var options = new MetricsHistoryOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Samples, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxSeriesPerInstance, 1);

        services.AddSingleton(options);
        services.AddSingleton<IMetricsHistory, RingMetricsHistory>();
        return services;
    }
}
