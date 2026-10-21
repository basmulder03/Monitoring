using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Hosting;
using Shouldly;
using System.Net.Sockets;
using Xunit;

namespace Monitoring.Hosting.Tests;

public class MonitoringHostTests
{
    [Fact]
    public void CreateBuilder_reads_settings_from_the_command_line()
    {
        // Arrange
        string[] args = ["--Hosting:Test:FromArgs=fromArgs"];

        // Act
        var builder = MonitoringHost.CreateBuilder(args, configFile: NoFile());

        // Assert
        builder.Configuration["Hosting:Test:FromArgs"].ShouldBe("fromArgs");
    }

    [Fact]
    public void CreateBuilder_lets_overrides_win_over_the_command_line()
    {
        // Arrange
        string[] args = ["--Hosting:Test:Both=args"];
        var overrides = new Dictionary<string, string?> { ["Hosting:Test:Both"] = "override" };

        // Act
        var builder = MonitoringHost.CreateBuilder(args, overrides, NoFile());

        // Assert
        builder.Configuration["Hosting:Test:Both"].ShouldBe("override");
    }

    [Fact]
    public void CreateBuilder_reads_the_json_file_including_comments()
    {
        // Arrange
        var file = WriteFile("{ // a comment\n  \"Hosting\": { \"Test\": { \"FromFile\": \"fromFile\" } } }");

        // Act
        var builder = MonitoringHost.CreateBuilder([], configFile: file);

        // Assert
        builder.Configuration["Hosting:Test:FromFile"].ShouldBe("fromFile");
    }

    [Fact]
    public void CreateBuilder_lets_the_command_line_win_over_the_file()
    {
        // Arrange
        var file = WriteFile("""{ "Hosting": { "Test": { "Both": "file" } } }""");
        string[] args = ["--Hosting:Test:Both=args"];

        // Act
        var builder = MonitoringHost.CreateBuilder(args, configFile: file);

        // Assert
        builder.Configuration["Hosting:Test:Both"].ShouldBe("args");
    }

    [Fact]
    public void CreateBuilder_reads_environment_variables_with_the_monitoring_prefix()
    {
        // Arrange: a unique name keeps this safe next to other tests
        var name = $"Hosting_Test_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable($"MONITORING_{name}", "fromEnvironment");

        try
        {
            // Act
            var builder = MonitoringHost.CreateBuilder([], configFile: NoFile());

            // Assert
            builder.Configuration[name].ShouldBe("fromEnvironment");
        }
        finally
        {
            Environment.SetEnvironmentVariable($"MONITORING_{name}", null);
        }
    }

    [Fact]
    public void CreateBuilder_works_without_a_configuration_file()
    {
        // Arrange: the file is optional

        // Act
        var create = () => MonitoringHost.CreateBuilder([], configFile: NoFile());

        // Assert
        create.ShouldNotThrow();
    }

    [Fact]
    public void Configure_gives_any_host_builder_the_standard_sources()
    {
        // Arrange
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        var overrides = new Dictionary<string, string?> { ["Hosting:Test:Configured"] = "yes" };

        // Act
        MonitoringHost.Configure(builder, [], overrides, NoFile());

        // Assert
        builder.Configuration["Hosting:Test:Configured"].ShouldBe("yes");
    }

    [Fact]
    public async Task RunAsync_with_a_host_factory_returns_zero_after_a_normal_stop()
    {
        // Arrange
        IHost Build()
        {
            var builder = MonitoringHost.CreateBuilder([], configFile: NoFile());
            builder.Services.AddHostedService<StopImmediately>();
            return builder.Build();
        }

        // Act
        var exitCode = await MonitoringHost.RunAsync(Build);

        // Assert
        exitCode.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_with_a_host_factory_reports_a_configuration_problem_with_its_own_exit_code()
    {
        // Arrange
        IHost Build() => throw new ConfigurationException("Setting 'X' is required.");

        // Act
        var exitCode = await MonitoringHost.RunAsync(Build);

        // Assert
        exitCode.ShouldBe(MonitoringHost.ConfigurationError);
    }

    [Fact]
    public async Task RunAsync_returns_zero_after_the_host_stopped_normally()
    {
        // Arrange
        void Compose(HostApplicationBuilder builder, SettingsReader settings) =>
            builder.Services.AddHostedService<StopImmediately>();

        // Act
        var exitCode = await MonitoringHost.RunAsync([], Compose);

        // Assert
        exitCode.ShouldBe(0);
    }

    [Fact]
    public async Task RunAsync_reports_a_configuration_problem_with_its_own_exit_code()
    {
        // Arrange
        void Compose(HostApplicationBuilder builder, SettingsReader settings) => settings.Required("Hosting:Test:Missing");

        // Act
        var exitCode = await MonitoringHost.RunAsync([], Compose);

        // Assert
        exitCode.ShouldBe(MonitoringHost.ConfigurationError);
    }

    [Fact]
    public async Task RunAsync_reports_an_invalid_argument_as_a_configuration_problem()
    {
        // Arrange
        void Compose(HostApplicationBuilder builder, SettingsReader settings) => throw new ArgumentOutOfRangeException("port");

        // Act
        var exitCode = await MonitoringHost.RunAsync([], Compose);

        // Assert
        exitCode.ShouldBe(MonitoringHost.ConfigurationError);
    }

    [Fact]
    public async Task RunAsync_reports_a_failure_to_bind_as_a_startup_failure()
    {
        // Arrange
        void Compose(HostApplicationBuilder builder, SettingsReader settings) =>
            builder.Services.AddHostedService<FailToBind>();

        // Act
        var exitCode = await MonitoringHost.RunAsync([], Compose);

        // Assert
        exitCode.ShouldBe(MonitoringHost.StartupFailure);
    }

    private static string NoFile() => Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");

    private static string WriteFile(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"monitoring-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    /// <summary>Hosted service that asks the host to stop as soon as it starts.</summary>
    private sealed class StopImmediately(IHostApplicationLifetime lifetime) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            lifetime.StopApplication();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Hosted service that fails the way a port that is already in use does.</summary>
    private sealed class FailToBind : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) =>
            throw new SocketException((int)SocketError.AddressAlreadyInUse);

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
