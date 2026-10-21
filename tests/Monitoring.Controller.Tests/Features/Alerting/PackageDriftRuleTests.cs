using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Packages;
using Monitoring.Controller.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Alerting;

public class PackageDriftRuleTests
{
    private static InstanceInfo Instance(string service, string version) =>
        new($"{service}-1", service, InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(), "1.0",
            Packages: [new PackageReport("Serilog", version, true), new PackageReport("Json", "13.0.1", true)]);

    [Fact]
    public void FindFailingSubjects_names_the_packages_that_break_their_alignment_rule()
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", Instance("orders", "3.1.0")).WithInstance("shop", Instance("payments", "4.0.0"));
        var inventory = new PackageInventory(registry, new PackagesOptions { Aligned = [new AlignmentRule("Serilog", VersionSpread.Major)] });
        var rule = new PackageDriftRule(inventory, new AlertingOptions());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["Serilog"]);
    }

    [Fact]
    public void FindFailingSubjects_is_empty_when_every_aligned_package_agrees()
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", Instance("orders", "4.0.0")).WithInstance("shop", Instance("payments", "4.0.1"));
        var inventory = new PackageInventory(registry, new PackagesOptions { Aligned = [new AlignmentRule("Serilog", VersionSpread.Major)] });
        var rule = new PackageDriftRule(inventory, new AlertingOptions());

        // Act
        var subjects = rule.FindFailingSubjects();

        // Assert
        subjects.ShouldBeEmpty();
    }

    [Fact]
    public void The_rule_waits_an_hour_by_default_because_a_rollout_passes_through_this_state()
    {
        // Arrange
        var rule = new PackageDriftRule(new PackageInventory(new FakeScopeRegistry(), new PackagesOptions()), new AlertingOptions());

        // Act
        var wait = rule.Wait;

        // Assert
        wait.ShouldBe(TimeSpan.FromHours(1));
    }
}
