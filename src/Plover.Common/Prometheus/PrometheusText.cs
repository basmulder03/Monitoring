using System.Globalization;
using System.Text;

namespace Plover.Common.Prometheus;

/// <summary>Writes samples in the Prometheus text format, version 0.0.4, by hand so no library is needed.</summary>
public static class PrometheusText
{
    /// <summary>The media type of the text format.</summary>
    public const string ContentType = "text/plain; version=0.0.4; charset=utf-8";

    /// <summary>Writes the samples, grouped by metric name with one type line each. Every value is exposed as a gauge, because the source does not say which are counters.</summary>
    /// <param name="samples">The values.</param>
    /// <returns>The text.</returns>
    public static string Write(IEnumerable<PrometheusSample> samples)
    {
        var text = new StringBuilder();
        foreach (var group in samples.GroupBy(sample => Name(sample.Name), StringComparer.Ordinal))
        {
            text.Append("# TYPE ").Append(group.Key).Append(" gauge\n");
            foreach (var sample in group)
            {
                text.Append(group.Key);
                if (sample.Labels.Count > 0)
                {
                    text.Append('{');
                    text.AppendJoin(',', sample.Labels.OrderBy(label => label.Key, StringComparer.Ordinal).Select(label => $"{Name(label.Key)}=\"{Escape(label.Value)}\""));
                    text.Append('}');
                }

                text.Append(' ').Append(Number(sample.Value)).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>Replaces everything a name may not contain with an underscore, and makes it start with a letter or underscore.</summary>
    private static string Name(string name)
    {
        var clean = string.Create(name.Length, name, static (span, source) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                var c = source[i];
                span[i] = char.IsAsciiLetterOrDigit(c) || c is '_' or ':' ? c : '_';
            }
        });
        return clean.Length == 0 || char.IsAsciiDigit(clean[0]) ? "_" + clean : clean;
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static string Number(double value) => double.IsNaN(value) ? "NaN"
        : double.IsPositiveInfinity(value) ? "+Inf"
        : double.IsNegativeInfinity(value) ? "-Inf"
        : value.ToString("R", CultureInfo.InvariantCulture);
}
