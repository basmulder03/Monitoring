using Monitoring.Controller.Features.Topology;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Topology;

public class TargetResolverTests
{
    private static InstanceInfo Instance(string service, string? ip = null) =>
        new($"{service}-1", service, InstanceLifecycle.Running, HealthState.Healthy,
            ip is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["k8s.pod-ip"] = ip });

    private static TargetResolver Resolver(params (string Scope, InstanceInfo Instance)[] instances) => new(instances);

    [Fact]
    public void Resolve_finds_the_service_that_has_the_address_of_the_pod()
    {
        // Arrange
        var resolver = Resolver(("shop", Instance("payments", "10.1.2.3")));

        // Act
        var node = resolver.Resolve("billing", "HTTP", "10.1.2.3:8080");

        // Assert
        node.Id.ShouldBe("service:shop/payments");
        node.Kind.ShouldBe(NodeKind.Service);
    }

    [Theory]
    [InlineData("payments.shop:8080")]
    [InlineData("payments.shop.svc:8080")]
    [InlineData("payments.shop.svc.cluster.local:8080")]
    public void Resolve_reads_service_dot_namespace_names_across_scopes(string target)
    {
        // Arrange
        var resolver = Resolver(("shop", Instance("payments")), ("billing", Instance("invoices")));

        // Act
        var node = resolver.Resolve("billing", "HTTP", target);

        // Assert
        node.Id.ShouldBe("service:shop/payments");
    }

    [Fact]
    public void Resolve_takes_a_plain_name_to_be_a_service_of_the_callers_own_scope()
    {
        // Arrange
        var resolver = Resolver(("shop", Instance("payments")), ("other", Instance("payments")));

        // Act
        var node = resolver.Resolve("other", "HTTP", "payments:8080");

        // Assert
        node.Id.ShouldBe("service:other/payments");
    }

    [Fact]
    public void Resolve_does_not_guess_a_plain_name_that_is_only_a_service_of_another_scope()
    {
        // Arrange
        var resolver = Resolver(("shop", Instance("payments")));

        // Act
        var node = resolver.Resolve("billing", "HTTP", "payments:8080");

        // Assert
        node.Kind.ShouldBe(NodeKind.External);
        node.Id.ShouldBe("external:payments:8080");
    }

    [Fact]
    public void Resolve_shows_an_address_nobody_knows_as_an_external_system()
    {
        // Arrange
        var resolver = Resolver(("shop", Instance("payments")));

        // Act
        var node = resolver.Resolve("shop", "HTTP", "api.example.org:443");

        // Assert
        node.Kind.ShouldBe(NodeKind.External);
        node.Name.ShouldBe("api.example.org:443");
    }

    [Theory]
    [InlineData("DB", "mssql://db1:1433/orders", NodeKind.Database, "database:mssql://db1:1433/orders")]
    [InlineData("MQ", "activemq://orders", NodeKind.Queue, "queue:activemq://orders")]
    public void Resolve_makes_databases_and_queues_nodes_of_their_own(string type, string target, NodeKind kind, string id)
    {
        // Arrange
        var resolver = Resolver();

        // Act
        var node = resolver.Resolve("shop", type, target);

        // Assert
        node.Kind.ShouldBe(kind);
        node.Id.ShouldBe(id);
        node.Name.ShouldBe(target);
    }
}
