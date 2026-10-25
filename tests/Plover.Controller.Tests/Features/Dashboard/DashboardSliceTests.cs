using Microsoft.Extensions.DependencyInjection;
using Plover.Controller.Features.Alerting;
using Plover.Controller.Features.Configuration;
using Plover.Controller.Features.Dashboard;
using Plover.Controller.Features.Health;
using Plover.Controller.Features.Liveness;
using Plover.Controller.Features.Packages;
using Plover.Controller.Features.Registry;
using Plover.Controller.Features.Vulnerabilities;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Dashboard;

public class DashboardSliceTests
{
    [Fact]
    public void AddDashboard_makes_the_model_resolvable_together_with_what_it_reads_from()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().AddLiveness().AddHealth().AddPackages().AddVulnerabilities().AddAlerting().AddConfiguration().AddDashboard().BuildServiceProvider();

        // Act
        var model = provider.GetService<DashboardModel>();

        // Assert
        model.ShouldNotBeNull();
    }

    [Theory]
    [InlineData(nameof(DashboardOptions.PushInterval))]
    [InlineData(nameof(DashboardOptions.KeepAliveInterval))]
    public void AddDashboard_rejects_an_interval_that_is_not_positive(string option)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddDashboard(o =>
        {
            if (option == nameof(DashboardOptions.PushInterval))
            {
                o.PushInterval = TimeSpan.Zero;
            }
            else
            {
                o.KeepAliveInterval = TimeSpan.Zero;
            }
        });

        // Assert
        add.ShouldThrow<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void AddDashboard_accepts_an_empty_list_of_labels_to_group_by()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddDashboard(o => o.GroupBy = []);

        // Assert
        add.ShouldNotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AddDashboard_rejects_a_blank_label_name_to_group_by(string name)
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddDashboard(o => o.GroupBy = ["environment", name]);

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void AddDashboard_rejects_a_label_name_listed_twice()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var add = () => services.AddDashboard(o => o.GroupBy = ["environment", "environment"]);

        // Assert
        add.ShouldThrow<ArgumentException>();
    }
}
