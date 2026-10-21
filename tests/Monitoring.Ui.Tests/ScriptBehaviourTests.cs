using System.Diagnostics;
using Monitoring.Ui;
using Shouldly;
using Xunit;

namespace Monitoring.Ui.Tests;

/// <summary>Runs the page script in a stand-in DOM with Node and checks what it renders for different overviews.</summary>
public class ScriptBehaviourTests
{
    [Fact]
    public async Task The_page_script_renders_groups_filters_alerts_and_announcements_correctly()
    {
        // Arrange
        Assert.SkipUnless(NodeIsInstalled(), "Node is needed to run the page script; install it to run this test.");
        var script = Path.Combine(Path.GetTempPath(), $"monitoring-app-{Guid.NewGuid():N}.js");
        await File.WriteAllTextAsync(script, UiAssets.ReadText("app.js")!, TestContext.Current.CancellationToken);

        try
        {
            // Act
            var (exitCode, output) = await RunNodeAsync(Path.Combine(AppContext.BaseDirectory, "Script", "check.js"), script);

            // Assert: every scenario is listed, so a failure names the scenario that broke
            exitCode.ShouldBe(0, output);
            output.ShouldContain("all passed");
        }
        finally
        {
            File.Delete(script);
        }
    }

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

    private static async Task<(int ExitCode, string Output)> RunNodeAsync(string checkScript, string pageScript)
    {
        var start = new ProcessStartInfo("node", $"\"{checkScript}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["MONITORING_UI_SCRIPT"] = pageScript;

        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, output + error);
    }
}
