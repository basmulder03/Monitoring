using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Plover.Controller.Features.DeadMansSwitch;

/// <summary>Registers the dead-man's switch slice.</summary>
public static class DeadMansSwitchSlice
{
    /// <summary>Adds the ping to an external watchdog. Nothing is added unless an address is configured.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Changes the <see cref="DeadMansSwitchOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The interval is not positive.</exception>
    /// <exception cref="ArgumentException">The address is not an absolute http or https address.</exception>
    public static IServiceCollection AddDeadMansSwitch(this IServiceCollection services, Action<DeadMansSwitchOptions> configure)
    {
        var options = new DeadMansSwitchOptions();
        configure(options);
        if (options.Url is null)
        {
            return services;
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Interval, TimeSpan.Zero);
        if (!options.Url.IsAbsoluteUri || options.Url.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"The address of the watchdog must be an absolute http or https address, but is '{options.Url}'.", nameof(configure));
        }

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddHostedService(provider => new PingService(
            new HttpClient { Timeout = TimeSpan.FromSeconds(10) }, options, provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<PingService>>()));
        return services;
    }
}
