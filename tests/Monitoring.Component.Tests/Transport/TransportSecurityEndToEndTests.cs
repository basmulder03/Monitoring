using Monitoring.Component.Tests.Support;
using Monitoring.Tests.Shared;
using Shouldly;
using Xunit;

namespace Monitoring.Component.Tests.Transport;

/// <summary>A real agent and a real controller talking over TLS, and what happens when the two do not agree.</summary>
public class TransportSecurityEndToEndTests
{
    private const string Token = "enroll";
    private const string ControllerName = "controller.test";

    [Fact]
    public async Task An_agent_that_trusts_the_authority_reports_over_tls()
    {
        // Arrange
        using var pki = new TestPki();
        var server = pki.Issue(ControllerName);
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithTls(server).Start();

        // Act
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).WithTls(pki, ControllerName).Start();
        agent.Report("pod-1");

        // Assert
        await Eventually.AssertAsync(() => controller.InstanceIds("shop").ShouldBe(["pod-1"]));
    }

    [Fact]
    public async Task An_agent_with_a_client_certificate_reports_over_mutual_tls()
    {
        // Arrange
        using var pki = new TestPki();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithTls(pki.Issue(ControllerName), clientCa: pki).Start();

        // Act
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller)
            .WithTls(pki, ControllerName, clientCertificate: pki.Issue("agent-1", client: true)).Start();
        agent.Report("pod-1");

        // Assert
        await Eventually.AssertAsync(() => controller.InstanceIds("shop").ShouldBe(["pod-1"]));
    }

    [Fact]
    public async Task An_agent_that_does_not_trust_the_controller_certificate_never_reports()
    {
        // Arrange
        using var pki = new TestPki();
        using var other = new TestPki("Other CA");
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithTls(pki.Issue(ControllerName)).Start();

        // Act
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).WithTls(other, ControllerName).Start();
        agent.Report("pod-1");
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        // Assert
        controller.InstanceIds("shop").ShouldBeEmpty();
    }

    [Fact]
    public async Task A_plain_agent_cannot_report_to_a_controller_that_requires_tls()
    {
        // Arrange
        using var pki = new TestPki();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithTls(pki.Issue(ControllerName)).Start();

        // Act
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).Start();
        agent.Report("pod-1");
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        // Assert
        controller.InstanceIds("shop").ShouldBeEmpty();
    }

    [Fact]
    public async Task An_agent_without_a_client_certificate_cannot_report_to_a_controller_that_requires_one()
    {
        // Arrange
        using var pki = new TestPki();
        await using var controller = new ControllerHost().AcceptingEnrollment(Token).WithTls(pki.Issue(ControllerName), clientCa: pki).Start();

        // Act
        await using var agent = new AgentHost("shop", Token).ConnectingTo(controller).WithTls(pki, ControllerName).Start();
        agent.Report("pod-1");
        await Task.Delay(1500, TestContext.Current.CancellationToken);

        // Assert
        controller.InstanceIds("shop").ShouldBeEmpty();
    }
}
