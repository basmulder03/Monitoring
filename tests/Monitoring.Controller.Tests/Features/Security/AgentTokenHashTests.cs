using Monitoring.Controller.Features.Security;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Security;

public class AgentTokenHashTests
{
    [Fact]
    public void Compute_returns_the_lower_case_hex_sha256_of_the_token()
    {
        // Arrange
        const string token = "abc";

        // Act
        var hash = AgentTokenHash.Compute(token);

        // Assert: the published SHA-256 test vector for "abc"
        hash.ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void Compute_gives_different_hashes_for_different_tokens()
    {
        // Arrange
        var first = AgentTokenHash.Compute("token-1");

        // Act
        var second = AgentTokenHash.Compute("token-2");

        // Assert
        second.ShouldNotBe(first);
    }
}
