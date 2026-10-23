using System.Runtime.CompilerServices;
using Monitoring.Controller.Features.Dashboard;
using Monitoring.Controller.Features.Packages;
using Monitoring.Controller.Features.SelfMonitoring;
using Monitoring.Controller.Features.WebAuth;
using Monitoring.Controller.Features.Topology;
using Monitoring.Controller.Features.Vulnerabilities;
using Monitoring.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Monitoring.Ui.Tests.Contracts;

/// <summary>Keeps the TypeScript types of the dashboard in step with the C# records the controller sends.</summary>
public class ContractsTests
{
    [Fact]
    public async Task The_typescript_contracts_match_the_types_the_controller_sends()
    {
        // Arrange
        var path = ContractsPath();
        var expected = new ContractWriter().Add(typeof(Overview)).Add(typeof(ScopeDetail)).Add(typeof(TopologyGraph)).Add(typeof(PackageCatalog)).Add(typeof(VulnerabilityReport)).Add(typeof(SystemStatus)).Add(typeof(MetricHistory)).Add(typeof(WhoAmI)).Write();

        if (Environment.GetEnvironmentVariable("UPDATE_CONTRACTS") == "1")
        {
            await File.WriteAllTextAsync(path, expected, TestContext.Current.CancellationToken);
        }

        // Act
        var actual = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Assert: a mismatch means a C# type changed; regenerate the file and fix the page where it no longer type-checks
        actual.ReplaceLineEndings().ShouldBe(expected.ReplaceLineEndings(), "contracts.ts is out of date. Run: UPDATE_CONTRACTS=1 dotnet test --project tests/Monitoring.Ui.Tests");
    }

    private static string ContractsPath([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "..", "src", "Monitoring.Ui", "ui", "src", "api", "contracts.ts"));
}
