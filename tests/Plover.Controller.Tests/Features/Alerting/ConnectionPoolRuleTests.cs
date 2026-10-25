using Plover.Controller.Features.Alerting;
using Plover.Controller.Tests.Shared;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Alerting;

public class ConnectionPoolRuleTests
{
    private static InstanceInfo Pod(string id, string pool, double max, double used) =>
        new(id, "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(), Metrics:
        [
            new MetricSeries("db.client.connections.max", new Dictionary<string, string> { ["pool.name"] = pool }, max),
            new MetricSeries("db.client.connections.usage", new Dictionary<string, string> { ["pool.name"] = pool, ["state"] = "used" }, used),
            new MetricSeries("db.client.connections.usage", new Dictionary<string, string> { ["pool.name"] = pool, ["state"] = "idle" }, max - used),
        ]);

    [Fact]
    public void A_pool_without_a_free_connection_is_named_by_service_and_pool()
    {
        var registry = new FakeScopeRegistry().WithInstance("shop", Pod("a", "db1", max: 10, used: 10)).WithInstance("shop", Pod("b", "db2", max: 10, used: 9));

        new ConnectionPoolRule(TimeSpan.FromMinutes(1), registry).FindFailingSubjects().ShouldBe(["shop/orders/db1"]);
    }

    [Fact]
    public void Several_full_replicas_of_one_pool_are_named_once()
    {
        var registry = new FakeScopeRegistry().WithInstance("shop", Pod("a", "db1", 5, 5)).WithInstance("shop", Pod("b", "db1", 5, 5));

        new ConnectionPoolRule(TimeSpan.Zero, registry).FindFailingSubjects().ShouldBe(["shop/orders/db1"]);
    }
}
