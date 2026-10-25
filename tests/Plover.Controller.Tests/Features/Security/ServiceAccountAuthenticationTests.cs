using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Plover.Controller.Features.Security;
using Plover.Protocol.Messages;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Security;

public class ServiceAccountAuthenticationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private const string Jwt = "header.payload.signature";

    private static Hello HelloWith(string? token, string? scope = null) =>
        new(1, "agent-1", "plover-agent", null, new Dictionary<string, string>(), token, scope);

    private sealed class FakeReviewer(string? user = null, Exception? failure = null) : ITokenReviewer
    {
        public List<(string Token, string Audience)> Reviewed { get; } = [];

        public ValueTask<string?> ReviewAsync(string token, string audience, CancellationToken ct)
        {
            Reviewed.Add((token, audience));
            return failure is null ? ValueTask.FromResult(user) : throw failure;
        }
    }

    [Fact]
    public async Task An_agent_with_the_token_of_a_service_account_reports_as_the_scope_of_its_namespace()
    {
        // Arrange
        var reviewer = new FakeReviewer("system:serviceaccount:shop:plover-agent");
        var authenticator = new ServiceAccountAgentAuthenticator(reviewer, new ServiceAccountOptions { Audience = "plover-controller" });

        // Act
        var identity = await authenticator.AuthenticateAsync(HelloWith(Jwt, "shop"), Ct);

        // Assert
        identity.ShouldBe(new AgentIdentity("shop"));
        reviewer.Reviewed.ShouldBe([(Jwt, "plover-controller")]);
    }

    [Fact]
    public async Task An_agent_that_announces_nothing_reports_as_its_own_scope_but_never_as_another()
    {
        // Arrange
        var authenticator = new ServiceAccountAgentAuthenticator(new FakeReviewer("system:serviceaccount:shop:plover-agent"), new ServiceAccountOptions());

        // Act
        var silent = await authenticator.AuthenticateAsync(HelloWith(Jwt), Ct);
        var other = await authenticator.AuthenticateAsync(HelloWith(Jwt, "billing"), Ct);

        // Assert
        silent.ShouldBe(new AgentIdentity("shop"));
        other.ShouldBeNull();
    }

    [Fact]
    public async Task A_namespace_can_be_given_a_scope_of_another_name()
    {
        // Arrange
        var options = new ServiceAccountOptions { Scopes = { ["shop-prod"] = "shop" } };
        var authenticator = new ServiceAccountAgentAuthenticator(new FakeReviewer("system:serviceaccount:shop-prod:agent"), options);

        // Act
        var identity = await authenticator.AuthenticateAsync(HelloWith(Jwt, "shop"), Ct);

        // Assert
        identity.ShouldBe(new AgentIdentity("shop"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("alice")]
    [InlineData("system:node:worker-1")]
    [InlineData("system:serviceaccount:shop")]
    [InlineData("system:serviceaccount::agent")]
    public async Task A_token_that_does_not_belong_to_a_service_account_is_refused(string? user)
    {
        // Arrange
        var authenticator = new ServiceAccountAgentAuthenticator(new FakeReviewer(user), new ServiceAccountOptions());

        // Act
        var identity = await authenticator.AuthenticateAsync(HelloWith(Jwt), Ct);

        // Assert
        identity.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a-plain-agent-token")]
    public async Task A_token_that_is_not_shaped_like_one_of_a_service_account_does_not_cost_a_question_to_the_api_server(string? token)
    {
        // Arrange
        var reviewer = new FakeReviewer("system:serviceaccount:shop:agent");
        var authenticator = new ServiceAccountAgentAuthenticator(reviewer, new ServiceAccountOptions());

        // Act
        var identity = await authenticator.AuthenticateAsync(HelloWith(token), Ct);

        // Assert
        identity.ShouldBeNull();
        reviewer.Reviewed.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_agent_that_cannot_be_checked_because_the_api_server_cannot_be_reached_is_refused_without_a_crash()
    {
        // Arrange
        var authenticator = new ServiceAccountAgentAuthenticator(new FakeReviewer(failure: new HttpRequestException("connection refused")), new ServiceAccountOptions());

        // Act
        var identity = await authenticator.AuthenticateAsync(HelloWith(Jwt), Ct);

        // Assert
        identity.ShouldBeNull();
    }

    [Fact]
    public async Task The_composite_accepts_what_any_of_its_authenticators_accepts_in_order()
    {
        // Arrange
        var staticTokens = new TokenAgentAuthenticator(new SecurityOptions { AgentTokens = [new AgentTokenEntry("shop", AgentTokenHash.Compute("secret"))] });
        var reviewer = new FakeReviewer("system:serviceaccount:blog:agent");
        var composite = new CompositeAgentAuthenticator(staticTokens, new ServiceAccountAgentAuthenticator(reviewer, new ServiceAccountOptions()));

        // Act
        var byToken = await composite.AuthenticateAsync(HelloWith("secret", "shop"), Ct);
        var byAccount = await composite.AuthenticateAsync(HelloWith(Jwt, "blog"), Ct);
        var neither = await composite.AuthenticateAsync(HelloWith("wrong"), Ct);

        // Assert
        byToken.ShouldBe(new AgentIdentity("shop"));
        byAccount.ShouldBe(new AgentIdentity("blog"));
        neither.ShouldBeNull();
        reviewer.Reviewed.Count.ShouldBe(1);
    }

    private sealed class StubApiServer(string answer, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public List<(string Path, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, "application/json") };
        }
    }

    private static KubernetesTokenReviewer Reviewer(StubApiServer server) =>
        new(new HttpClient(server) { BaseAddress = new Uri("https://kubernetes.test/") });

    [Fact]
    public async Task The_api_server_is_asked_with_a_token_review_for_the_audience()
    {
        // Arrange
        var server = new StubApiServer("""{"status":{"authenticated":true,"user":{"username":"system:serviceaccount:shop:agent"},"audiences":["plover-controller"]}}""");

        // Act
        var user = await Reviewer(server).ReviewAsync(Jwt, "plover-controller", Ct);

        // Assert
        user.ShouldBe("system:serviceaccount:shop:agent");
        var (path, body) = server.Requests.Single();
        path.ShouldBe("/apis/authentication.k8s.io/v1/tokenreviews");
        using var json = JsonDocument.Parse(body);
        json.RootElement.GetProperty("kind").GetString().ShouldBe("TokenReview");
        json.RootElement.GetProperty("spec").GetProperty("token").GetString().ShouldBe(Jwt);
        json.RootElement.GetProperty("spec").GetProperty("audiences")[0].GetString().ShouldBe("plover-controller");
    }

    [Theory]
    [InlineData("""{"status":{"authenticated":false,"error":"token expired"}}""")]
    [InlineData("""{"status":{"authenticated":true,"user":{"username":"system:serviceaccount:shop:agent"},"audiences":["something-else"]}}""")]
    [InlineData("""{"status":{"authenticated":true,"user":{"username":"system:serviceaccount:shop:agent"}}}""")]
    [InlineData("""{}""")]
    public async Task A_token_the_api_server_does_not_accept_or_that_is_made_for_another_audience_gives_nobody(string answer)
    {
        // Act
        var user = await Reviewer(new StubApiServer(answer)).ReviewAsync(Jwt, "plover-controller", Ct);

        // Assert
        user.ShouldBeNull();
    }

    [Fact]
    public async Task An_api_server_that_refuses_the_controller_is_a_fault_and_not_an_unknown_agent()
    {
        // Arrange
        var reviewer = Reviewer(new StubApiServer("""{"message":"forbidden"}""", HttpStatusCode.Forbidden));

        // Act
        var review = async () => await reviewer.ReviewAsync(Jwt, "plover-controller", Ct);

        // Assert
        await review.ShouldThrowAsync<HttpRequestException>();
    }

    [Fact]
    public void Accepting_service_accounts_needs_an_api_server_to_ask_when_the_controller_does_not_run_in_a_cluster()
    {
        // Act
        var add = () => new ServiceCollection().AddSecurity(options => options.ServiceAccounts.Enabled = true);

        // Assert
        add.ShouldThrow<ArgumentException>().Message.ShouldContain("API server");
    }

    [Fact]
    public async Task With_service_accounts_enabled_the_configured_tokens_still_work()
    {
        // Arrange
        var services = new ServiceCollection().AddLogging().AddSecurity(options =>
        {
            options.AgentTokens.Add(new AgentTokenEntry("shop", AgentTokenHash.Compute("secret")));
            options.ServiceAccounts.Enabled = true;
            options.ServiceAccounts.ApiServer = "https://127.0.0.1:1";
        });
        using var provider = services.BuildServiceProvider();

        // Act
        var identity = await provider.GetRequiredService<IAgentAuthenticator>().AuthenticateAsync(HelloWith("secret", "shop"), Ct);

        // Assert
        identity.ShouldBe(new AgentIdentity("shop"));
    }
}
