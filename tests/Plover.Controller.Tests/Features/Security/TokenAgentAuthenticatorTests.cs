using Plover.Controller.Features.Security;
using Plover.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Security;

public class TokenAgentAuthenticatorTests
{
    [Fact]
    public async Task AuthenticateAsync_returns_the_scope_of_a_known_token()
    {
        // Arrange
        var authenticator = new AuthenticatorFixture().WithToken("shop", "secret-1").Build();

        // Act
        var identity = await authenticator.AuthenticateAsync(AuthenticatorFixture.HelloWith("secret-1"), TestContext.Current.CancellationToken);

        // Assert
        identity.ShouldBe(new AgentIdentity("shop"));
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("SECRET-1")]
    [InlineData("secret-1 ")]
    [InlineData("")]
    [InlineData(null)]
    public async Task AuthenticateAsync_rejects_a_token_that_is_not_configured(string? token)
    {
        // Arrange
        var authenticator = new AuthenticatorFixture().WithToken("shop", "secret-1").Build();

        // Act
        var identity = await authenticator.AuthenticateAsync(AuthenticatorFixture.HelloWith(token), TestContext.Current.CancellationToken);

        // Assert
        identity.ShouldBeNull();
    }

    [Theory]
    [InlineData("old-token")]
    [InlineData("new-token")]
    public async Task AuthenticateAsync_accepts_every_token_configured_for_a_scope_during_rotation(string token)
    {
        // Arrange
        var authenticator = new AuthenticatorFixture().WithToken("shop", "old-token").WithToken("shop", "new-token").Build();

        // Act
        var identity = await authenticator.AuthenticateAsync(AuthenticatorFixture.HelloWith(token), TestContext.Current.CancellationToken);

        // Assert
        identity.ShouldBe(new AgentIdentity("shop"));
    }

    [Theory]
    [InlineData("shop-token", "shop")]
    [InlineData("billing-token", "billing")]
    public async Task AuthenticateAsync_maps_each_token_only_to_its_own_scope(string token, string expectedScope)
    {
        // Arrange
        var authenticator = new AuthenticatorFixture().WithToken("shop", "shop-token").WithToken("billing", "billing-token").Build();

        // Act
        var identity = await authenticator.AuthenticateAsync(AuthenticatorFixture.HelloWith(token), TestContext.Current.CancellationToken);

        // Assert
        identity!.ScopeName.ShouldBe(expectedScope);
    }

    [Fact]
    public async Task AuthenticateAsync_accepts_a_configured_hash_written_in_upper_case()
    {
        // Arrange
        var upperCaseHash = AgentTokenHash.Compute("secret-1").ToUpperInvariant();
        var authenticator = new AuthenticatorFixture().WithHash("shop", upperCaseHash).Build();

        // Act
        var identity = await authenticator.AuthenticateAsync(AuthenticatorFixture.HelloWith("secret-1"), TestContext.Current.CancellationToken);

        // Assert
        identity.ShouldBe(new AgentIdentity("shop"));
    }

    [Fact]
    public async Task AuthenticateAsync_rejects_everything_when_no_token_is_configured()
    {
        // Arrange
        var authenticator = new AuthenticatorFixture().Build();

        // Act
        var identity = await authenticator.AuthenticateAsync(AuthenticatorFixture.HelloWith("anything"), TestContext.Current.CancellationToken);

        // Assert
        identity.ShouldBeNull();
    }

    [Theory]
    [InlineData("shop")]
    [InlineData("billing")]
    [InlineData("orders platform")]
    public async Task AuthenticateAsync_lets_an_enrollment_token_register_any_announced_scope(string scope)
    {
        // Arrange
        var authenticator = new AuthenticatorFixture().WithToken(AgentTokenEntry.AnyScope, "enroll").Build();

        // Act
        var identity = await authenticator.AuthenticateAsync(AuthenticatorFixture.HelloWith("enroll", scope), TestContext.Current.CancellationToken);

        // Assert
        identity.ShouldBe(new AgentIdentity(scope));
    }

