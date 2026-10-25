using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Plover.Common.Prometheus;
using Shouldly;
using Xunit;

namespace Plover.Common.Tests.Prometheus;

public class PrometheusTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly IReadOnlyDictionary<string, string> NoLabels = new Dictionary<string, string>();

    [Fact]
    public void Write_gives_each_name_one_type_line_and_makes_the_names_valid()
    {
        // Arrange
        PrometheusSample[] samples =
        [
            new("plover.agent.frames_sent", NoLabels, 12),
            new("http.requests", new Dictionary<string, string> { ["route"] = "/a" }, 1),
            new("http.requests", new Dictionary<string, string> { ["route"] = "/b" }, 2.5),
        ];

        // Act
        var text = PrometheusText.Write(samples);

        // Assert
        text.ShouldBe(
            "# TYPE plover_agent_frames_sent gauge\nplover_agent_frames_sent 12\n"
            + "# TYPE http_requests gauge\nhttp_requests{route=\"/a\"} 1\nhttp_requests{route=\"/b\"} 2.5\n");
    }

    [Fact]
    public void Write_escapes_label_values_and_spells_out_the_values_that_are_not_numbers()
    {
        // Arrange
        PrometheusSample[] samples =
        [
            new("m", new Dictionary<string, string> { ["a.b"] = "x\"y\\z\n" }, double.NaN),
            new("n", NoLabels, double.PositiveInfinity),
        ];

        // Act
        var text = PrometheusText.Write(samples);

        // Assert
        text.ShouldContain("m{a_b=\"x\\\"y\\\\z\\n\"} NaN\n");
        text.ShouldContain("n +Inf\n");
    }

    [Fact]
    public void Write_does_not_start_a_name_with_a_digit()
    {
        // Act
        var text = PrometheusText.Write([new PrometheusSample("9lives", NoLabels, 1)]);

        // Assert
        text.ShouldContain("_9lives 1\n");
    }

    [Fact]
    public async Task The_service_answers_a_scrape_with_the_current_values()
    {
        // Arrange
        var value = 1;
        await using var running = await Running(() => [new PrometheusSample("x.y", NoLabels, value)]);

        // Act
        value = 7;
        var response = await running.RequestAsync("GET /metrics HTTP/1.1");

        // Assert
        response.ShouldStartWith("HTTP/1.1 200 OK");
        response.ShouldContain("text/plain; version=0.0.4");
        response.ShouldEndWith("# TYPE x_y gauge\nx_y 7\n");
    }

    [Theory]
    [InlineData("GET /other HTTP/1.1", "404 Not Found")]
    [InlineData("POST /metrics HTTP/1.1", "405 Method Not Allowed")]
    [InlineData("nonsense", "400 Bad Request")]
    public async Task The_service_refuses_anything_but_a_scrape(string request, string status)
    {
        // Arrange
        await using var running = await Running(() => []);

        // Act
        var response = await running.RequestAsync(request);

        // Assert
        response.ShouldStartWith($"HTTP/1.1 {status}");
    }

    [Fact]
    public void Nothing_is_added_while_the_port_is_zero()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddPrometheus(_ => { }, _ => () => []);

        // Assert
        services.ShouldBeEmpty();
    }

    [Fact]
    public void An_address_that_is_not_an_ip_address_is_refused()
    {
        // Act
        var add = () => new ServiceCollection().AddPrometheus(o => { o.Port = 9100; o.Address = "localhost"; }, _ => () => []);

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    private static async Task<RunningService> Running(Func<IEnumerable<PrometheusSample>> source)
    {
        var service = new PrometheusService(new PrometheusOptions { Port = 0 }, source, NullLogger<PrometheusService>.Instance);
        await service.StartAsync(Ct);
        return new RunningService(service);
    }

    private sealed class RunningService(PrometheusService service) : IAsyncDisposable
    {
        public async Task<string> RequestAsync(string requestLine)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(service.LocalEndPoint!, Ct);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes($"{requestLine}\r\nHost: x\r\n\r\n"), Ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadToEndAsync(Ct);
        }

        public async ValueTask DisposeAsync()
        {
            await service.StopAsync(Ct);
            service.Dispose();
        }
    }
}
