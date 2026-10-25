namespace Plover.Controller;

/// <summary>Orders package versions: by their numbers first, and a pre-release such as <c>1.0.0-beta</c> before the release it leads up to.</summary>
internal sealed class VersionOrder : IComparer<string>
{
    /// <summary>The only instance.</summary>
    public static VersionOrder Instance { get; } = new();

    /// <inheritdoc />
    public int Compare(string? x, string? y)
    {
        var (numbersX, preX) = Split(x ?? "");
        var (numbersY, preY) = Split(y ?? "");

        for (var i = 0; i < Math.Max(numbersX.Length, numbersY.Length); i++)
        {
            var left = i < numbersX.Length ? numbersX[i] : 0;
            var right = i < numbersY.Length ? numbersY[i] : 0;
            if (left != right)
            {
                return left.CompareTo(right);
            }
        }

        if (preX.Length == 0 || preY.Length == 0)
        {
            // No pre-release label is the release, which comes after every pre-release of the same numbers.
            return preX.Length == preY.Length ? 0 : preX.Length == 0 ? 1 : -1;
        }

        return string.Compare(preX, preY, StringComparison.OrdinalIgnoreCase);
    }

    private static (long[] Numbers, string PreRelease) Split(string version)
    {
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        var withoutBuild = plus < 0 ? version : version[..plus];
        var dash = withoutBuild.IndexOf('-', StringComparison.Ordinal);
        var numbers = (dash < 0 ? withoutBuild : withoutBuild[..dash]).Split('.')
            .Select(part => long.TryParse(part, out var number) ? number : 0)
            .ToArray();
        return (numbers, dash < 0 ? "" : withoutBuild[(dash + 1)..]);
    }
}
