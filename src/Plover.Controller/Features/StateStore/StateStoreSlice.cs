using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Plover.Controller.Features.StateStore;

/// <summary>Settings of the state store.</summary>
public sealed class StateStoreOptions
{
    /// <summary>The SQLite file to keep the state in; <see langword="null"/> keeps it in memory.</summary>
    public string? SqlitePath { get; set; }
}

/// <summary>Registers the state store.</summary>
public static class StateStoreSlice
{
    /// <summary>Adds the store: in memory unless a SQLite file is given.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally sets the <see cref="StateStoreOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddStateStore(this IServiceCollection services, Action<StateStoreOptions>? configure = null)
    {
        var options = new StateStoreOptions();
        configure?.Invoke(options);

        services.AddLogging();
        if (string.IsNullOrWhiteSpace(options.SqlitePath))
        {
            services.TryAddSingleton<IStateStore, MemoryStateStore>();
        }
        else
        {
            services.TryAddSingleton<IStateStore>(provider =>
            {
                var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Plover.Controller.StateStore");
                return new SqliteStateStore(options.SqlitePath, message => logger.LogWarning("{Message}", message));
            });
        }

        return services;
    }
}
