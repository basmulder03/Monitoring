using System.Runtime.CompilerServices;
using Plover.Controller.Features.Alerting;
using Plover.Controller.Features.Dashboard;
using Plover.Controller.Features.Packages;
using Plover.Controller.Features.SelfMonitoring;
using Plover.Controller.Features.WebAuth;
using Plover.Controller.Features.Topology;
using Plover.Controller.Features.Vulnerabilities;
using Plover.Protocol.Reports;
using Shouldly;
using Xunit;

namespace Plover.Ui.Tests.Contracts;

/// <summary>Keeps the TypeScript types of the dashboard in step with the C# records the controller sends.</summary>
public class ContractsTests
{
    [Fact]
    public async Task The_typescript_contracts_match_the_types_the_controller_sends()
    {
        // Arrange
        var path = ContractsPath();
        var expected = new ContractWriter().Add(typeof(Overview)).Add(typeof(ScopeDetail)).Add(typeof(TopologyGraph)).Add(typeof(PackageCatalog)).Add(typeof(VulnerabilityReport)).Add(typeof(SystemStatus)).Add(typeof(MetricHistory)).Add(typeof(WhoAmI)).Add(typeof(RuntimeSilence)).Add(typeof(AlertOccurrence)).Add(typeof(RulesReport)).Write();

        if (Environment.GetEnvironmentVariable("UPDATE_CONTRACTS") == "1")
        {
            await File.WriteAllTextAsync(path, expected, TestContext.Current.CancellationToken);
        }

        // Act
        var actual = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // Assert: a mismatch means a C# type changed; regenerate the file and fix the page where it no longer type-checks
        actual.ReplaceLineEndings().ShouldBe(expected.ReplaceLineEndings(), "contracts.ts is out of date. Run: UPDATE_CONTRACTS=1 dotnet test --project tests/Plover.Ui.Tests");
    }

    private static string ContractsPath([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "..", "src", "Plover.Ui", "ui", "src", "api", "contracts.ts"));
}
