using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Plover.Controller.Features.Vulnerabilities;
using Shouldly;
using Xunit;

namespace Plover.Controller.Tests.Features.Vulnerabilities;

public class VulnerabilitiesEndpointsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Vulnerabilities_answers_with_the_findings_as_json()
    {
        // Arrange
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IVulnerabilities>(new FixedCheck());
        await using var app = builder.Build();
        app.MapVulnerabilities();
        await app.StartAsync(Ct);
        using var client = app.GetTestClient();

        // Act
        using var json = JsonDocument.Parse(await client.GetStringAsync("/api/vulnerabilities", Ct));

        // Assert
        var finding = json.RootElement.GetProperty("findings")[0];
        finding.GetProperty("advisory").GetString().ShouldBe("GHSA-1");
        finding.GetProperty("severity").GetString().ShouldBe("HIGH");
        finding.GetProperty("fixedIn").GetString().ShouldBe("2.0.0");
        json.RootElement.GetProperty("advisories").GetInt32().ShouldBe(5);
        await app.StopAsync(Ct);
    }

    private sealed class FixedCheck : IVulnerabilities
    {
        public VulnerabilityReport Check() => new(
            [new VulnerabilityFinding("GHSA-1", ["CVE-1"], "bad", "HIGH", "Serilog", "1.0.0", "2.0.0", [])], 5, null);
    }
}
