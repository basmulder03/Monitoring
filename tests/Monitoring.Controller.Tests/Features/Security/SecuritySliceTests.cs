using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.Security;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Security;

public class SecuritySliceTests
{
    [Fact]
    public void AddSecurity_makes_the_authenticator_resolvable()
    {
        // Arrange
        var services = new ServiceCollection().AddSecurity(o => o.AgentTokens.Add(new AgentTokenEntry("shop", AgentTokenHash.Compute("t"))));
        using var provider = services.BuildServiceProvider();

        // Act
        var authenticator = provider.GetService<IAgentAuthenticator>();

        // Assert
        authenticator.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("xyz")]
    [InlineData("ba7816bf")]
    [InlineData("zz7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void AddSecurity_rejects_a_hash_that_is_not_64_hexadecimal_characters(string hash)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddSecurity(o => o.AgentTokens.Add(new AgentTokenEntry("shop", hash)));

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AddSecurity_rejects_an_entry_without_a_scope_name(string scopeName)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddSecurity(o => o.AgentTokens.Add(new AgentTokenEntry(scopeName, AgentTokenHash.Compute("t"))));

        // Assert
        add.ShouldThrow<ArgumentException>();
    }
}
