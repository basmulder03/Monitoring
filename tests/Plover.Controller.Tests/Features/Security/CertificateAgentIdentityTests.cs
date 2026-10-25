using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Plover.Controller.Features.Security;
using Plover.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Security;

public class CertificateAgentIdentityTests
{
    private static X509Certificate2 CertificateOf(string commonName)
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static Hello HelloAs(string? scope) => new(1, "agent-1", "agent", null, new Dictionary<string, string>(), null, scope);

    [Fact]
    public void The_common_name_of_the_certificate_is_the_scope()
    {
        var identity = new CertificateAgentIdentity(new CertificateOptions()).Identify(HelloAs(null), CertificateOf("shop"));

        identity.ShouldBe(new AgentIdentity("shop"));
    }

    [Fact]
    public void A_mapped_name_gives_another_scope()
    {
        var options = new CertificateOptions { Scopes = { ["agent-shop"] = "shop" } };

        new CertificateAgentIdentity(options).Identify(HelloAs("shop"), CertificateOf("agent-shop")).ShouldBe(new AgentIdentity("shop"));
    }

    [Fact]
    public void An_agent_that_announces_another_scope_than_its_certificate_is_not_known()
    {
        new CertificateAgentIdentity(new CertificateOptions()).Identify(HelloAs("billing"), CertificateOf("shop")).ShouldBeNull();
    }

    [Fact]
    public void A_scope_that_is_too_long_is_not_accepted()
    {
        new CertificateAgentIdentity(new CertificateOptions()).Identify(HelloAs(null), CertificateOf(new string('a', 254))).ShouldBeNull();
    }
}
