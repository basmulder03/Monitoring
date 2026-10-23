using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Monitoring.AppHost;

namespace Monitoring.Specifications.Support;

/// <summary>What the receiver of the alert webhook has been sent; the receiver is part of the fake cluster, which keeps the bodies for the scenarios to read.</summary>
public sealed class WebhookDriver
{
    /// <summary>One message the webhook received.</summary>
    /// <param name="Status"><c>firing</c> or <c>resolved</c>.</param>
    /// <param name="Rule">The rule of the alert.</param>
    /// <param name="Subject">What the alert is about.</param>
    /// <param name="Body">The body exactly as it arrived.</param>
    public sealed record Message(string Status, string Rule, string Subject, string Body);

    /// <summary>Reads what the receiver has got.</summary>
    /// <returns>The messages, oldest first.</returns>
    public async Task<IReadOnlyList<Message>> ReceivedAsync()
    {
        using var client = RunningEnvironment.App.CreateHttpClient(MonitoringEnvironment.Cluster, "api");
        client.Timeout = TimeSpan.FromSeconds(5);
        using var document = JsonDocument.Parse(await client.GetStringAsync("/receive/webhook"));
        return [.. document.RootElement.EnumerateArray().Select(m => new Message(
            m.GetProperty("status").GetString()!, m.GetProperty("rule").GetString()!, m.GetProperty("subject").GetString()!, m.GetRawText()))];
    }

    /// <summary>Makes the receiver forget what it got, so a scenario only sees its own messages.</summary>
    /// <returns>A task that completes when it has forgotten.</returns>
    public async Task ForgetAsync()
    {
        using var client = RunningEnvironment.App.CreateHttpClient(MonitoringEnvironment.Cluster, "api");
        using var response = await client.DeleteAsync("/receive/webhook");
        response.EnsureSuccessStatusCode();
    }

    /// <summary>The signature the controller should have put on a body.</summary>
    /// <param name="body">The body as received.</param>
    /// <returns>The value of the signature header.</returns>
    public static string SignatureOf(string body) => "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(MonitoringEnvironment.WebhookSecret), Encoding.UTF8.GetBytes(body)));
}
