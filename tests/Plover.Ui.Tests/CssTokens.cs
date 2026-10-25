using System.Text.RegularExpressions;

namespace Plover.Ui.Tests;

/// <summary>Reads the design tokens (CSS custom properties) from a block of the style sheet.</summary>
internal static class CssTokens
{
    /// <summary>Reads the custom properties declared in the first block that starts with the selector.</summary>
    /// <param name="css">The style sheet.</param>
    /// <param name="selector">The selector that opens the block, such as <c>:root[data-theme="dark"]</c>.</param>
    /// <returns>The tokens by name without the leading dashes, such as <c>bg</c>.</returns>
    /// <exception cref="InvalidOperationException">The style sheet has no such block.</exception>
    public static IReadOnlyDictionary<string, string> Read(string css, string selector)
    {
        var start = css.IndexOf(selector + " {", StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"The style sheet has no block '{selector}'.");
        }

        var end = css.IndexOf('}', start);
        var block = css[start..end];
        return Regex.Matches(block, @"--(?<name>[a-z]+):\s*(?<value>#[0-9a-fA-F]{6});")
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);
    }
}
