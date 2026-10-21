using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Dashboard;
using Monitoring.Controller.Features.Health;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Dashboard;

public class OverviewStreamTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stream_is_served_as_server_sent_events()
    {
        // Arrange
        await using var stream = await new StreamFixture().StartAsync();

        // Act
        await stream.NextEventAsync();

        // Assert
        stream.ContentType.ShouldBe("text/event-stream");
    }

    [Fact]
    public async Task Stream_sends_the_current_overview_at_once()
    {
        // Arrange
        await using var stream = await new StreamFixture().WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Degraded).StartAsync();

        // Act
        var first = await stream.NextEventAsync();

        // Assert
        first.Name.ShouldBe("overview");
        using var json = JsonDocument.Parse(first.Data);
        var scope = json.RootElement.GetProperty("scopes")[0];
        scope.GetProperty("name").GetString().ShouldBe("shop");
        scope.GetProperty("health").GetString().ShouldBe("Degraded");
    }

    [Fact]
    public async Task Stream_pushes_a_new_overview_when_the_state_changed()
    {
        // Arrange
        var fixture = new StreamFixture().WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Healthy);
        await using var stream = await fixture.StartAsync();
        await stream.NextEventAsync();

        // Act
        fixture.Registry.WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Unhealthy);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await stream.NextEventAsync();

        // Assert
        using var json = JsonDocument.Parse(second.Data);
        json.RootElement.GetProperty("scopes")[0].GetProperty("health").GetString().ShouldBe("Unhealthy");
    }

    [Fact]
    public async Task Stream_sends_no_overview_while_the_state_stays_the_same_but_keeps_the_connection_alive()
    {
        // Arrange
        var fixture = new StreamFixture().WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Healthy);
        await using var stream = await fixture.StartAsync();
        await stream.NextEventAsync();

        // Act: several checks pass without a change, then the keep-alive interval
        for (var i = 0; i < 3; i++)
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        var next = await stream.NextEventAsync();

        // Assert: the first thing after the first overview is the keep-alive event, not a repeat of the overview
        next.Name.ShouldBe("keep-alive");
    }

    /// <summary>Starts the dashboard in memory with a fake clock and keeps a streaming request open against it.</summary>
    private sealed class StreamFixture
    {
        public FakeTimeProvider Clock { get; } = new();

        public FakeScopeRegistry Registry { get; } = new();

        public StreamFixture WithInstance(string scope, string service, InstanceLifecycle lifecycle, HealthState health)
        {
            Registry.WithInstance(scope, service, lifecycle, health);
            return this;
        }

        public async Task<OpenStream> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<TimeProvider>(Clock);
            builder.Services.AddSingleton<IScopeRegistry>(Registry);
            builder.Services.AddSingleton<IHealthRollup, HealthRollup>();
            builder.Services.AddSingleton<IAlertState>(new NoAlerts());
            builder.Services.AddDashboard();

            var app = builder.Build();
            app.MapDashboard();
            await app.StartAsync(Ct);

            var client = app.GetTestClient();
            var response = await client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, Ct);
            var reader = new StreamReader(await response.Content.ReadAsStreamAsync(Ct));
            return new OpenStream(app, client, response, reader);
        }
    }

    /// <summary>One event read from the stream.</summary>
    private sealed record StreamItem(string Name, string Data);

    /// <summary>A streaming request that is open against the in-memory dashboard.</summary>
    private sealed class OpenStream(WebApplication app, HttpClient client, HttpResponseMessage response, StreamReader reader) : IAsyncDisposable
    {
        public string? ContentType => response.Content.Headers.ContentType?.MediaType;

        /// <summary>Reads lines up to the next blank line. Fails instead of hanging when nothing arrives.</summary>
        public async Task<StreamItem> NextEventAsync()
        {
            using var guard = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            guard.CancelAfter(TimeSpan.FromSeconds(5));

            string name = "", data = "";
            while (await reader.ReadLineAsync(guard.Token) is { } line)
            {
                if (line.Length == 0)
                {
                    return new StreamItem(name, data);
                }

                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    name = line["event: ".Length..];
                }
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                {
                    data = line["data: ".Length..];
                }
            }

            throw new InvalidOperationException("The stream ended.");
        }

        public async ValueTask DisposeAsync()
        {
            reader.Dispose();
            response.Dispose();
            client.Dispose();
            await app.StopAsync(Ct);
            await app.DisposeAsync();
        }
    }

    private sealed class NoAlerts : IAlertState
    {
        public IReadOnlyList<Alert> GetActiveAlerts() => [];
    }
}
