using Monitoring.Common;
using Shouldly;
using Xunit;

namespace Monitoring.Common.Tests;

public class HostPortTests
{
    [Theory]
    [InlineData("controller.example:9440", "controller.example", 9440)]
    [InlineData("  controller:1  ", "controller", 1)]
    [InlineData("10.1.2.3:65535", "10.1.2.3", 65535)]
    [InlineData("[::1]:9440", "::1", 9440)]
    public void TryParse_splits_host_and_port(string text, string expectedHost, int expectedPort)
    {
        // Arrange: the address comes from the test data

        // Act
        var parsed = HostPort.TryParse(text, out var host, out var port);

        // Assert
        parsed.ShouldBeTrue();
        host.ShouldBe(expectedHost);
        port.ShouldBe(expectedPort);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("controller")]
    [InlineData("controller:")]
    [InlineData("controller:0")]
    [InlineData("controller:65536")]
    [InlineData("controller:abc")]
    [InlineData(":9440")]
    [InlineData("controller:9440/path")]
    [InlineData("tcp://controller:9440")]
    public void TryParse_rejects_text_that_is_not_host_and_port(string? text)
    {
        // Arrange: the address comes from the test data

        // Act
        var parsed = HostPort.TryParse(text, out _, out _);

        // Assert
        parsed.ShouldBeFalse();
    }
}
