using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Controller.Features.Alerting;
using Monitoring.Controller.Features.Configuration;
using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.StateStore;
using Monitoring.Controller.Features.WebAuth;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.Alerting;

public class AlertActionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string HashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    [Fact]
    public void A_silence_hides_the_alerts_it_covers_until_it_ends_and_can_be_taken_back()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var actions = new AlertActions(clock);

        // Act
        var silence = actions.Silence("instance-unhealthy", "shop/", clock.GetUtcNow().AddHours(1), "maintenance", "ann");
        var coveredBefore = actions.Silenced("instance-unhealthy", "shop/orders", clock.GetUtcNow());
        var otherRule = actions.Silenced("service-down", "shop/orders", clock.GetUtcNow());
        var otherScope = actions.Silenced("instance-unhealthy", "billing/x", clock.GetUtcNow());
        clock.Advance(TimeSpan.FromHours(2));
        var coveredAfter = actions.Silenced("instance-unhealthy", "shop/orders", clock.GetUtcNow());

        // Assert
        silence.By.ShouldBe("ann");
        coveredBefore.ShouldBeTrue();
        otherRule.ShouldBeFalse();
        otherScope.ShouldBeFalse();
        coveredAfter.ShouldBeFalse();
    }

    [Fact]
    public void Taking_a_silence_back_lets_the_alerts_show_again()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var actions = new AlertActions(clock);
        var silence = actions.Silence("*", "", clock.GetUtcNow().AddHours(1), null, "ann");

        // Act
        var removed = actions.RemoveSilence(silence.Id, "bob");
        var again = actions.RemoveSilence(silence.Id, "bob");

        // Assert
        removed.ShouldBeTrue();
        again.ShouldBeFalse();
        actions.Silenced("r", "a", clock.GetUtcNow()).ShouldBeFalse();
        actions.Silences().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", 60)]
    [InlineData("r", 0)]
    [InlineData("r", -5)]
    [InlineData("r", 60 * 24 * 32)]
    public void A_silence_needs_a_rule_and_an_end_that_is_in_the_future_and_not_too_far_away(string rule, int minutes)
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var actions = new AlertActions(clock);

        // Act
        var silence = () => actions.Silence(rule, "", clock.GetUtcNow().AddMinutes(minutes), null, "ann");

        // Assert
        silence.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void There_is_a_limit_to_the_silences_and_the_text_an_operator_types_is_kept_short_and_on_one_line()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var actions = new AlertActions(clock);
        var first = actions.Silence("r", "", clock.GetUtcNow().AddHours(1), "line one\nline two " + new string('x', 500), "ann");
        for (var i = 1; i < AlertActions.MaxSilences; i++)
        {
            actions.Silence("r", $"s{i}", clock.GetUtcNow().AddHours(1), null, "ann");
        }

        // Act
        var one = () => actions.Silence("r", "more", clock.GetUtcNow().AddHours(1), null, "ann");

        // Assert
        one.ShouldThrow<ArgumentException>();
        first.Reason!.Length.ShouldBe(200);
        first.Reason.ShouldNotContain("\n");
    }

    [Fact]
    public void An_acknowledgement_is_shown_on_the_alert_and_goes_when_the_problem_goes()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var actions = new AlertActions(clock);
        var rule = new FakeRule();
        var evaluator = new AlertEvaluator([rule], new AlertingOptions { StartupGrace = TimeSpan.Zero }, clock, actions);
        rule.Subjects = ["shop/a"];
        evaluator.Evaluate();
        var key = evaluator.GetActiveAlerts().Single().Key;

        // Act
        actions.Acknowledge(key, "ann", "restarting it");
        var acknowledged = evaluator.GetActiveAlerts().Single().Acknowledged;
        rule.Subjects = [];
        evaluator.Evaluate();
        rule.Subjects = ["shop/a"];
        evaluator.Evaluate();
        var afterRecurrence = evaluator.GetActiveAlerts().Single().Acknowledged;

        // Assert
        acknowledged!.By.ShouldBe("ann");
        acknowledged.Note.ShouldBe("restarting it");
        afterRecurrence.ShouldBeNull();
    }

    [Fact]
    public void An_evaluator_leaves_out_what_an_operator_silenced()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var actions = new AlertActions(clock);
        var rule = new FakeRule { Subjects = ["shop/a", "billing/b"] };
        var evaluator = new AlertEvaluator([rule], new AlertingOptions { StartupGrace = TimeSpan.Zero }, clock, actions);
        evaluator.Evaluate();

        // Act
        actions.Silence("r", "shop/", clock.GetUtcNow().AddHours(1), "maintenance", "ann");

        // Assert
        evaluator.GetActiveAlerts().Select(a => a.Subject).ShouldBe(["billing/b"]);
    }

    [Fact]
    public void Replicas_take_over_the_latest_change_of_each_silence_and_acknowledgement()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var first = new AlertActions(clock);
        var second = new AlertActions(clock);
        var silence = first.Silence("r", "shop/", clock.GetUtcNow().AddHours(1), "x", "ann");
        first.Acknowledge("r:shop/a", "ann", null);
        second.Merge(first.Export());
        clock.Advance(TimeSpan.FromMinutes(1));

        // Act: the second replica takes the silence back and the first learns of it
        second.RemoveSilence(silence.Id, "bob");
        second.RemoveAcknowledgement("r:shop/a", "bob");
        first.Merge(second.Export());
        // An older copy must not bring it back
        second.Merge(new ActionsExport([silence], []));

        // Assert
        first.Silences().ShouldBeEmpty();
        first.AcknowledgementOf("r:shop/a").ShouldBeNull();
        second.Silences().ShouldBeEmpty();
    }

    [Fact]
    public void What_operators_did_survives_a_restart_when_the_state_store_keeps_it()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        IStateStore store = new MemoryStateStore();
        var before = new AlertActions(clock, store);
        before.Silence("r", "shop/", clock.GetUtcNow().AddHours(1), "x", "ann");
        before.Acknowledge("r:shop/a", "ann", "on it");

        // Act
        var after = new AlertActions(clock, store);

        // Assert
        after.Silences().Single().SubjectPrefix.ShouldBe("shop/");
        after.AcknowledgementOf("r:shop/a")!.Note.ShouldBe("on it");
    }

    // ---- the routes ----

    private static async Task<HttpClient> ServerAsync(AlertActions actions, bool authentication = true, AlertHistory? history = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(actions);
        builder.Services.AddSingleton(history ?? new AlertHistory(new FakeTimeProvider()));
        builder.Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        builder.Services.AddSingleton(new AlertingOptions());
        builder.Services.AddWebAuth(o =>
        {
            if (authentication)
            {
                o.Tokens.Add(new WebToken("viewer", HashOf("viewer-secret"), WebRole.Viewer));
                o.Tokens.Add(new WebToken("ann", HashOf("operator-secret"), WebRole.Operator));
            }
        });
        var app = builder.Build();
        app.UseWebAuth();
        app.MapAlertActions();
        await app.StartAsync(Ct);
        return app.GetTestClient();
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string? token, object? body = null, bool actionHeader = true)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }

        if (actionHeader)
        {
            request.Headers.Add(AlertActionsEndpoints.ActionHeader, "1");
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    [Fact]
    public async Task An_operator_can_make_and_take_back_a_silence_and_acknowledge_an_alert()
    {
        // Arrange
        var actions = new AlertActions(new FakeTimeProvider());
        using var client = await ServerAsync(actions);

        // Act
        var made = await client.SendAsync(Request(HttpMethod.Post, "/api/silences", "operator-secret", new { rule = "r", subjectPrefix = "shop/", minutes = 60, reason = "maintenance" }), Ct);
        var silence = actions.Silences().Single();
        var acknowledged = await client.SendAsync(Request(HttpMethod.Post, "/api/alerts/acknowledge", "operator-secret", new { key = "r:shop/a", note = "on it" }), Ct);
        var removed = await client.SendAsync(Request(HttpMethod.Delete, $"/api/silences/{silence.Id}", "operator-secret"), Ct);

        // Assert
        made.StatusCode.ShouldBe(HttpStatusCode.Created);
        acknowledged.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        silence.By.ShouldBe("ann");
        actions.AcknowledgementOf("r:shop/a")!.By.ShouldBe("ann");
        actions.Silences().ShouldBeEmpty();
    }

    [Fact]
    public async Task Everyone_who_may_see_the_dashboard_can_read_what_fired_lately_and_what_operators_did()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var history = new AlertHistory(clock);
        history.Update(new Dictionary<string, Alert> { ["r:shop/a"] = new("r:shop/a", "r", "shop/a", AlertSeverity.Critical, clock.GetUtcNow()) }, clock.GetUtcNow());
        var actions = new AlertActions(clock);
        actions.Silence("r", "shop/", clock.GetUtcNow().AddHours(1), "x", "ann");
        using var client = await ServerAsync(actions, history: history);

        // Act
        var recent = await client.SendAsync(Request(HttpMethod.Get, "/api/alerts/history?limit=5", "viewer-secret", actionHeader: false), Ct);
        var exported = await client.SendAsync(Request(HttpMethod.Get, "/api/actions", "viewer-secret", actionHeader: false), Ct);

        // Assert
        recent.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = await recent.Content.ReadAsStringAsync(Ct);
        text.ShouldContain("shop/a");
        text.ShouldContain("\"severity\":\"Critical\"");  // as text, like everywhere else in the API
        exported.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await exported.Content.ReadAsStringAsync(Ct)).ShouldContain("shop/");
    }

    [Fact]
    public async Task A_viewer_can_read_but_not_change()
    {
        // Arrange
        var actions = new AlertActions(new FakeTimeProvider());
        using var client = await ServerAsync(actions);

        // Act
        var read = await client.SendAsync(Request(HttpMethod.Get, "/api/silences", "viewer-secret", actionHeader: false), Ct);
        var write = await client.SendAsync(Request(HttpMethod.Post, "/api/silences", "viewer-secret", new { rule = "r", minutes = 5 }), Ct);

        // Assert
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        actions.Silences().ShouldBeEmpty();
    }

    [Fact]
    public async Task Nothing_can_be_changed_on_a_dashboard_that_needs_no_sign_in()
    {
        // Arrange
        var actions = new AlertActions(new FakeTimeProvider());
        using var client = await ServerAsync(actions, authentication: false);

        // Act
        var write = await client.SendAsync(Request(HttpMethod.Post, "/api/silences", null, new { rule = "*", minutes = 5 }), Ct);

        // Assert
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        actions.Silences().ShouldBeEmpty();
    }

    [Fact]
    public async Task A_request_without_the_action_header_is_refused_even_from_an_operator()
    {
        // Arrange
        var actions = new AlertActions(new FakeTimeProvider());
        using var client = await ServerAsync(actions);

        // Act
        var write = await client.SendAsync(Request(HttpMethod.Post, "/api/silences", "operator-secret", new { rule = "*", minutes = 5 }, actionHeader: false), Ct);

        // Assert
        write.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        actions.Silences().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{ "rule": "r" }""")]
    [InlineData("""{ "rule": "r", "minutes": 5, "until": "2030-01-01T00:00:00Z" }""")]
    [InlineData("""{ "rule": "r", "minutes": 0 }""")]
    [InlineData("""{ "rule": "r", "minutes": 999999 }""")]
    [InlineData("not json")]
    public async Task A_silence_that_says_nothing_sensible_about_how_long_is_refused(string body)
    {
        // Arrange
        var actions = new AlertActions(new FakeTimeProvider());
        using var client = await ServerAsync(actions);
        var request = Request(HttpMethod.Post, "/api/silences", "operator-secret");
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        // Act
        var response = await client.SendAsync(request, Ct);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        actions.Silences().ShouldBeEmpty();
    }

    // ---- copying from the other replicas ----

    [Fact]
    public async Task A_replica_copies_what_the_others_know_and_does_not_mind_one_that_is_down()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        var other = new AlertActions(clock);
        other.Silence("r", "shop/", clock.GetUtcNow().AddHours(1), "maintenance", "ann");
        var json = System.Text.Json.JsonSerializer.Serialize(other.Export(), AlertActionsJsonContext.Default.ActionsExport);
        var seen = new List<(Uri Uri, string? Authorization)>();
        var handler = new ReplyingHandler(request =>
        {
            seen.Add((request.RequestUri!, request.Headers.Authorization?.ToString()));
            return request.RequestUri!.Host == "up"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });
        var mine = new AlertActions(clock);
        var options = new AlertingOptions { Peers = [new Uri("http://down:8080"), new Uri("http://up:8080")], PeerToken = "viewer-secret" };
        var sync = new AlertActionsSync(new HttpClient(handler), mine, options, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        // Act
        await sync.SyncAsync(Ct);

        // Assert
        mine.Silences().Single().Reason.ShouldBe("maintenance");
        seen.Select(r => r.Uri.AbsoluteUri).ShouldBe(["http://down:8080/api/actions", "http://up:8080/api/actions"]);
        seen.ShouldAllBe(r => r.Authorization == "Bearer viewer-secret");
    }

    [Fact]
    public async Task The_replica_that_ranks_first_among_those_it_can_reach_is_the_one_that_sends()
    {
        // Arrange: another data center's replica, rank 10, and this one, rank 0 or 20
        var clock = new FakeTimeProvider();
        var up = true;
        var handler = new ReplyingHandler(_ =>
        {
            if (!up)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            var reply = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"silences\":[],\"acknowledgements\":[]}") };
            reply.Headers.Add(AlertActionsSync.ReplicaHeader, "10/dc2-controller-0");
            return reply;
        });
        AlertActionsSync Replica(int rank) => new(new HttpClient(handler), new AlertActions(clock),
            new AlertingOptions { Peers = [new Uri("http://dc2:8080")], ReplicaId = "dc1-controller-0", ReplicaRank = rank }, clock, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        var first = Replica(0);
        var second = Replica(20);

        // Act
        await first.SyncAsync(Ct);
        await second.SyncAsync(Ct);
        var whileLinked = (first.IsFirst, second.IsFirst);
        up = false;
        clock.Advance(TimeSpan.FromSeconds(31));
        await second.SyncAsync(Ct);
        var afterLinkBroke = second.IsFirst;

        // Assert
        whileLinked.ShouldBe((true, false));
        afterLinkBroke.ShouldBeTrue(); // the peer cannot be reached, so both sides send: told twice rather than not at all
    }

    [Fact]
    public void A_peer_has_to_be_an_http_or_https_address()
    {
        // Act
        var add = () => new ServiceCollection().AddRegistry().AddLiveness().AddConfiguration().AddAlerting(o => o.Peers = [new Uri("ftp://other:21")]);

        // Assert
        add.ShouldThrow<ArgumentException>();
    }

    private sealed class ReplyingHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(reply(request));
    }

    private sealed class FakeRule : IAlertRule
    {
        public string Name => "r";

        public AlertSeverity Severity => AlertSeverity.Warning;

        public TimeSpan Wait => TimeSpan.Zero;

        public string[] Subjects { get; set; } = [];

        public IEnumerable<string> FindFailingSubjects() => Subjects;
    }
}
