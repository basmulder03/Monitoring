using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Plover.Hosting;

/// <summary>The way every monitoring executable is started: the same configuration sources, logging and exit codes.</summary>
public static class PloverHost
{
    /// <summary>Environment variable that names the configuration file; defaults to <see cref="DefaultConfigFile"/>.</summary>
    public const string ConfigFileVariable = "PLOVER_CONFIG_FILE";

    /// <summary>Configuration file read from the current directory when no other is named, or from the folder of the program when it runs as a Windows service, whose current directory is the system folder. It is optional.</summary>
    public const string DefaultConfigFile = "plover.json";

    /// <summary>Exit code for a configuration problem, as in sysexits.h.</summary>
    public const int ConfigurationError = 78;

    /// <summary>Exit code for a failure to start, such as a port that is already in use.</summary>
    public const int StartupFailure = 1;

    /// <summary>
    /// Adds the standard configuration sources and console logging to a host builder, whatever kind of host it builds.
    /// From lowest to highest priority the sources are: the JSON file, environment variables starting with <c>PLOVER_</c>,
    /// the command line, and finally the given overrides.
    /// </summary>
    /// <param name="builder">The builder to set up.</param>
    /// <param name="args">Command line arguments, such as <c>--Controller:Listen:Port=9441</c>.</param>
    /// <param name="overrides">Settings that win over every other source; meant for tests.</param>
    /// <param name="configFile">The configuration file; defaults to the file named by <see cref="ConfigFileVariable"/>, else <see cref="DefaultConfigFile"/>.</param>
    public static void Configure(
        IHostApplicationBuilder builder,
        string[] args,
        IEnumerable<KeyValuePair<string, string?>>? overrides = null,
        string? configFile = null)
    {
        var file = configFile
            ?? (Environment.GetEnvironmentVariable(ConfigFileVariable) is { Length: > 0 } named ? named : DefaultConfigFilePath());
        builder.Configuration.AddJsonFile(Path.GetFullPath(file), optional: true, reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables("PLOVER_");
        builder.Configuration.AddCommandLine(args);
        if (overrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        // A builder may come with console logging of its own; one console logger is enough.
        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        builder.Logging.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
    }

    /// <summary>A service has no console and starts in the system folder, so what is next to the program is the only place its file can be.</summary>
    private static string DefaultConfigFilePath() =>
        OperatingSystem.IsWindows() && !Environment.UserInteractive ? Path.Combine(AppContext.BaseDirectory, DefaultConfigFile) : DefaultConfigFile;

    /// <summary>Creates a plain host builder (no web server) set up with <see cref="Configure"/>.</summary>
    /// <param name="args">Command line arguments, such as <c>--Agent:ScopeName=shop</c>.</param>
    /// <param name="overrides">Settings that win over every other source; meant for tests.</param>
    /// <param name="configFile">The configuration file; defaults to the file named by <see cref="ConfigFileVariable"/>, else <see cref="DefaultConfigFile"/>.</param>
    /// <returns>The builder, with logging to the console already set up.</returns>
    public static HostApplicationBuilder CreateBuilder(
        string[] args, IEnumerable<KeyValuePair<string, string?>>? overrides = null, string? configFile = null)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = args });
        Configure(builder, args, overrides, configFile);
        return builder;
    }

    /// <summary>Builds the host from the configuration and runs it until it is told to stop.</summary>
    /// <param name="args">Command line arguments.</param>
    /// <param name="compose">Adds the executable's services, reading its settings.</param>
    /// <returns>0 after a normal stop, <see cref="ConfigurationError"/> for a configuration problem, or <see cref="StartupFailure"/> when it could not start.</returns>
    public static Task<int> RunAsync(string[] args, Action<HostApplicationBuilder, SettingsReader> compose)
    {
        var (rest, check) = SplitCheck(args);
        return RunAsync(() =>
        {
            var builder = CreateBuilder(rest);
            compose(builder, new SettingsReader(builder.Configuration));
            return builder.Build();
        }, check);
    }

    /// <summary>The switch that makes an executable only check its configuration and exit, so a change can be tried before it is rolled out.</summary>
    public const string CheckSwitch = "--check";

    /// <summary>Takes the check switch out of the arguments, because the command line provider cannot read a switch without a value.</summary>
    /// <param name="args">The arguments of the executable.</param>
    /// <returns>The arguments without the switch, and whether it was there.</returns>
    public static (string[] Remaining, bool Check) SplitCheck(string[] args) =>
        ([.. args.Where(arg => arg != CheckSwitch)], args.Contains(CheckSwitch));

    /// <summary>Builds a host and runs it until it is told to stop, turning start-up problems into exit codes.</summary>
    /// <param name="build">Creates the host; may throw <see cref="ConfigurationException"/> or <see cref="ArgumentException"/> for a bad configuration.</param>
    /// <param name="check">Only build the host and check the configuration, then exit; see <see cref="CheckSwitch"/>.</param>
    /// <returns>0 after a normal stop, <see cref="ConfigurationError"/> for a configuration problem, or <see cref="StartupFailure"/> when it could not start.</returns>
    public static async Task<int> RunAsync(Func<IHost> build, bool check = false)
    {
        try
        {
            using var host = build();
            if (check)
            {
                // Built without starting: reading every setting, the tokens and the certificates has already happened. What is looked at while running is checked now.
                var problems = host.Services.GetServices<IConfigurationCheck>().SelectMany(c => c.Problems()).ToList();
                foreach (var problem in problems)
                {
                    await Console.Error.WriteLineAsync($"Configuration error: {problem}").ConfigureAwait(false);
                }

                if (problems.Count > 0)
                {
                    return ConfigurationError;
                }

                await Console.Out.WriteLineAsync("The configuration is valid.").ConfigureAwait(false);
                return 0;
            }

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
