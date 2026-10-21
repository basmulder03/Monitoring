using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Controller.Features.Dashboard;

/// <summary>Registers the dashboard slice.</summary>
public static class DashboardSlice
{
    /// <summary>
    /// Adds what the dashboard reads from. It needs the registry, the health roll-up and the alerting state, so
    /// <c>AddRegistry</c>, <c>AddLiveness</c>, <c>AddHealth</c> and <c>AddAlerting</c> must be called as well; <c>MapDashboard</c> adds the endpoints.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="DashboardOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An interval is not positive.</exception>
    /// <exception cref="ArgumentException">A label name to group by is blank or listed twice.</exception>
    public static IServiceCollection AddDashboard(this IServiceCollection services, Action<DashboardOptions>? configure = null)
    {
        var options = new DashboardOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.PushInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.KeepAliveInterval, TimeSpan.Zero);
        if (options.GroupBy.Any(string.IsNullOrWhiteSpace) || options.GroupBy.Distinct(StringComparer.Ordinal).Count() != options.GroupBy.Count)
        {
            throw new ArgumentException("The label names to group by must be non-blank and different from each other.", nameof(configure));
        }


        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<DashboardModel>();
        services.AddSingleton<OverviewStream>();
        return services;
    }
}
