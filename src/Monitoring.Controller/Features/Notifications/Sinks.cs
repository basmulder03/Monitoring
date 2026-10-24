using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Monitoring.Controller.Features.Alerting;

namespace Monitoring.Controller.Features.Notifications;

/// <summary>The body a webhook receives.</summary>
/// <param name="Status"><c>firing</c> or <c>resolved</c>.</param>
/// <param name="Key">The stable key of the alert, which a receiver de-duplicates by.</param>
/// <param name="Rule">The rule that fired.</param>
/// <param name="Subject">What it is about.</param>
/// <param name="Severity"><c>warning</c> or <c>critical</c>.</param>
/// <param name="Since">When the condition began.</param>
/// <param name="Source">Which controller sent it.</param>
internal sealed record WebhookBody(string Status, string Key, string Rule, string Subject, string Severity, DateTimeOffset Since, string Source);

/// <summary>A chat message in the shape of Slack, which Mattermost and Google Chat accept as well.</summary>
/// <param name="Text">The message.</param>
internal sealed record SlackBody(string Text);

/// <summary>A chat message in the shape of Discord.</summary>
/// <param name="Content">The message.</param>
internal sealed record DiscordBody(string Content);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WebhookBody))]
[JsonSerializable(typeof(SlackBody))]
[JsonSerializable(typeof(DiscordBody))]
internal sealed partial class NotificationsJsonContext : JsonSerializerContext;

/// <summary>What a notification says, in words.</summary>
internal static class Wording
{
    /// <summary>Makes a one-line message.</summary>
    /// <param name="notification">The notification.</param>
    /// <param name="source">Which controller sends it.</param>
    /// <returns>The text.</returns>
    public static string Line(AlertNotification notification, string source)
    {
        var alert = notification.Alert;
        var mark = notification.Firing ? (alert.Severity == AlertSeverity.Critical ? "CRITICAL" : "WARNING") : "RESOLVED";
        return $"[{mark}] {alert.RuleName}: {alert.Subject} ({source}, since {alert.Since:u})";
    }
}

/// <summary>Posts each notification as JSON to a webhook.</summary>
/// <param name="http">Sends the request.</param>
/// <param name="target">Where, and with which secret.</param>
/// <param name="source">Which controller sends it.</param>
/// <param name="time">Clock for the timestamp that is signed with the body; the system clock unless a test sets one.</param>
internal sealed class WebhookSink(HttpClient http, WebhookTarget target, string source, TimeProvider? time = null) : IAlertSink
{
    /// <inheritdoc />
    public string Name => $"webhook:{target.Url.Host}";

    /// <inheritdoc />
    public async Task SendAsync(AlertNotification notification, CancellationToken ct)
    {
        var alert = notification.Alert;
        var body = new WebhookBody(notification.Firing ? "firing" : "resolved", alert.Key, alert.RuleName, alert.Subject, alert.Severity.ToString().ToLowerInvariant(), alert.Since, source);
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(body, NotificationsJsonContext.Default.WebhookBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, target.Url) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new("application/json");
        if (!string.IsNullOrEmpty(target.Secret))
        {
            // The time is part of what is signed, so a receiver that refuses old timestamps cannot be fed a captured request again.
            var timestamp = (time ?? TimeProvider.System).GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var signed = Encoding.UTF8.GetBytes(timestamp + ".").Concat(bytes).ToArray();
            request.Headers.Add("X-Monitoring-Timestamp", timestamp);
            request.Headers.Add("X-Monitoring-Signature", "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(target.Secret), signed)));
        }

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Posts each notification as a line of text to a chat channel.</summary>
/// <param name="http">Sends the request.</param>
/// <param name="target">Where, and in which shape.</param>
/// <param name="source">Which controller sends it.</param>
internal sealed class ChatSink(HttpClient http, ChatTarget target, string source) : IAlertSink
{
    /// <inheritdoc />
    public string Name => $"chat:{target.Url.Host}";

    /// <inheritdoc />
    public async Task SendAsync(AlertNotification notification, CancellationToken ct)
    {
        var text = Wording.Line(notification, source);
        using var response = target.Format.Equals("discord", StringComparison.OrdinalIgnoreCase)
            ? await http.PostAsJsonAsync(target.Url, new DiscordBody(text), NotificationsJsonContext.Default.DiscordBody, ct).ConfigureAwait(false)
            : await http.PostAsJsonAsync(target.Url, new SlackBody(text), NotificationsJsonContext.Default.SlackBody, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}

/// <summary>Sends a mail; the one place that talks SMTP, so the rest can be tested without a server.</summary>
internal interface ISmtpSender
{
    /// <summary>Sends one mail to the addresses.</summary>
    /// <param name="subject">The subject.</param>
    /// <param name="body">The text.</param>
    /// <param name="ct">Cancels the send.</param>
    /// <returns>A task that completes when the server has accepted it.</returns>
    Task SendAsync(string subject, string body, CancellationToken ct);
}

/// <summary>Sends each notification as a mail.</summary>
/// <param name="smtp">Sends the mail.</param>
/// <param name="host">The name of the server, for the channel's name.</param>
/// <param name="source">Which controller sends it.</param>
internal sealed class EmailSink(ISmtpSender smtp, string host, string source) : IAlertSink
{
    /// <inheritdoc />
    public string Name => $"email:{host}";

    /// <inheritdoc />
    public Task SendAsync(AlertNotification notification, CancellationToken ct)
    {
        var alert = notification.Alert;
        var line = Wording.Line(notification, source);
        var body = $"{line}{Environment.NewLine}{Environment.NewLine}Key: {alert.Key}{Environment.NewLine}Rule: {alert.RuleName}{Environment.NewLine}Subject: {alert.Subject}{Environment.NewLine}Severity: {alert.Severity}{Environment.NewLine}Since: {alert.Since:u}";
        return smtp.SendAsync(line, body, ct);
    }
}

/// <summary>Sends mail through an SMTP server.</summary>
/// <param name="options">The server and the addresses.</param>
internal sealed class SmtpSender(EmailTarget options) : ISmtpSender
{
    /// <inheritdoc />
    public async Task SendAsync(string subject, string body, CancellationToken ct)
    {
        using var client = new System.Net.Mail.SmtpClient(options.Host, options.Port) { EnableSsl = options.Ssl, Timeout = 15_000 };
        if (!string.IsNullOrEmpty(options.User))
        {
            client.Credentials = new System.Net.NetworkCredential(options.User, options.Password);
        }

        using var message = new System.Net.Mail.MailMessage { From = new System.Net.Mail.MailAddress(options.From!), Subject = subject, Body = body };
        foreach (var address in options.To)
        {
            message.To.Add(address);
        }

        await client.SendMailAsync(message, ct).ConfigureAwait(false);
    }
}
