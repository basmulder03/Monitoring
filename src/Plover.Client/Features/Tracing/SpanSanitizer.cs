using System.Text.RegularExpressions;

namespace Plover.Client.Features.Tracing;

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

    /// <summary>
    /// Replaces the values in a statement by question marks, so what was asked stays visible but not about whom. String literals in every common form (single or double
    /// quotes, doubled or backslash-escaped quotes, Postgres dollar quotes), hexadecimal and decimal numbers and comments are taken out; a quote that is never closed takes the
    /// rest of the statement with it. Double-quoted text is taken out as well, although standard SQL makes it an identifier, because MySQL makes it a value.
    /// </summary>
    /// <param name="sql">A statement as it was sent to the database.</param>
    /// <returns>The statement with the literals replaced and the comments removed.</returns>
    public static string Sql(string sql) => Literal().Replace(sql, match => match.Value.StartsWith("--", StringComparison.Ordinal) || match.Value.StartsWith("/*", StringComparison.Ordinal) ? " " : "?");

    [GeneratedRegex(
        @"'(?:[^'\\]|''|\\.)*'|""(?:[^""\\]|""""|\\.)*""|\$(\w*)\$.*?\$\1\$|--[^\r\n]*|/\*.*?\*/|(?<![\w$])0[xX][0-9a-fA-F]+\b|(?<![\w$])\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b|'.*|"".*",
        RegexOptions.Singleline)]
    private static partial Regex Literal();
}
