using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Monitoring.Hosting;

/// <summary>The way every monitoring executable is started: the same configuration sources, logging and exit codes.</summary>
public static class MonitoringHost
{
    /// <summary>Environment variable that names the configuration file; defaults to <see cref="DefaultConfigFile"/>.</summary>
    public const string ConfigFileVariable = "MONITORING_CONFIG_FILE";

    /// <summary>Configuration file read from the current directory when no other is named. It is optional.</summary>
    public const string DefaultConfigFile = "monitoring.json";

    /// <summary>Exit code for a configuration problem, as in sysexits.h.</summary>
    public const int ConfigurationError = 78;

    /// <summary>Exit code for a failure to start, such as a port that is already in use.</summary>
    public const int StartupFailure = 1;

    /// <summary>
    /// Creates a host builder with the standard configuration sources, from lowest to highest priority: the JSON file,
    /// environment variables starting with <c>MONITORING_</c>, the command line, and finally the given overrides.
    /// </summary>
    /// <param name="args">Command line arguments, such as <c>--Controller:Listen:Port=9441</c>.</param>
    /// <param name="overrides">Settings that win over every other source; meant for tests.</param>
    /// <param name="configFile">The configuration file; defaults to the file named by <see cref="ConfigFileVariable"/>, else <see cref="DefaultConfigFile"/>.</param>
    /// <returns>The builder, with logging to the console already set up.</returns>
    public static HostApplicationBuilder CreateBuilder(
        string[] args, IEnumerable<KeyValuePair<string, string?>>? overrides = null, string? configFile = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = args });

        var file = configFile
            ?? (Environment.GetEnvironmentVariable(ConfigFileVariable) is { Length: > 0 } named ? named : DefaultConfigFile);
        builder.Configuration.AddJsonFile(Path.GetFullPath(file), optional: true, reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables("MONITORING_");
        builder.Configuration.AddCommandLine(args);
        if (overrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        return builder;
    }

    /// <summary>Builds the host from the configuration and runs it until it is told to stop.</summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="compose">Adds the executable's services, reading its settings.</param>
    /// <returns>0 after a normal stop, <see cref="ConfigurationError"/> for a configuration problem, or <see cref="StartupFailure"/> when it could not start.</returns>
    public static async Task<int> RunAsync(string[] args, Action<HostApplicationBuilder, SettingsReader> compose)
    {
        try
        {
            var builder = CreateBuilder(args);
            compose(builder, new SettingsReader(builder.Configuration));
            using var host = builder.Build();
            await host.RunAsync().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is ConfigurationException or ArgumentException)
        {
            await Console.Error.WriteLineAsync($"Configuration error: {ex.Message}").ConfigureAwait(false);
            return ConfigurationError;
        }
        catch (SocketException ex)
        {
            await Console.Error.WriteLineAsync($"Failed to start: {ex.Message}").ConfigureAwait(false);
            return StartupFailure;
        }
    }
}
