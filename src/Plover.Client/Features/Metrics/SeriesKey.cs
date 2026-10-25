using System.Diagnostics.Metrics;

namespace Plover.Client.Features.Metrics;

/// <summary>Identifies one series: the name of the instrument and the exact tags.</summary>
internal sealed record SeriesKey
{
    private SeriesKey(string name, IReadOnlyDictionary<string, string> tags, string tagText)
    {
        Name = name;
        Tags = tags;
        TagText = tagText;
    }

    /// <summary>Name of the instrument, with a suffix for the parts of a histogram.</summary>
    public string Name { get; }

    /// <summary>The tags as text values.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; }

    /// <summary>The tags in a fixed order as one string, which makes two keys with the same tags equal whatever the order they came in.</summary>
    public string TagText { get; }

    /// <summary>Creates the key of a measurement.</summary>
    /// <param name="name">Name of the instrument.</param>
    /// <param name="tags">Tags of the measurement.</param>
    /// <returns>The key.</returns>
    public static SeriesKey Of(string name, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            map[tag.Key] = tag.Value?.ToString() ?? "";
        }

        return new SeriesKey(name, map, string.Join('\u001f', map.Select(pair => $"{pair.Key}={pair.Value}")));
    }

    /// <summary>Creates the key of a part of the same series.</summary>
    /// <param name="suffix">Appended to the name.</param>
    /// <returns>The key.</returns>
    public SeriesKey Suffixed(string suffix) => new(Name + suffix, Tags, TagText);

    /// <inheritdoc />
    public bool Equals(SeriesKey? other) => other is not null && Name == other.Name && TagText == other.TagText;

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Name, TagText);
}
