using Microsoft.Extensions.DependencyInjection;

namespace Monitoring.Controller.Features.Dashboard;

/// <summary>Registers the dashboard slice.</summary>
public static class DashboardSlice
{
    /// <summary>
    /// Adds what the dashboard reads from. It needs the registry, the health roll-up and the alerting state, so
    /// <c>AddRegistry</c>, <c>AddHealth</c> and <c>AddAlerting</c> must be called as well; <c>MapDashboard</c> adds the endpoints.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddDashboard(this IServiceCollection services)
    {
        services.AddSingleton<DashboardModel>();
        return services;
    }
}
