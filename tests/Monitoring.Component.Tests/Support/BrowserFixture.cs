using Microsoft.Playwright;
using Xunit;

namespace Monitoring.Component.Tests.Support;

/// <summary>
/// One headless Chromium shared by the browser tests. If no browser can be started the tests are skipped, with the reason,
/// because a browser is a one-time install per machine (see docs/testing.md).
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    /// <summary>Whether a browser could be started.</summary>
    public bool IsAvailable => _browser is not null;

    /// <summary>Why no browser is available, with what to do about it.</summary>
    public string Reason { get; private set; } = "";

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        try
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await LaunchAsync(_playwright);
        }
        catch (PlaywrightException ex) when (IsMissingBrowser(ex) && Environment.GetEnvironmentVariable("MONITORING_SKIP_BROWSER_INSTALL") != "true")
        {
            // The browser is a one-time download per machine, fetched with Playwright's own installer.
            // Set MONITORING_SKIP_BROWSER_INSTALL=true to skip the tests instead of downloading.
            _browser = await InstallAndLaunchAsync(ex);
        }
        catch (PlaywrightException ex)
        {
            Reason = Explain(ex);
        }
    }

    private async Task<IBrowser?> InstallAndLaunchAsync(PlaywrightException missing)
    {
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
        {
            Reason = $"The browser is not installed and Playwright could not download it (exit code {exitCode}), so the browser tests are skipped. "
                + "Behind a proxy that inspects TLS, make Node trust the system certificates (NODE_OPTIONS=--use-system-ca). See docs/testing.md. "
                + missing.Message.Split('\n')[0];
            return null;
        }

        try
        {
            return await LaunchAsync(_playwright!);
        }
        catch (PlaywrightException ex)
        {
            Reason = Explain(ex);
            return null;
        }
    }

    private static Task<IBrowser> LaunchAsync(IPlaywright playwright) =>
        // The dashboard is on the loopback address; a proxy from the environment must not get in the way.
        playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true, Args = ["--no-proxy-server"] });

    private static bool IsMissingBrowser(PlaywrightException ex) =>
        ex.Message.Contains("Executable doesn't exist", StringComparison.Ordinal)
        || ex.Message.Contains("playwright install", StringComparison.Ordinal);

    private static string Explain(PlaywrightException ex)
    {
        var firstLine = ex.Message.Split('\n')[0];
        var libraries = ex.Message.Contains("shared libraries", StringComparison.Ordinal)
            ? " The browser needs system libraries; install them with 'playwright install-deps chromium' (needs root)."
            : "";
        return $"No browser could be started, so the browser tests are skipped.{libraries} See docs/testing.md. {firstLine}";
    }

    /// <summary>Opens a new page in a fresh browser context.</summary>
    /// <param name="width">The width of the window in pixels; a desktop by default.</param>
    /// <returns>The page, which records script errors and console errors, and closes its context when disposed.</returns>
    public async Task<BrowserPage> NewPageAsync(int width = 1280)
    {
        var context = await _browser!.NewContextAsync(new BrowserNewContextOptions { ViewportSize = new ViewportSize { Width = width, Height = 800 } });
        return new BrowserPage(context, await context.NewPageAsync());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
        }

        _playwright?.Dispose();
    }
}

/// <summary>Shares one <see cref="BrowserFixture"/> between all browser test classes.</summary>
[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<BrowserFixture>
{
    /// <summary>Name of the collection.</summary>
    public const string Name = "browser";
}