    [Fact]
    public async Task AuthenticateAsync_trims_the_announced_scope()
    {
        // Arrange
        var authenticator = new AuthenticatorFixture().WithToken(AgentTokenEntry.AnyScope, "enroll").Build();

        // Act
        var identity = await authenticator.AuthenticateAsync(AuthenticatorFixture.HelloWith("enroll", "  shop "), TestContext.Current.CancellationToken);

        // Assert
        identity.ShouldBe(new AgentIdentity("shop"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*")]
    [InlineData("bad\nname")]
    public async Task AuthenticateAsync_rejects_an_enrollment_token_without_a_usable_scope_name(string? scope)
    {
        // Arrange
        var authenticator = new AuthenticatorFixture().WithToken(AgentTokenEntry.AnyScope, "enroll").Build();

        // Act
        var identity = await authenticator.AuthenticateAsync(AuthenticatorFixture.HelloWith("enroll", scope), TestContext.Current.CancellationToken);

        // Assert
        identity.ShouldBeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_rejects_an_announced_scope_name_that_is_too_long()
    {
        // Arrange
        var authenticator = new AuthenticatorFixture().WithToken(AgentTokenEntry.AnyScope, "enroll").Build();
        var tooLong = new string('x', TokenAgentAuthenticator.MaxScopeNameLength + 1);

        // Act
        var identity = await authenticator.AuthenticateAsync(AuthenticatorFixture.HelloWith("enroll", tooLong), TestContext.Current.CancellationToken);

        // Assert
        identity.ShouldBeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_accepts_a_bound_token_announcing_its_own_scope()
    {
        // Arrange
        var authenticator = new AuthenticatorFixture().WithToken("shop", "secret-1").Build();

        // Act
        var identity = await authenticator.AuthenticateAsync(AuthenticatorFixture.HelloWith("secret-1", "shop"), TestContext.Current.CancellationToken);

        // Assert
        identity.ShouldBe(new AgentIdentity("shop"));
    }

    [Fact]
    public async Task AuthenticateAsync_rejects_a_bound_token_announcing_another_scope()
    {
        // Arrange
        var authenticator = new AuthenticatorFixture().WithToken("shop", "secret-1").Build();

        // Act
        var identity = await authenticator.AuthenticateAsync(AuthenticatorFixture.HelloWith("secret-1", "billing"), TestContext.Current.CancellationToken);

        // Assert
        identity.ShouldBeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_uses_the_bound_scope_when_the_token_is_also_configured_as_enrollment_for_another_agent_group()
    {
        // Arrange: the same secret is bound to "shop" and an enrollment token; the agent announces "shop"
        var authenticator = new AuthenticatorFixture().WithToken("billing", "shared").WithToken(AgentTokenEntry.AnyScope, "shared").Build();

        // Act
        var identity = await authenticator.AuthenticateAsync(AuthenticatorFixture.HelloWith("shared", "shop"), TestContext.Current.CancellationToken);

        // Assert: the bound entry refuses "shop", the enrollment entry accepts it
        identity.ShouldBe(new AgentIdentity("shop"));
    }

    [Fact]
    public void Constructor_rejects_a_hash_that_is_not_hexadecimal()
    {
        // Arrange
        var options = new SecurityOptions { AgentTokens = [new AgentTokenEntry("shop", "not-hex")] };

        // Act
        var create = () => new TokenAgentAuthenticator(options);

        // Assert
        create.ShouldThrow<FormatException>();
    }

    /// <summary>Builds an authenticator that accepts exactly the tokens the test adds.</summary>
    private sealed class AuthenticatorFixture
    {
        private readonly SecurityOptions _options = new();

        public AuthenticatorFixture WithToken(string scope, string token) => WithHash(scope, AgentTokenHash.Compute(token));

        public AuthenticatorFixture WithHash(string scope, string hash)
        {
            _options.AgentTokens.Add(new AgentTokenEntry(scope, hash));
            return this;
        }

        public TokenAgentAuthenticator Build() => new(_options);

        public static Hello HelloWith(string? token, string? scope = null) =>
            new(1, "pod-1", "orders", null, new Dictionary<string, string>(), token, scope);
    }
}
