using Plover.Specifications.Support;
using Reqnroll;
using Shouldly;

namespace Plover.Specifications.Steps;

/// <summary>What the channels outside the dashboard are told.</summary>
[Binding]
public sealed class NotificationSteps(DashboardDriver dashboard, WebhookDriver webhook)
{
    [Then(@"the webhook is told that the alert ""(.*)"" for ""(.*)"" is (firing|resolved) within (\d+) seconds")]
    public async Task ThenTheWebhookIsTold(string rule, string subject, string status, int seconds)
    {
        try
        {
            await WaitForAsync(rule, subject, status, seconds);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"{ex.Message} The webhook has received: {string.Join(" | ", (await webhook.ReceivedAsync()).Select(m => m.Body))}");
        }
    }

    private Task WaitForAsync(string rule, string subject, string status, int seconds) =>
        dashboard.WaitUntilAsync($"the webhook is told that '{rule}' for '{subject}' is {status}", TimeSpan.FromSeconds(seconds), async () =>
            (await webhook.ReceivedAsync()).Any(m => m.Status == status && m.Rule == rule && m.Subject == subject));

    [Then(@"the webhook has been told exactly once that the alert ""(.*)"" for ""(.*)"" is firing, for the next (\d+) seconds")]
    public async Task ThenTheWebhookWasToldOnce(string rule, string subject, int seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            var told = (await webhook.ReceivedAsync()).Count(m => m.Status == "firing" && m.Rule == rule && m.Subject == subject);
            told.ShouldBeLessThanOrEqualTo(1);
            await Task.Delay(500);
        }
    }
}
