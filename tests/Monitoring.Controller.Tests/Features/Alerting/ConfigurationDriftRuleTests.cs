using Microsoft.Extensions.Logging.Abstractions;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Configuration;
using Monitoring.Controller.Tests.Shared;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Alerting;

public class ConfigurationDriftRuleTests
{
    private readonly EffectiveConfiguration _configuration = new(NullLogger<EffectiveConfiguration>.Instance);
    private readonly FakeAgentLiveness _liveness = new();
    private readonly FakeScopeRegistry _registry = new FakeScopeRegistry()
        .WithInstance("shop", "orders", InstanceLifecycle.Running, HealthState.Healthy);

    private ConfigurationDriftRule Rule(AppliedConfigurations applied) => new(_registry, _liveness, applied, new AlertingOptions());

    [Fact]
    public void A_scope_whose_agent_uses_another_configuration_than_expected_is_named()
    {
        // Arrange
        var applied = new AppliedConfigurations(_configuration);
        applied.Report("shop", Guid.NewGuid(), "some-old-version");

        // Act
        var subjects = Rule(applied).FindFailingSubjects();

        // Assert
        subjects.ShouldBe(["shop"]);
    }

    [Fact]
    public void A_scope_whose_agent_uses_the_expected_configuration_or_has_not_said_is_not_named()
    {
        // Arrange
        var applied = new AppliedConfigurations(_configuration);
        var rule = Rule(applied);

        // Act
        var silent = rule.FindFailingSubjects().ToList();
        applied.Report("shop", Guid.NewGuid(), _configuration.For("shop", new Dictionary<string, string>()).Version);
        var inStep = rule.FindFailingSubjects().ToList();

        // Assert
        silent.ShouldBeEmpty();
        inStep.ShouldBeEmpty();
    }

    [Fact]
    public void A_scope_without_a_live_agent_is_left_to_the_alert_for_that()
    {
        // Arrange
        var applied = new AppliedConfigurations(_configuration);
        applied.Report("shop", Guid.NewGuid(), "some-old-version");
        _liveness.WithDeadScope("shop");

        // Act
        var subjects = Rule(applied).FindFailingSubjects();

        // Assert
        subjects.ShouldBeEmpty();
    }
}
