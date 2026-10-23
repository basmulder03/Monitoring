using System.Text.Json;
using Aspire.Hosting.Testing;
using Monitoring.AppHost;

namespace Monitoring.Specifications.Support;

/// <summary>An edge of the dependency graph, as the controller reports it.</summary>
/// <param name="From">Id of the node the calls start at.</param>
/// <param name="To">Id of the node they go to.</param>
/// <param name="Type">How it was called (HTTP, MQ, DB, TCP).</param>
/// <param name="Calls">How many calls were counted.</param>
/// <param name="Errors">How many of them failed.</param>
public sealed record GraphEdge(string From, string To, string Type, long Calls, long Errors);

/// <summary>An alert, as the dashboard lists it.</summary>
/// <param name="Rule">The rule that fired.</param>
/// <param name="Subject">What it is about.</param>
/// <param name="Severity">How serious it is.</param>
public sealed record ShownAlert(string Rule, string Subject, string Severity);

/// <summary>
/// What a person sees on the dashboard, read from the API behind it. Steps ask this driver and never touch HTTP or JSON themselves, so when the API changes one place changes;
/// and every question that has a delay is a <c>Wait...</c> with a deadline, because nothing in a monitoring system is instant.
/// </summary>
public sealed class DashboardDriver
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Reads a page of the API of the controller.</summary>
    /// <param name="path">The path, such as <c>/api/overview</c>.</param>
    /// <returns>The JSON, or <see langword="null"/> when the controller does not answer or does not have it (yet).</returns>
    public async Task<JsonDocument?> GetAsync(string path)
    {
        // A controller that is down leaves its port open for a while, so a request must not wait for the default hundred seconds.
        using var client = RunningEnvironment.App.CreateHttpClient(MonitoringEnvironment.Controller, "web");
        client.Timeout = RequestTimeout;
        client.DefaultRequestHeaders.Authorization = new("Bearer", MonitoringEnvironment.DashboardToken);
        try
        {
            using var response = await client.GetAsync(path);
            return response.IsSuccessStatusCode ? await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync()) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Asks for a page the way a visitor would, with or without the token, and says how the controller answered.</summary>
    /// <param name="path">The path.</param>
    /// <param name="token">The token to give in the address, or <see langword="null"/> for none.</param>
    /// <returns>The status code and whether the answer sets the cookie that remembers the token.</returns>
    public async Task<(int Status, bool SetsCookie)> VisitAsync(string path, string? token)
    {
        using var client = RunningEnvironment.App.CreateHttpClient(MonitoringEnvironment.Controller, "web");
        client.Timeout = RequestTimeout;
        using var response = await client.GetAsync(token is null ? path : $"{path}?token={Uri.EscapeDataString(token)}");
        return ((int)response.StatusCode, response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("monitoring_token=", StringComparison.Ordinal)));
    }

    /// <summary>Reads the page of a scope.</summary>
    /// <param name="scope">The scope.</param>
    /// <returns>The JSON, or <see langword="null"/>.</returns>
    public Task<JsonDocument?> ScopeAsync(string scope) => GetAsync($"/api/scopes/{scope}");

    /// <summary>Finds a service of a scope.</summary>
    /// <param name="scope">The scope.</param>
    /// <param name="service">The service.</param>
    /// <returns>Its health and the number of live instances, or <see langword="null"/> when the scope does not list it.</returns>
    public async Task<(string Health, int LiveInstances, int Instances)?> ServiceAsync(string scope, string service)
    {
        using var document = await ScopeAsync(scope);
        var found = document?.RootElement.GetProperty("services").EnumerateArray().FirstOrDefault(s => s.GetProperty("name").GetString() == service);
        return found is { ValueKind: JsonValueKind.Object } s
            ? (s.GetProperty("health").GetString()!, s.GetProperty("liveInstances").GetInt32(), s.GetProperty("instances").GetArrayLength())
            : null;
    }

    /// <summary>Reads the alerts that are shown now.</summary>
    /// <returns>The alerts; none when the controller does not answer.</returns>
    public async Task<IReadOnlyList<ShownAlert>> AlertsAsync()
    {
        using var document = await GetAsync("/api/overview");
        return document is null ? [] : [.. document.RootElement.GetProperty("alerts").EnumerateArray()
            .Select(a => new ShownAlert(a.GetProperty("rule").GetString()!, a.GetProperty("subject").GetString()!, a.GetProperty("severity").GetString()!))];
    }

    /// <summary>Reads the edges of the dependency graph.</summary>
    /// <returns>The edges; none when the controller does not answer.</returns>
    public async Task<IReadOnlyList<GraphEdge>> EdgesAsync()
    {
        using var document = await GetAsync("/api/topology");
        return document is null ? [] : [.. document.RootElement.GetProperty("edges").EnumerateArray()
            .Select(e => new GraphEdge(e.GetProperty("from").GetString()!, e.GetProperty("to").GetString()!, e.GetProperty("type").GetString()!, e.GetProperty("calls").GetInt64(), e.GetProperty("errors").GetInt64()))];
    }

    /// <summary>Reads a page and returns its text, for a message that shows what the dashboard said when a wait ran out.</summary>
    /// <param name="path">The path.</param>
    /// <returns>The text, or a note that there was none.</returns>
    public async Task<string> TextAsync(string path)
    {
        using var document = await GetAsync(path);
        return document?.RootElement.GetRawText() ?? "nothing";
    }

    /// <summary>Waits until a question about the dashboard has the wanted answer, and says what the dashboard showed when it never did.</summary>
    /// <param name="what">What is waited for, for the message.</param>
    /// <param name="within">The deadline.</param>
    /// <param name="holds">The question.</param>
    /// <param name="evidencePath">The page whose text is added to the message when the wait runs out.</param>
    /// <returns>A task that completes when it holds.</returns>
    public async Task WaitUntilAsync(string what, TimeSpan within, Func<Task<bool>> holds, string evidencePath = "/api/overview")
    {
        try
        {
            await Eventually.HoldsAsync(what, within, holds);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"{ex.Message}. {evidencePath} says: {await TextAsync(evidencePath)}");
        }
    }
}
