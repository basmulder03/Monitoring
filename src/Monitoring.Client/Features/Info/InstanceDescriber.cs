using System.Reflection;
using Microsoft.Extensions.Hosting;
using Monitoring.Client.Features.LocalServer;
using Monitoring.Protocol.Reports;

namespace Monitoring.Client.Features.Info;

/// <summary>Works out how the application introduces itself: its service name and version.</summary>
/// <param name="options">Explicit settings, which win over the defaults.</param>
/// <param name="environment">Provides the application name.</param>
internal sealed class InstanceDescriber(MonitoringOptions options, IHostEnvironment environment)
{
    /// <summary>Describes the application.</summary>
    /// <returns>The service name and version.</returns>
    public InstanceDescription Describe() =>
        new(
            string.IsNullOrWhiteSpace(options.ServiceName) ? environment.ApplicationName : options.ServiceName,
            string.IsNullOrWhiteSpace(options.Version) ? EntryAssemblyVersion() : options.Version);

    private static string? EntryAssemblyVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // Build tooling appends the commit after a plus sign; the version is what comes before it.
        return version is null ? null : version.Split('+')[0];
    }
}
