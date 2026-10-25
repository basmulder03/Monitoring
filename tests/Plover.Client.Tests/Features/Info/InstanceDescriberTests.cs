using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Plover.Client.Features.Info;
using Plover.Client.Features.LocalServer;
using Shouldly;
using Xunit;

namespace Plover.Client.Tests.Features.Info;

public class InstanceDescriberTests
{
    [Fact]
    public void Describe_uses_the_configured_service_name_and_version()
    {
        // Arrange
        var describer = new InstanceDescriber(new PloverOptions { ServiceName = "orders", Version = "1.2.3" }, new Environment("host-app"));

        // Act
        var description = describer.Describe();

        // Assert
        description.ServiceName.ShouldBe("orders");
        description.Version.ShouldBe("1.2.3");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Describe_falls_back_to_the_name_of_the_application(string? configured)
    {
        // Arrange
        var describer = new InstanceDescriber(new PloverOptions { ServiceName = configured }, new Environment("host-app"));

        // Act
        var description = describer.Describe();

        // Assert
        description.ServiceName.ShouldBe("host-app");
    }

    [Fact]
    public void Describe_falls_back_to_the_version_of_the_entry_assembly_without_the_commit()
    {
        // Arrange
        var describer = new InstanceDescriber(new PloverOptions(), new Environment("host-app"));

        // Act
        var description = describer.Describe();

        // Assert: whatever the version is, build metadata after a plus sign is not part of it
        description.Version?.ShouldNotContain("+");
    }

    private sealed class Environment(string applicationName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";

        public string ApplicationName { get; set; } = applicationName;

        public string ContentRootPath { get; set; } = "/";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
