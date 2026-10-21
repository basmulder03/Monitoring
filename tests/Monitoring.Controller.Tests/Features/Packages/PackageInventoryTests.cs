using Monitoring.Controller.Features.Packages;
using Monitoring.Controller.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Packages;

public class PackageInventoryTests
{
    private static PackageReport Package(string name, string version, bool direct = true) => new(name, version, direct);

    private static InstanceInfo Instance(
        string service, string id, string? version, IReadOnlyList<PackageReport>? packages, InstanceLifecycle lifecycle = InstanceLifecycle.Running) =>
        new(id, service, lifecycle, HealthState.Healthy, new Dictionary<string, string>(), version, Packages: packages);

    private static PackageInventory Inventory(FakeScopeRegistry registry) => new(registry);

    [Fact]
    public void GetCatalog_lists_each_package_once_with_the_versions_in_use_newest_first()
    {
        // Arrange
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", Instance("orders", "orders-1", "1.0", [Package("Serilog", "3.1.0"), Package("Json", "13.0.1")]))
            .WithInstance("shop", Instance("payments", "payments-1", "2.0", [Package("Serilog", "4.0.0")]));

        // Act
        var catalog = Inventory(registry).GetCatalog();

        // Assert
        catalog.Packages.Select(p => p.Name).ShouldBe(["Json", "Serilog"]);
        catalog.Packages.Single(p => p.Name == "Serilog").Versions.Select(v => v.Version).ShouldBe(["4.0.0", "3.1.0"]);
    }

    [Fact]
    public void GetCatalog_says_which_services_use_each_version_and_how_many_instances()
    {
        // Arrange
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", Instance("orders", "orders-1", "1.0", [Package("Serilog", "4.0.0")]))
            .WithInstance("shop", Instance("orders", "orders-2", "1.0", [Package("Serilog", "4.0.0", direct: false)]))
            .WithInstance("billing", Instance("invoices", "invoices-1", "1.0", [Package("Serilog", "4.0.0")]));

        // Act
        var usage = Inventory(registry).Find("serilog")!.Versions.Single().UsedBy;

        // Assert
        usage.Select(u => (u.Scope, u.Service, u.Instances, u.Direct)).ShouldBe([("billing", "invoices", 1, true), ("shop", "orders", 2, true)]);
    }

    [Fact]
    public void GetCatalog_counts_the_services_that_have_not_reported_what_they_are_built_from()
    {
        // Arrange
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", Instance("orders", "orders-1", "1.0", [Package("Serilog", "4.0.0")]))
            .WithInstance("shop", Instance("legacy", "legacy-1", null, null))
            .WithInstance("shop", Instance("empty", "empty-1", null, []));

        // Act
        var catalog = Inventory(registry).GetCatalog();

        // Assert
        catalog.Services.ShouldBe(1);
        catalog.ServicesWithoutList.ShouldBe(2);
    }

    [Fact]
    public void GetCatalog_ignores_instances_that_are_gone()
    {
        // Arrange
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", Instance("orders", "orders-1", "1.0", [Package("Serilog", "4.0.0")], InstanceLifecycle.Gone));

        // Act
        var catalog = Inventory(registry).GetCatalog();

        // Assert
        catalog.Packages.ShouldBeEmpty();
    }

    [Fact]
    public void Find_returns_nothing_for_a_package_nobody_uses()
    {
        // Arrange
        var registry = new FakeScopeRegistry();

        // Act
        var entry = Inventory(registry).Find("Nope");

        // Assert
        entry.ShouldBeNull();
    }

    [Fact]
    public void ForService_describes_the_newest_instance_when_a_rollout_is_going_on()
    {
        // Arrange
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", Instance("orders", "orders-1", "1.9.0", [Package("Serilog", "3.1.0")]))
            .WithInstance("shop", Instance("orders", "orders-2", "1.10.0", [Package("Serilog", "4.0.0")]));

        // Act
        var service = Inventory(registry).ForService("shop", "orders")!;

        // Assert
        service.Version.ShouldBe("1.10.0");
        service.Packages.Single().Version.ShouldBe("4.0.0");
    }

    [Fact]
    public void ForService_returns_nothing_for_a_service_that_reported_no_packages()
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", Instance("orders", "orders-1", "1.0", null));

        // Act
        var service = Inventory(registry).ForService("shop", "orders");

        // Assert
        service.ShouldBeNull();
    }
}
