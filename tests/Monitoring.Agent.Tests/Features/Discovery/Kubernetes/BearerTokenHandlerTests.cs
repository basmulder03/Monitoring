using System.Net;
using Monitoring.Agent.Features.Discovery.Kubernetes;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.Discovery.Kubernetes;

public class BearerTokenHandlerTests
{
    [Fact]
    public async Task SendAsync_adds_the_token_as_a_bearer_credential()
    {
        // Arrange
        var inner = new RecordingHandler();
        using var client = new HttpClient(new BearerTokenHandler(() => "abc") { InnerHandler = inner });

        // Act
        await client.GetAsync("http://kubernetes.test/", TestContext.Current.CancellationToken);

        // Assert
        inner.Authorization.ShouldBe("Bearer abc");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task SendAsync_sends_no_credential_when_there_is_no_token(string? token)
    {
        // Arrange
        var inner = new RecordingHandler();
        using var client = new HttpClient(new BearerTokenHandler(() => token) { InnerHandler = inner });

        // Act
        await client.GetAsync("http://kubernetes.test/", TestContext.Current.CancellationToken);

        // Assert
        inner.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task SendAsync_reads_the_token_again_for_every_request_because_it_rotates()
    {
        // Arrange
        var token = "first";
        var inner = new RecordingHandler();
        using var client = new HttpClient(new BearerTokenHandler(() => token) { InnerHandler = inner });
        await client.GetAsync("http://kubernetes.test/", TestContext.Current.CancellationToken);

        // Act
        token = "second";
        await client.GetAsync("http://kubernetes.test/", TestContext.Current.CancellationToken);

        // Assert
        inner.Authorization.ShouldBe("Bearer second");
    }

    /// <summary>Handler that remembers the credential of the last request.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
