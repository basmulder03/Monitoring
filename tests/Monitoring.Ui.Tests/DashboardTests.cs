using System.Diagnostics;
using System.Runtime.CompilerServices;
using Shouldly;
using Xunit;

namespace Monitoring.Ui.Tests;

/// <summary>Runs the tests of the dashboard itself (Vitest, in a DOM) as part of <c>dotnet test</c>.</summary>
public class DashboardTests
{
    [Fact]
    public async Task The_dashboard_tests_pass()
    {
        // Arrange
        var directory = UiDirectory();
        Assert.SkipUnless(Directory.Exists(Path.Combine(directory, "node_modules", "vitest")), "The dashboard dependencies are not installed. Run 'npm ci' in src/Monitoring.Ui/ui; see docs/testing.md.");

        // Act
        var (exitCode, output) = await RunNpmAsync(directory, "test");

        // Assert: the output names every test that failed
        exitCode.ShouldBe(0, output);
    }

    private static string UiDirectory([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "src", "Monitoring.Ui", "ui"));

    private static async Task<(int ExitCode, string Output)> RunNpmAsync(string directory, string script)
    {
        var start = new ProcessStartInfo("npm", $"run {script}")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, await output + await errors);
    }
}
