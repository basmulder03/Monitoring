using System.Security.Cryptography;
using System.Text;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Notifications;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Notifications;

public class SinksTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly Alert Sample = new("instance-unhealthy:shop/orders", "instance-unhealthy", "shop/orders", AlertSeverity.Critical, new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));

    private sealed class Capture : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = "";

        public System.Net.HttpStatusCode Status { get; set; } = System.Net.HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status);
        }
    }

    [Fact]
    public async Task A_webhook_gets_the_alert_as_json_with_the_stable_key_and_a_signature_when_there_is_a_secret()
    {
        var capture = new Capture();
        var sink = new WebhookSink(new HttpClient(capture), new WebhookTarget(new Uri("https://hooks.example/alerts"), "s3cret"), "prod-a");

        await sink.SendAsync(new AlertNotification(true, Sample), Ct);

        capture.Body.ShouldContain("\"status\":\"firing\"");
        capture.Body.ShouldContain("\"key\":\"instance-unhealthy:shop/orders\"");
        capture.Body.ShouldContain("\"severity\":\"critical\"");
        capture.Body.ShouldContain("\"source\":\"prod-a\"");
        var expected = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("s3cret"), Encoding.UTF8.GetBytes(capture.Body)));
        capture.Request!.Headers.GetValues("X-Monitoring-Signature").Single().ShouldBe(expected);
    }

    [Fact]
    public async Task A_webhook_without_a_secret_sends_no_signature_and_says_resolved_when_the_alert_ended()
    {
        var capture = new Capture();
        var sink = new WebhookSink(new HttpClient(capture), new WebhookTarget(new Uri("https://hooks.example/alerts")), "prod-a");

        await sink.SendAsync(new AlertNotification(false, Sample), Ct);

        capture.Body.ShouldContain("\"status\":\"resolved\"");
        capture.Request!.Headers.Contains("X-Monitoring-Signature").ShouldBeFalse();
    }

    [Fact]
    public async Task A_receiver_that_refuses_makes_the_send_fail_so_it_is_tried_again()
    {
        var sink = new WebhookSink(new HttpClient(new Capture { Status = System.Net.HttpStatusCode.InternalServerError }), new WebhookTarget(new Uri("https://hooks.example/alerts")), "prod-a");

        var send = () => sink.SendAsync(new AlertNotification(true, Sample), Ct);

        await send.ShouldThrowAsync<HttpRequestException>();
    }

    [Theory]
    [InlineData("slack", "\"text\":")]
    [InlineData("discord", "\"content\":")]
    public async Task A_chat_gets_one_line_in_the_shape_it_expects(string format, string field)
    {
        var capture = new Capture();
        var sink = new ChatSink(new HttpClient(capture), new ChatTarget(new Uri("https://chat.example/hook"), format), "prod-a");

        await sink.SendAsync(new AlertNotification(true, Sample), Ct);

        capture.Body.ShouldContain(field);
        capture.Body.ShouldContain("CRITICAL");
        capture.Body.ShouldContain("shop/orders");
    }

    [Fact]
    public async Task An_email_has_the_line_as_subject_and_the_details_in_the_body()
    {
        var smtp = new FakeSmtp();

        await new EmailSink(smtp, "smtp.example", "prod-a").SendAsync(new AlertNotification(false, Sample), Ct);

        smtp.Subject.ShouldStartWith("[RESOLVED] instance-unhealthy: shop/orders");
        smtp.Body.ShouldContain("Key: instance-unhealthy:shop/orders");
    }

    private sealed class FakeSmtp : ISmtpSender
    {
        public string Subject { get; private set; } = "";

        public string Body { get; private set; } = "";

        public Task SendAsync(string subject, string body, CancellationToken ct)
        {
            Subject = subject;
            Body = body;
            return Task.CompletedTask;
        }
    }
}
