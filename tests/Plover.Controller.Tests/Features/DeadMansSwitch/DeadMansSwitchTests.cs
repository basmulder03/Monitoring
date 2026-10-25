using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Plover.Controller.Features.DeadMansSwitch;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.DeadMansSwitch;

public class DeadMansSwitchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Service_pings_at_startup_and_then_every_interval()
    {
        // Arrange
        var handler = new StubHandler();
        var clock = new FakeTimeProvider();
        using var service = Service(handler, clock);

        // Act
        await service.StartAsync(Ct);
        await handler.WaitForPingsAsync(1);
        clock.Advance(TimeSpan.FromMinutes(1));
        await handler.WaitForPingsAsync(2);
        await service.StopAsync(Ct);

        // Assert
        handler.Requests.ShouldAllBe(uri => uri == new Uri("https://watchdog.example/ping"));
    }

    [Fact]
    public async Task Service_keeps_pinging_after_a_ping_failed()
    {
        // Arrange
        var handler = new StubHandler { Fail = true };
        var clock = new FakeTimeProvider();
        using var service = Service(handler, clock);

        // Act
        await service.StartAsync(Ct);
        await handler.WaitForPingsAsync(1);
        clock.Advance(TimeSpan.FromMinutes(1));
        await handler.WaitForPingsAsync(2);
        await service.StopAsync(Ct);

        // Assert
        handler.Requests.Count.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Nothing_is_added_without_an_address()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDeadMansSwitch(_ => { });

        // Assert
        services.ShouldBeEmpty();
    }

    [Fact]
    public void An_address_that_is_not_http_is_refused()
    {
        // Act
        var add = () => new ServiceCollection().AddDeadMansSwitch(o => o.Url = new Uri("ftp://watchdog.example"));

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    private static PingService Service(StubHandler handler, TimeProvider clock) =>
        new(new HttpClient(handler), new DeadMansSwitchOptions { Url = new Uri("https://watchdog.example/ping") }, clock, NullLogger<PingService>.Instance);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly List<Uri> _requests = [];

        public bool Fail { get; init; }

        public IReadOnlyList<Uri> Requests
        {
            get { lock (_requests) { return [.. _requests]; } }
        }

        public async Task WaitForPingsAsync(int count)
        {
            for (var i = 0; i < 200 && Requests.Count < count; i++)
            {
                await Task.Delay(10, Ct);
            }

            Requests.Count.ShouldBeGreaterThanOrEqualTo(count);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_requests) { _requests.Add(request.RequestUri!); }
            return Fail ? throw new HttpRequestException("down") : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
