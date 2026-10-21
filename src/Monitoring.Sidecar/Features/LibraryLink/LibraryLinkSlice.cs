using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Monitoring.Sidecar.Features.LibraryLink;

/// <summary>Registers the library link slice.</summary>
public static class LibraryLinkSlice
{
    /// <summary>Adds the link to the application's monitoring client.</summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="LibraryLinkOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentException">The socket path is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The call timeout is not positive.</exception>
    public static IServiceCollection AddLibraryLink(this IServiceCollection services, Action<LibraryLinkOptions>? configure = null)
    {
        var options = new LibraryLinkOptions();
        configure?.Invoke(options);
        if (string.IsNullOrWhiteSpace(options.SocketPath))
        {
            throw new ArgumentException("The socket path must be set.", nameof(configure));
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.CallTimeout, TimeSpan.Zero);

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<ILibraryLink, SocketLibraryLink>();
        return services;
    }
}
