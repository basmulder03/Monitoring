using Monitoring.Protocol.Versioning;
using Shouldly;
using Xunit;

namespace Monitoring.Protocol.Tests.Versioning;

public class ProtocolVersionTests
{
    [Theory]
    [InlineData(ProtocolVersion.Current - 1, true)]
    [InlineData(ProtocolVersion.Current, true)]
    [InlineData(ProtocolVersion.Current + 1, true)]
    [InlineData(ProtocolVersion.Current - 2, false)]
    [InlineData(ProtocolVersion.Current + 2, false)]
    [InlineData(int.MinValue, false)]
    [InlineData(int.MaxValue, false)]
    public void IsCompatible_accepts_only_adjacent_versions(int remoteVersion, bool expected)
    {
        // Arrange: the version announced by the peer comes from the test data

        // Act
        var compatible = ProtocolVersion.IsCompatible(remoteVersion);

        // Assert
        compatible.ShouldBe(expected);
    }
}
