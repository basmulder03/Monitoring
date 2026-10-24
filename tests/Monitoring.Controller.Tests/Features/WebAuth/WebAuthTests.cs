using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Monitoring.Controller.Features.WebAuth;
using Shouldly;
using Xunit;

namespace Monitoring.Controller.Tests.Features.WebAuth;

public class WebAuthTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string HashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static HttpContext Request(string? remote = "10.1.2.3", string? bearer = null, string? user = null, string? groups = null, string? cookie = null, string? query = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remote is null ? null : IPAddress.Parse(remote);
        if (bearer is not null) { context.Request.Headers.Authorization = $"Bearer {bearer}"; }
        if (user is not null) { context.Request.Headers["X-Forwarded-User"] = user; }
        if (groups is not null) { context.Request.Headers["X-Forwarded-Groups"] = groups; }
        if (cookie is not null) { context.Request.Headers.Cookie = $"{WebAuthenticator.CookieName}={cookie}"; }
        if (query is not null) { context.Request.QueryString = new QueryString($"?token={query}"); }
        return context;
    }

    private static WebAuthenticator Make(Action<WebAuthOptions>? configure = null)
    {
        var options = new WebAuthOptions();
        configure?.Invoke(options);
        return new WebAuthenticator(options);
    }

    [Fact]
    public void Nothing_is_asked_until_a_proxy_is_trusted_or_a_token_is_configured()
    {
        Make().Enabled.ShouldBeFalse();
        Make(o => o.TrustedProxies.Add("10.0.0.0/8")).Enabled.ShouldBeTrue();
        Make(o => o.Tokens.Add(new WebToken("tv", HashOf("x")))).Enabled.ShouldBeTrue();
    }

    [Fact]
    public void A_trusted_proxy_names_the_person_and_the_operator_group_makes_an_operator()
    {
        var auth = Make(o => { o.TrustedProxies.Add("10.0.0.0/8"); o.OperatorGroup = "ops"; });

        var viewer = auth.Identify(Request(user: "ann", groups: "dev, qa").Request);
        var operatorUser = auth.Identify(Request(user: "bob", groups: "dev, ops").Request);

        viewer.ShouldBe(new WebUser("ann", WebRole.Viewer));
        operatorUser.ShouldBe(new WebUser("bob", WebRole.Operator));
    }

    [Theory]
    [InlineData("192.168.1.5")]
    [InlineData("11.0.0.1")]
    public void The_header_of_an_address_that_is_not_a_trusted_proxy_is_ignored_because_anyone_could_write_it(string remote)
    {
        var auth = Make(o => o.TrustedProxies.Add("10.0.0.0/8"));

        auth.Identify(Request(remote, user: "ann").Request).ShouldBeNull();
    }

    [Fact]
    public void A_proxy_can_be_a_single_address_and_an_ipv4_peer_seen_as_ipv6_is_still_recognised()
    {
        var auth = Make(o => o.TrustedProxies.Add("10.9.9.9"));

        auth.Identify(Request("10.9.9.9", user: "ann").Request).ShouldNotBeNull();
        auth.Identify(Request("::ffff:10.9.9.9", user: "ann").Request).ShouldNotBeNull();
        auth.Identify(Request("10.9.9.8", user: "ann").Request).ShouldBeNull();
    }

    [Theory]
    [InlineData("bearer")]
    [InlineData("cookie")]
    [InlineData("query")]
    public void A_token_works_in_the_header_the_cookie_or_the_address(string how)
    {
        var auth = Make(o => o.Tokens.Add(new WebToken("wall", HashOf("long-secret"), WebRole.Viewer)));
        var request = how switch
        {
            "bearer" => Request(bearer: "long-secret"),
            "cookie" => Request(cookie: "long-secret"),
            _ => Request(query: "long-secret"),
        };

        auth.Identify(request.Request).ShouldBe(new WebUser("wall", WebRole.Viewer, ViaToken: true));
    }

    [Theory]
    [InlineData("wrong")]
    [InlineData("")]
    public void A_wrong_or_missing_token_is_not_accepted(string token)
    {
        var auth = Make(o => o.Tokens.Add(new WebToken("wall", HashOf("long-secret"))));

        auth.Identify(Request(bearer: token.Length == 0 ? null : token).Request).ShouldBeNull();
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("10.0.0.0/40")]
    public void A_proxy_that_is_not_an_address_or_network_is_rejected(string proxy)
    {
        var make = () => Make(o => o.TrustedProxies.Add(proxy));

        make.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void A_token_whose_hash_is_not_a_hash_is_rejected()
    {
        var make = () => Make(o => o.Tokens.Add(new WebToken("wall", "plaintext-secret")));

        make.ShouldThrow<ArgumentException>();
    }

    private static async Task<HttpClient> ServerAsync(Action<WebAuthOptions> configure)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddWebAuth(configure);
        var app = builder.Build();
        app.UseWebAuth();
        app.MapGet("/api/overview", () => "data");
        await app.StartAsync(Ct);
        return app.GetTestClient();
    }

    [Fact]
    public async Task With_a_token_configured_every_page_asks_who_is_looking_except_the_health_probe()
    {
        using var client = await ServerAsync(o => o.Tokens.Add(new WebToken("wall", HashOf("long-secret"))));

        (await client.GetAsync("/api/overview", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/healthz", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/overview");
        request.Headers.Authorization = new("Bearer", "long-secret");
        (await client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_token_in_the_address_is_kept_in_a_cookie_that_script_cannot_read()
    {
        using var client = await ServerAsync(o => o.Tokens.Add(new WebToken("wall", HashOf("long-secret"))));

        using var response = await client.GetAsync("/api/overview?token=long-secret", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.ShouldContain($"{WebAuthenticator.CookieName}=long-secret");
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);

        using var wall = await client.GetAsync("/tv?token=long-secret", Ct);
        wall.Headers.GetValues("Set-Cookie").Single().ShouldContain($"{WebAuthenticator.CookieName}=long-secret");
    }

    [Fact]
    public async Task Without_any_setting_the_dashboard_is_open_as_before_and_says_so()
    {
        using var client = await ServerAsync(_ => { });

        (await client.GetAsync("/api/overview", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetStringAsync("/api/me", Ct)).ShouldContain("\"authenticationEnabled\":false");
    }

    [Fact]
    public async Task The_page_can_ask_who_is_signed_in()
    {
        using var client = await ServerAsync(o => o.Tokens.Add(new WebToken("wall", HashOf("long-secret"), WebRole.Operator)));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new("Bearer", "long-secret");

        var body = await (await client.SendAsync(request, Ct)).Content.ReadAsStringAsync(Ct);

        body.ShouldBe("{\"authenticationEnabled\":true,\"name\":\"wall\",\"role\":\"operator\"}");
    }
}
