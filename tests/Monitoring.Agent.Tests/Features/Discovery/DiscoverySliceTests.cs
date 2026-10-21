using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Monitoring.Agent.Features.Discovery;
using Monitoring.Agent.Features.ScopeState;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.Discovery;

public class DiscoverySliceTests
{
    private static void Kubernetes(DiscoveryOptions options)
    {
        options.Kind = DiscoveryKind.Kubernetes;
        options.Kubernetes.Namespace = "shop";
        options.Kubernetes.ApiServer = "http://127.0.0.1:8001";
    }

    [Fact]
    public void AddDiscovery_registers_nothing_by_default()
    {
        // Arrange
        using var provider = new ServiceCollection().AddScopeState(o => o.ScopeName = "shop").AddDiscovery(_ => { }).BuildServiceProvider();

        // Act
        var hosted = provider.GetServices<IHostedService>();

        // Assert
        hosted.ShouldBeEmpty();
    }

    [Fact]
    public void AddDiscovery_runs_the_kubernetes_discovery_in_the_background()
    {
        // Arrange
        using var provider = new ServiceCollection().AddScopeState(o => o.ScopeName = "shop").AddDiscovery(Kubernetes).BuildServiceProvider();

        // Act
        var hosted = provider.GetServices<IHostedService>().Select(s => s.GetType().Name);

        // Assert
        hosted.ShouldBe(["KubernetesDiscoveryService"]);
    }

    [Fact]
    public void AddDiscovery_rejects_a_kubernetes_api_server_that_is_not_an_http_address()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddDiscovery(o =>
        {
            Kubernetes(o);
            o.Kubernetes.ApiServer = "not a url";
        });

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData(nameof(KubernetesDiscoveryOptions.ResyncInterval))]
    [InlineData(nameof(KubernetesDiscoveryOptions.RetryMinDelay))]
    public void AddDiscovery_rejects_an_interval_that_is_not_positive(string option)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddDiscovery(o =>
        {
            Kubernetes(o);
            if (option == nameof(KubernetesDiscoveryOptions.ResyncInterval))
            {
                o.Kubernetes.ResyncInterval = TimeSpan.Zero;
            }
            else
            {
                o.Kubernetes.RetryMinDelay = TimeSpan.Zero;
            }
        });

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddDiscovery_rejects_a_maximum_retry_delay_below_the_minimum()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddDiscovery(o =>
        {
            Kubernetes(o);
            o.Kubernetes.RetryMinDelay = TimeSpan.FromSeconds(5);
            o.Kubernetes.RetryMaxDelay = TimeSpan.FromSeconds(1);
        });

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddDiscovery_rejects_an_unknown_kind()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddDiscovery(o => o.Kind = (DiscoveryKind)99);

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }
}
