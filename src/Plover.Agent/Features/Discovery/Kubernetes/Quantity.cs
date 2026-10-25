using System.Globalization;

namespace Plover.Agent.Features.Discovery.Kubernetes;

/// <summary>Reads Kubernetes quantities such as <c>250m</c>, <c>1.5</c>, <c>128Mi</c> or <c>2G</c> as a plain number: cores for cpu, bytes for memory.</summary>
internal static class Quantity
{
    private static readonly (string Suffix, double Factor)[] Suffixes =
    [
        ("Ki", 1024d), ("Mi", 1024d * 1024), ("Gi", 1024d * 1024 * 1024), ("Ti", 1024d * 1024 * 1024 * 1024), ("Pi", 1024d * 1024 * 1024 * 1024 * 1024),
        ("n", 1e-9), ("u", 1e-6), ("m", 1e-3), ("k", 1e3), ("M", 1e6), ("G", 1e9), ("T", 1e12), ("P", 1e15),
    ];

    /// <summary>Reads a quantity.</summary>
    /// <param name="text">The quantity as Kubernetes writes it.</param>
    /// <returns>The number, or <see langword="null"/> when it is not a quantity this understands.</returns>
    public static double? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var factor = 1d;
        var number = text;
        foreach (var (suffix, value) in Suffixes)
        {
            if (text.EndsWith(suffix, StringComparison.Ordinal))
            {
                factor = value;
                number = text[..^suffix.Length];
                break;
            }
        }

        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed * factor : null;
    }
}
