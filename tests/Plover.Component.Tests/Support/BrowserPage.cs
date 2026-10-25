using Microsoft.Playwright;

namespace Plover.Component.Tests.Support;

/// <summary>A browser page that remembers the script errors and console errors it saw, such as a violated content security policy.</summary>
public sealed class BrowserPage : IAsyncDisposable
{
    private readonly IBrowserContext _context;
    private readonly List<string> _problems = [];

    internal BrowserPage(IBrowserContext context, IPage page)
    {
        _context = context;
        Page = page;
        page.PageError += (_, message) => Record($"script error: {message}");
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                Record($"console error: {message.Text}");
            }
        };
    }

    /// <summary>The page.</summary>
    public IPage Page { get; }

    /// <summary>The errors seen so far.</summary>
    public IReadOnlyList<string> Problems
    {
        get
        {
            lock (_problems)
            {
                return [.. _problems];
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _context.CloseAsync();

    private void Record(string problem)
    {
        lock (_problems)
        {
            _problems.Add(problem);
        }
    }
}
