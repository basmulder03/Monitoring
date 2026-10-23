using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.Notifications;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Notifications;

public class NotificationsSliceTests
{
    [Fact]
    public void Nothing_is_added_when_no_channel_is_configured()
    {
        var services = new ServiceCollection().AddNotifications(_ => { });

        services.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("hooks.example/alerts")]
    [InlineData("ftp://hooks.example/alerts")]
    public void A_channel_needs_an_absolute_http_or_https_address(string address)
    {
        var add = () => new ServiceCollection().AddNotifications(o => o.Webhooks.Add(new WebhookTarget(new Uri(address, UriKind.RelativeOrAbsolute))));

        add.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void The_email_channel_needs_a_sender_and_a_receiver()
    {
        var add = () => new ServiceCollection().AddNotifications(o => o.Email.Host = "smtp.example");

        add.ShouldThrow<ArgumentException>().Message.ShouldContain("sender");
    }

    [Theory]
    [InlineData("severe")]
    [InlineData("")]
    public void The_lowest_severity_must_be_a_known_one(string severity)
    {
        var add = () => new ServiceCollection().AddNotifications(o =>
        {
            o.Webhooks.Add(new WebhookTarget(new Uri("https://hooks.example/alerts")));
            o.MinSeverity = severity;
        });

        add.ShouldThrow<ArgumentException>();
    }
}
