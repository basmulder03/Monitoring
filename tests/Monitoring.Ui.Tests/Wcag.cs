using System.Globalization;

namespace Monitoring.Ui.Tests;

/// <summary>Contrast ratios as defined by the Web Content Accessibility Guidelines.</summary>
internal static class Wcag
{
    /// <summary>Computes the contrast ratio between two colours.</summary>
    /// <param name="a">One colour as <c>#rrggbb</c>.</param>
    /// <param name="b">The other colour as <c>#rrggbb</c>.</param>
    /// <returns>A ratio from 1 (identical) to 21 (black on white).</returns>
    public static double Contrast(string a, string b)
    {
        var (lighter, darker) = (Luminance(a), Luminance(b));
        if (lighter < darker)
        {
            (lighter, darker) = (darker, lighter);
        }

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string hex)
    {
        double Channel(int offset)
        {
            var value = int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
    }
}
