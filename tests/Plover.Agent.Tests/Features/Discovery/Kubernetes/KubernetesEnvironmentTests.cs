using Plover.Agent.Features.Discovery;
using Plover.Agent.Features.Discovery.Kubernetes;
using Shouldly;
using Xunit;

namespace Plover.Agent.Tests.Features.Discovery.Kubernetes;

public class KubernetesEnvironmentTests
{
    private const string Dir = "/var/run/secrets/kubernetes.io/serviceaccount";

    [Fact]
    public void Resolve_uses_the_configured_namespace()
    {
        // Arrange
        var pod = new PodEnvironment().InCluster().WithFile($"{Dir}/namespace", "from-pod");

        // Act
        var (target, _) = pod.Resolve(new KubernetesDiscoveryOptions { Namespace = "configured" });

        // Assert
        target.Namespace.ShouldBe("configured");
    }

    [Fact]
    public void Resolve_falls_back_to_the_namespace_of_the_pod()
    {
        // Arrange
        var pod = new PodEnvironment().InCluster().WithFile($"{Dir}/namespace", " from-pod\n");

        // Act
        var (target, _) = pod.Resolve(new KubernetesDiscoveryOptions());

        // Assert
        target.Namespace.ShouldBe("from-pod");
    }

    [Fact]
    public void Resolve_cannot_determine_the_namespace_outside_a_pod()
    {
        // Arrange
        var pod = new PodEnvironment().WithEnvironment("KUBERNETES_SERVICE_HOST", "10.0.0.1");

        // Act
        var resolve = () => { pod.Resolve(new KubernetesDiscoveryOptions()); };

        // Assert
        resolve.ShouldThrow<ArgumentException>().Message.ShouldContain("namespace");
    }

    [Fact]
    public void Resolve_uses_the_configured_api_server()
    {
        // Arrange
        var pod = new PodEnvironment().InCluster();

        // Act
        var (_, connection) = pod.Resolve(new KubernetesDiscoveryOptions { ApiServer = "http://127.0.0.1:8001" });

        // Assert
        connection.ApiServer.ShouldBe(new Uri("http://127.0.0.1:8001"));
    }

    [Theory]
    [InlineData("10.0.0.1", "443", "10.0.0.1", 443)]
    [InlineData("10.0.0.1", "6443", "10.0.0.1", 6443)]
    [InlineData("fd00::1", "443", "[fd00::1]", 443)]
    public void Resolve_builds_the_in_cluster_api_server_address_from_the_environment(string host, string port, string expectedHost, int expectedPort)
    {
        // Arrange
        var pod = new PodEnvironment().WithFile($"{Dir}/namespace", "shop")
            .WithEnvironment("KUBERNETES_SERVICE_HOST", host).WithEnvironment("KUBERNETES_SERVICE_PORT", port);

        // Act
        var (_, connection) = pod.Resolve(new KubernetesDiscoveryOptions());

        // Assert
        connection.ApiServer.Scheme.ShouldBe("https");
        connection.ApiServer.Host.ShouldBe(expectedHost);
        connection.ApiServer.Port.ShouldBe(expectedPort);
    }

    [Fact]
    public void Resolve_assumes_port_443_when_the_environment_has_no_port()
    {
        // Arrange
        var pod = new PodEnvironment().WithFile($"{Dir}/namespace", "shop").WithEnvironment("KUBERNETES_SERVICE_HOST", "10.0.0.1");

        // Act
        var (_, connection) = pod.Resolve(new KubernetesDiscoveryOptions());

        // Assert
        connection.ApiServer.Port.ShouldBe(443);
    }

