using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Dashboard;
using Monitoring.Controller.Features.Health;
using Monitoring.Controller.Features.Registry;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Dashboard;

public class DashboardSliceTests
{
    [Fact]
    public void AddDashboard_makes_the_model_resolvable_together_with_what_it_reads_from()
    {
        // Arrange
        using var provider = new ServiceCollection().AddRegistry().AddHealth().AddAlerting().AddDashboard().BuildServiceProvider();

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
}
