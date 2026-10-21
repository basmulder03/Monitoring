using Monitoring.Component.Tests.Support;
using Monitoring.Controller.Features.Security;
using Shouldly;
using Xunit;

namespace Monitoring.Component.Tests.Executables;

/// <summary>Whole controller and agent hosts, built from configuration alone, talking over loopback TCP.</summary>
public class ExecutablesFromConfigurationTests
{
    private const string EnrollmentToken = "enroll";

    [Fact]
    public async Task Agents_register_themselves_under_their_own_scope_with_a_single_enrollment_token()
    {
        // Arrange
        await using var controller = new ControllerHost().AcceptingEnrollment(EnrollmentToken).Start();
        await using var shop = new AgentHost("shop", EnrollmentToken).ConnectingTo(controller).Start();
        await using var billing = new AgentHost("billing", EnrollmentToken).ConnectingTo(controller).Start();

        // Act
        shop.Report("shop-pod-1");
        billing.Report("billing-pod-1");

        // Assert
        await Eventually.AssertAsync(() =>
        {
            controller.Registry.GetScopeNames().ShouldBe(["billing", "shop"]);
            controller.InstanceIds("shop").ShouldBe(["shop-pod-1"]);
            controller.InstanceIds("billing").ShouldBe(["billing-pod-1"]);
        });
    }

    [Fact]
    public async Task An_agent_with_a_token_bound_to_another_scope_is_not_registered()
    {
        // Arrange
        await using var controller = new ControllerHost().AcceptingScope("shop", "shop-secret").Start();
        await using var agent = new AgentHost("billing", "shop-secret").ConnectingTo(controller).Start();

        // Act
        agent.Report("billing-pod-1");
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Assert
        controller.Registry.GetScopeNames().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_bound_token_still_registers_an_agent_for_its_own_scope()
    {
        // Arrange
        await using var controller = new ControllerHost().AcceptingScope("shop", "shop-secret").Start();
        await using var agent = new AgentHost("shop", "shop-secret").ConnectingTo(controller).Start();

        // Act
        agent.Report("shop-pod-1");

        // Assert
        await Eventually.AssertAsync(() => controller.InstanceIds("shop").ShouldBe(["shop-pod-1"]));
    }

    [Fact]
    public async Task An_agent_with_an_unknown_token_is_not_registered()
    {
        // Arrange
        await using var controller = new ControllerHost().AcceptingEnrollment(EnrollmentToken).Start();
        await using var agent = new AgentHost("shop", "guess").ConnectingTo(controller).Start();

        // Act
        agent.Report("shop-pod-1");
        await Task.Delay(500, TestContext.Current.CancellationToken);

        // Assert
        controller.Registry.GetScopeNames().ShouldBeEmpty();
    }
}
