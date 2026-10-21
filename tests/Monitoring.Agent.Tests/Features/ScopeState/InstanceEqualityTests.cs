using Monitoring.Agent.Features.ScopeState;
using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.ScopeState;

public class InstanceEqualityTests
{
    private static InstanceInfo Instance(
        string id = "a",
        string service = "orders",
        InstanceLifecycle lifecycle = InstanceLifecycle.Running,
        HealthState health = HealthState.Healthy,
        string? version = "1.0",
        Dictionary<string, string>? labels = null,
        IReadOnlyList<HealthCheckResult>? checks = null) =>
        new(id, service, lifecycle, health, labels ?? new Dictionary<string, string> { ["k"] = "v" }, version, checks);

    [Fact]
    public void AreEqual_is_true_for_instances_that_say_the_same_even_with_separate_dictionaries_and_lists()
    {
        // Arrange
        var a = Instance(checks: [new HealthCheckResult("db", HealthState.Healthy)]);
        var b = Instance(checks: [new HealthCheckResult("db", HealthState.Healthy)]);

        // Act
        var equal = InstanceEquality.AreEqual(a, b);

        // Assert
        equal.ShouldBeTrue();
    }

    [Fact]
    public void AreEqual_is_true_when_neither_instance_has_checks()
    {
        // Arrange
        var a = Instance(checks: null);
        var b = Instance(checks: null);

        // Act
        var equal = InstanceEquality.AreEqual(a, b);

        // Assert
        equal.ShouldBeTrue();
    }

    public static TheoryData<string, InstanceInfo> Differences() => new()
    {
        { "id", Instance(id: "b") },
        { "service name", Instance(service: "payments") },
        { "lifecycle", Instance(lifecycle: InstanceLifecycle.Terminating) },
        { "health", Instance(health: HealthState.Degraded) },
        { "version", Instance(version: "2.0") },
        { "label value", Instance(labels: new Dictionary<string, string> { ["k"] = "other" }) },
        { "label name", Instance(labels: new Dictionary<string, string> { ["other"] = "v" }) },
        { "extra label", Instance(labels: new Dictionary<string, string> { ["k"] = "v", ["x"] = "y" }) },
        { "a check appears", Instance(checks: [new HealthCheckResult("db", HealthState.Healthy)]) },
    };

    [Theory]
    [MemberData(nameof(Differences))]
    public void AreEqual_is_false_when_one_thing_differs(string difference, InstanceInfo other)
    {
        // Arrange
        var original = Instance();

        // Act
        var equal = InstanceEquality.AreEqual(original, other);

        // Assert
        equal.ShouldBeFalse($"the {difference} differs");
    }

    [Theory]
    [InlineData(HealthState.Healthy, HealthState.Unhealthy, false)]
    [InlineData(HealthState.Healthy, HealthState.Healthy, true)]
    public void AreEqual_compares_the_result_of_each_check(HealthState first, HealthState second, bool expected)
    {
        // Arrange
        var a = Instance(checks: [new HealthCheckResult("db", first, "x")]);
        var b = Instance(checks: [new HealthCheckResult("db", second, "x")]);

        // Act
        var equal = InstanceEquality.AreEqual(a, b);

        // Assert
        equal.ShouldBe(expected);
    }
}
