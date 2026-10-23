using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Alerting;

public class CertificateExpiryRuleTests
{
    private static InstanceInfo Pod(string id, params (string Certificate, double Days)[] certificates) =>
        new(id, "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>(),
            Metrics: [.. certificates.Select(c => new MetricSeries(CertificateExpiryRule.MetricName, new Dictionary<string, string> { ["certificate"] = c.Certificate }, c.Days))]);

    private static CertificateExpiryRule Warning(FakeScopeRegistry registry) =>
        new("certificate-expiring", AlertSeverity.Warning, registry, belowDays: 14, notBelowDays: 3);

    private static CertificateExpiryRule Critical(FakeScopeRegistry registry) =>
        new("certificate-expired-soon", AlertSeverity.Critical, registry, belowDays: 3, notBelowDays: null);

    [Fact]
    public void The_warning_names_a_certificate_that_expires_within_two_weeks_but_not_within_three_days()
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", Pod("a", ("https", 10)));

        // Act
        var subjects = Warning(registry).FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["shop/orders/https"]);
        Critical(registry).FindFailingSubjects().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(2.5)]
    [InlineData(-1)]
    public void The_critical_alert_takes_over_below_three_days_and_for_an_expired_certificate(double days)
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", Pod("a", ("https", days)));

        // Act
        var critical = Critical(registry).FindFailingSubjects();

        // Assert
        critical.ShouldBe(["shop/orders/https"]);
        Warning(registry).FindFailingSubjects().ShouldBeEmpty();
    }

    [Fact]
    public void A_certificate_with_plenty_of_time_left_is_not_named()
    {
        // Arrange
        var registry = new FakeScopeRegistry().WithInstance("shop", Pod("a", ("https", 60)));

        // Act
        var subjects = Warning(registry).FindFailingSubjects().Concat(Critical(registry).FindFailingSubjects());

        // Assert
        subjects.ShouldBeEmpty();
    }

    [Fact]
    public void Replicas_that_share_a_certificate_count_once_and_the_one_that_expires_first_decides()
    {
        // Arrange
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", Pod("a", ("https", 60)))
            .WithInstance("shop", Pod("b", ("https", 10)));

        // Act
        var subjects = Warning(registry).FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["shop/orders/https"]);
    }

    [Fact]
    public void Instances_that_are_gone_or_report_no_certificate_are_ignored()
    {
        // Arrange
        var registry = new FakeScopeRegistry()
            .WithInstance("shop", Pod("a", ("https", 1)) with { Lifecycle = InstanceLifecycle.Gone })
            .WithInstance("shop", new InstanceInfo("b", "orders", InstanceLifecycle.Running, HealthState.Healthy, new Dictionary<string, string>()));

        // Act
        var subjects = Critical(registry).FindFailingSubjects();

        // Assert
        subjects.ShouldBeEmpty();
    }
}
