using System.Text.RegularExpressions;

namespace Monitoring.Client.Features.Tracing;

/// <summary>Removes what must not leave the application from the details of a span: query strings, credentials in addresses and literals in SQL.</summary>
internal static partial class SpanSanitizer
{
    /// <summary>Keeps the address up to its path.</summary>
    /// <param name="url">An address as the application used it.</param>
    /// <returns>The address without user information, query string and fragment; the text itself when it is not an absolute address.</returns>
    public static string Url(string url)
    {
        // On Unix a path such as /orders/42 also parses as an absolute (file) address, so only text with a scheme counts.
        if (!url.Contains("://", StringComparison.Ordinal) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var cut = url.IndexOfAny(['?', '#']);
            return cut < 0 ? url : url[..cut];
        }

        return uri.GetLeftPart(UriPartial.Path).Replace(uri.UserInfo + "@", "", StringComparison.Ordinal);
    }

    /// <summary>Replaces the values in a statement by question marks, so what was asked stays visible but not about whom.</summary>
    /// <param name="sql">A statement as it was sent to the database.</param>
    /// <returns>The statement with string and number literals replaced.</returns>
    public static string Sql(string sql) => Number().Replace(StringLiteral().Replace(sql, "?"), "?");

    [GeneratedRegex("'(?:[^']|'')*'")]
    private static partial Regex StringLiteral();

    [GeneratedRegex(@"\b\d+(?:\.\d+)?\b")]
    private static partial Regex Number();
}
