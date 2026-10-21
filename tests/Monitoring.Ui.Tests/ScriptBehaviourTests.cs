using System.Diagnostics;
using System.Runtime.CompilerServices;
using Monitoring.Ui;
using Shouldly;
using Xunit;

namespace Monitoring.Ui.Tests;

/// <summary>Runs the real page and its script in jsdom (installed with npm) and checks what it renders for different overviews.</summary>
public class ScriptBehaviourTests
{
    [Fact]
    public async Task The_page_script_renders_groups_filters_alerts_announcements_and_themes_correctly()
    {
        // Arrange
        var directory = SourceDirectory();
        Assert.SkipUnless(NodeIsInstalled(), "Node is needed to run the page script tests. See docs/testing.md.");
        Assert.SkipUnless(
            Directory.Exists(Path.Combine(directory, "node_modules", "jsdom")),
            "The dependencies of the page script tests are not installed. Run 'npm ci' in tests/Monitoring.Ui.Tests; see docs/testing.md.");

        // The files under test are the ones embedded in the controller, written out for the script to read.
        var page = Directory.CreateTempSubdirectory("monitoring-ui-");
        await File.WriteAllTextAsync(Path.Combine(page.FullName, "index.html"), UiAssets.ReadText("index.html")!, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(page.FullName, "app.js"), UiAssets.ReadText("app.js")!, TestContext.Current.CancellationToken);

        try
        {
            // Act
            var (exitCode, output) = await RunNodeAsync(directory, page.FullName);

            // Assert: every scenario is listed, so a failure names the scenario that broke
            exitCode.ShouldBe(0, output);
            output.ShouldContain("all passed");
        }
        finally
        {
            page.Delete(recursive: true);
        }
    }

    private static string SourceDirectory([CallerFilePath] string file = "") => Path.GetDirectoryName(file)!;

    private static bool NodeIsInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, UseShellExecute = false });
            process!.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task<(int ExitCode, string Output)> RunNodeAsync(string directory, string pageDirectory)
    {
        var start = new ProcessStartInfo("node", $"\"{Path.Combine(directory, "Script", "check.js")}\"")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["MONITORING_UI_DIR"] = pageDirectory;

        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, output + error);
    }
}