    [Fact]
    public void Resolve_cannot_determine_the_api_server_outside_a_pod()
    {
        // Arrange
        var pod = new PodEnvironment().WithFile($"{Dir}/namespace", "shop");

        // Act
        var resolve = () => { pod.Resolve(new KubernetesDiscoveryOptions()); };

        // Assert
        resolve.ShouldThrow<ArgumentException>().Message.ShouldContain("API server");
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://server")]
    [InlineData("10.0.0.1:443")]
    public void Resolve_rejects_an_api_server_that_is_not_an_http_address(string server)
    {
        // Arrange
        var pod = new PodEnvironment().InCluster();

        // Act
        var resolve = () => { pod.Resolve(new KubernetesDiscoveryOptions { ApiServer = server }); };

        // Assert
        resolve.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Resolve_uses_the_service_account_token_and_certificate_authority_of_the_pod()
    {
        // Arrange
        var pod = new PodEnvironment().InCluster().WithFile($"{Dir}/token", "t").WithFile($"{Dir}/ca.crt", "pem");

        // Act
        var (_, connection) = pod.Resolve(new KubernetesDiscoveryOptions());

        // Assert
        connection.TokenFile.ShouldBe($"{Dir}/token");
        connection.CaFile.ShouldBe($"{Dir}/ca.crt");
        connection.Token.ShouldBeNull();
    }

    [Fact]
    public void Resolve_leaves_out_credentials_the_pod_does_not_have()
    {
        // Arrange
        var pod = new PodEnvironment().InCluster();

        // Act
        var (_, connection) = pod.Resolve(new KubernetesDiscoveryOptions());

        // Assert
        connection.TokenFile.ShouldBeNull();
        connection.CaFile.ShouldBeNull();
    }

    [Fact]
    public void Resolve_prefers_a_fixed_token_over_the_token_file_of_the_pod()
    {
        // Arrange
        var pod = new PodEnvironment().InCluster().WithFile($"{Dir}/token", "t");

        // Act
        var (_, connection) = pod.Resolve(new KubernetesDiscoveryOptions { Token = "dev-token" });

        // Assert
        connection.Token.ShouldBe("dev-token");
        connection.TokenFile.ShouldBeNull();
    }

    [Fact]
    public void Resolve_uses_a_configured_token_file_and_certificate_authority()
    {
        // Arrange
        var pod = new PodEnvironment().InCluster().WithFile($"{Dir}/token", "t");

        // Act
        var (_, connection) = pod.Resolve(new KubernetesDiscoveryOptions { TokenFile = "/my/token", CaFile = "/my/ca.crt" });

        // Assert
        connection.TokenFile.ShouldBe("/my/token");
        connection.CaFile.ShouldBe("/my/ca.crt");
    }

    [Fact]
    public void Resolve_does_not_use_a_certificate_authority_for_a_plain_http_api_server()
    {
        // Arrange
        var pod = new PodEnvironment().InCluster().WithFile($"{Dir}/ca.crt", "pem");

        // Act
        var (_, connection) = pod.Resolve(new KubernetesDiscoveryOptions { ApiServer = "http://127.0.0.1:8001" });

        // Assert
        connection.CaFile.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_treats_blank_options_as_not_set(string blank)
    {
        // Arrange
        var pod = new PodEnvironment().InCluster().WithFile($"{Dir}/namespace", "from-pod");

        // Act
        var (target, _) = pod.Resolve(new KubernetesDiscoveryOptions { Namespace = blank, ApiServer = blank, Token = blank });

        // Assert
        target.Namespace.ShouldBe("from-pod");
    }

    /// <summary>A pretend pod environment: environment variables and files the agent can see.</summary>
    private sealed class PodEnvironment
    {
        private readonly Dictionary<string, string> _variables = [];
        private readonly Dictionary<string, string> _files = [];

        public PodEnvironment WithEnvironment(string name, string value)
        {
            _variables[name] = value;
            return this;
        }

        public PodEnvironment WithFile(string path, string content)
        {
            _files[path] = content;
            return this;
        }

        /// <summary>Adds the API server address and namespace that Kubernetes gives every pod.</summary>
        public PodEnvironment InCluster() =>
            WithEnvironment("KUBERNETES_SERVICE_HOST", "10.0.0.1").WithFile($"{Dir}/namespace", "shop");

        public (DiscoveryTarget Target, KubernetesConnection Connection) Resolve(KubernetesDiscoveryOptions options) =>
            KubernetesEnvironment.Resolve(
                options,
                name => _variables.GetValueOrDefault(name),
                path => _files.GetValueOrDefault(path));
    }
}
