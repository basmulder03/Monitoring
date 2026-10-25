using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Plover.Controller.Features.Alerting;
using Plover.Controller.Features.Packages;
using Plover.Controller.Features.Vulnerabilities;
using Plover.Controller.Tests.Shared;
using Plover.Protocol.Health;
using Plover.Protocol.Instances;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Vulnerabilities;

public class VulnerablePackageRuleTests
{
    private static (VulnerablePackageRule Critical, VulnerablePackageRule Other) Rules()
    {
        var file = Path.Combine(Path.GetTempPath(), $"adv-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, """
            [ { "id": "A-high", "database_specific": { "severity": "HIGH" }, "affected": [ { "package": { "ecosystem": "NuGet", "name": "One" }, "versions": ["1.0"] } ] },
              { "id": "A-mod", "database_specific": { "severity": "MODERATE" }, "affected": [ { "package": { "ecosystem": "NuGet", "name": "Two" }, "versions": ["1.0"] } ] } ]
            """);
        var store = new AdvisoryStore(new VulnerabilitiesOptions { Advisories = file }, new FakeTimeProvider(), NullLogger<AdvisoryStore>.Instance);
        store.Refresh();
        File.Delete(file);

        var registry = new FakeScopeRegistry().WithInstance(
            "shop",
            new InstanceInfo("orders-1", "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(), "1.0",
                Packages: [new PackageReport("One", "1.0", true), new PackageReport("Two", "1.0", true)]));
        var check = new VulnerabilityCheck(new PackageInventory(registry, new PackagesOptions()), store);
        var options = new AlertingOptions();
        return (
            new VulnerablePackageRule("vulnerable-package-critical", AlertSeverity.Critical, serious: true, check, options),
            new VulnerablePackageRule("vulnerable-package", AlertSeverity.Warning, serious: false, check, options));
    }

    [Fact]
    public void The_critical_rule_names_high_and_critical_advisories_with_the_package_and_version()
    {
        // Arrange
        var (critical, _) = Rules();

        // Act
        var subjects = critical.FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["A-high One@1.0"]);
        critical.Severity.ShouldBe(AlertSeverity.Critical);
    }

    [Fact]
    public void The_other_rule_names_the_rest_as_warnings()
    {
        // Arrange
        var (_, other) = Rules();

        // Act
        var subjects = other.FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["A-mod Two@1.0"]);
        other.Severity.ShouldBe(AlertSeverity.Warning);
    }

    [Fact]
    public void The_rules_fire_at_once_by_default()
    {
        // Arrange
        var (critical, other) = Rules();

        // Act
        var waits = new[] { critical.Wait, other.Wait };

        // Assert
        waits.ShouldAllBe(wait => wait == TimeSpan.Zero);
    }
}
