using Monitoring.Agent.Features.Discovery.Kubernetes;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.Discovery.Kubernetes;

public class KubernetesHttpClientTests
{
    private static readonly Uri Server = new("https://10.0.0.1:443");

    [Fact]
    public void Create_points_the_client_at_the_api_server_without_an_overall_timeout()
    {
        // Arrange
        var connection = new KubernetesConnection(Server);

        // Act
        using var client = KubernetesHttpClient.Create(connection);

        // Assert
        client.BaseAddress.ShouldBe(Server);
        client.Timeout.ShouldBe(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public void ReadToken_returns_the_fixed_token_when_there_is_no_token_file()
    {
        // Arrange
        var connection = new KubernetesConnection(Server, Token: "fixed");

        // Act
        var token = KubernetesHttpClient.ReadToken(connection);

        // Assert
        token.ShouldBe("fixed");
    }

    [Fact]
    public void ReadToken_reads_and_trims_the_token_file()
    {
        // Arrange
        var file = TempFile("  from-file \n");
        var connection = new KubernetesConnection(Server, TokenFile: file, Token: "ignored");

        // Act
        var token = KubernetesHttpClient.ReadToken(connection);

        // Assert
        token.ShouldBe("from-file");
    }

    [Fact]
    public void ReadToken_follows_a_rotated_token_file()
    {
        // Arrange
        var file = TempFile("first");
        var connection = new KubernetesConnection(Server, TokenFile: file);
        KubernetesHttpClient.ReadToken(connection);

        // Act
        File.WriteAllText(file, "second");
        var token = KubernetesHttpClient.ReadToken(connection);

        // Assert
        token.ShouldBe("second");
    }

    [Fact]
    public void ReadToken_returns_nothing_when_the_token_file_cannot_be_read()
    {
        // Arrange
        var connection = new KubernetesConnection(Server, TokenFile: Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"));

        // Act
        var token = KubernetesHttpClient.ReadToken(connection);

        // Assert
        token.ShouldBeNull();
    }

    private static string TempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"token-{Guid.NewGuid():N}");
        File.WriteAllText(path, content);
        return path;
    }
}
