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

    private static PackageInventory Inventory(FakeScopeRegistry registry, params AlignmentRule[] aligned) =>
        new(registry, new PackagesOptions { Aligned = [.. aligned] });

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

    private static FakeScopeRegistry TwoServices(string first, string second) => new FakeScopeRegistry()
        .WithInstance("shop", Instance("orders", "orders-1", "1.0", [Package("Microsoft.Extensions.Hosting", first)]))
        .WithInstance("shop", Instance("payments", "payments-1", "1.0", [Package("Microsoft.Extensions.Hosting", second)]));

    [Theory]
    [InlineData("10.0.1", "10.0.1", VersionSpread.Exact, false)]
    [InlineData("10.0.1", "10.0.2", VersionSpread.Exact, true)]
    [InlineData("10.0.1", "10.0.2", VersionSpread.Minor, false)]
    [InlineData("10.0.1", "10.1.0", VersionSpread.Minor, true)]
    [InlineData("10.0.1", "10.1.0", VersionSpread.Major, false)]
    [InlineData("9.0.5", "10.0.0", VersionSpread.Major, true)]
    [InlineData("10.0.0-rc.1", "10.0.0", VersionSpread.Minor, false)]
    public void GetCatalog_flags_a_package_whose_versions_are_further_apart_than_its_rule_allows(string first, string second, VersionSpread spread, bool violated)
    {
        // Arrange
        var inventory = Inventory(TwoServices(first, second), new AlignmentRule("Microsoft.Extensions.*", spread));

        // Act
        var entry = inventory.Find("Microsoft.Extensions.Hosting")!;

        // Assert
        (entry.Violation is not null).ShouldBe(violated);
    }

    [Fact]
    public void GetCatalog_says_which_rule_a_package_breaks()
    {
        // Arrange
        var inventory = Inventory(TwoServices("9.0.0", "10.0.0"), new AlignmentRule("Microsoft.Extensions.*", VersionSpread.Major));

        // Act
        var entry = inventory.Find("Microsoft.Extensions.Hosting")!;

        // Assert
        entry.Violation.ShouldBe("Microsoft.Extensions.* must have the same major version");
    }

    [Fact]
    public void GetCatalog_does_not_flag_packages_that_no_rule_covers()
    {
        // Arrange
        var inventory = Inventory(TwoServices("9.0.0", "10.0.0"), new AlignmentRule("Serilog", VersionSpread.Exact));

        // Act
        var entry = inventory.Find("Microsoft.Extensions.Hosting")!;

        // Assert
        entry.Violation.ShouldBeNull();
    }

    [Fact]
    public void GetCatalog_lets_the_first_rule_that_covers_a_package_decide()
    {
        // Arrange
        var inventory = Inventory(
            TwoServices("10.0.1", "10.1.0"),
            new AlignmentRule("Microsoft.Extensions.Hosting", VersionSpread.Major),
            new AlignmentRule("Microsoft.Extensions.*", VersionSpread.Exact));

        // Act
        var entry = inventory.Find("Microsoft.Extensions.Hosting")!;

        // Assert
        entry.Violation.ShouldBeNull();
    }

    [Theory]
    [InlineData("Serilog", "serilog", true)]
    [InlineData("Microsoft.Extensions.*", "microsoft.extensions.hosting", true)]
    [InlineData("Microsoft.Extensions.*", "Microsoft.Extension", false)]
    [InlineData("Serilog", "Serilog.Sinks.File", false)]
    public void An_alignment_rule_covers_a_package_by_name_or_prefix_whatever_the_case(string rule, string package, bool covered)
    {
        // Arrange
        var alignment = new AlignmentRule(rule, VersionSpread.Exact);

        // Act
        var result = alignment.Covers(package);

        // Assert
        result.ShouldBe(covered);
    }
}
