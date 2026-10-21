using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Monitoring.Client.Features.Health;
using Monitoring.Client.Features.Info;
using Monitoring.Client.Features.LocalServer;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;
using HealthCheckResult = Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult;

namespace Monitoring.Client.Tests.Features.LocalServer;

public class PullHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HandleAsync_answers_an_info_pull_with_the_description_of_the_application()
    {
        // Arrange
        var handler = HandlerFixture.Build();

        // Act
        var result = await handler.HandleAsync(new Pull(7, PullKind.Info, 100), Ct);

        // Assert
        result.RequestId.ShouldBe(7);
        result.Kind.ShouldBe(PullKind.Info);
        result.Cursor.ShouldBe(100);
        ReportPayload.ReadDescription(result.PayloadJson)!.ServiceName.ShouldBe("orders");
    }

    [Fact]
    public async Task HandleAsync_answers_a_health_pull_with_the_health_checks_of_the_application()
    {
        // Arrange
        var handler = HandlerFixture.Build();

        // Act
        var result = await handler.HandleAsync(new Pull(8, PullKind.Health, 0), Ct);

        // Assert
        result.RequestId.ShouldBe(8);
        var report = ReportPayload.ReadHealth(result.PayloadJson)!;
        report.Health.ShouldBe(HealthState.Degraded);
        report.Checks.Single().Name.ShouldBe("cache");
    }

    [Theory]
    [InlineData(PullKind.Metrics)]
    [InlineData(PullKind.Spans)]
    [InlineData(PullKind.Edges)]
    public async Task HandleAsync_answers_kinds_of_data_it_does_not_provide_yet_with_nothing(PullKind kind)
    {
        // Arrange
        var handler = HandlerFixture.Build();

        // Act
        var result = await handler.HandleAsync(new Pull(9, kind, 5), Ct);

        // Assert
        result.PayloadJson.ShouldBeNull();
        result.RequestId.ShouldBe(9);
        result.Kind.ShouldBe(kind);
    }

    /// <summary>Builds a handler over an application named "orders" with one degraded check.</summary>
    private static class HandlerFixture
    {
        public static PullHandler Build()
        {
            var options = new MonitoringOptions { ServiceName = "orders", Version = "1.0.0" };
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddHealthChecks().AddCheck("cache", () => HealthCheckResult.Degraded("warming up"));
            services.AddSingleton(options);
            services.AddSingleton<IHostEnvironment>(new Environment());
            services.AddSingleton<InstanceDescriber>();
            services.AddSingleton<HealthReporter>();
            services.AddSingleton<IPullSource, InfoPullSource>();
            services.AddSingleton<IPullSource, HealthPullSource>();
            services.AddSingleton<PullHandler>();
            return services.BuildServiceProvider().GetRequiredService<PullHandler>();
        }
    }

    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";

        public string ApplicationName { get; set; } = "host-app";

        public string ContentRootPath { get; set; } = "/";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
