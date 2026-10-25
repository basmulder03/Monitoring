using System.Reflection;
using Plover.Protocol.Reports;

namespace Plover.Client.Features.Packages;

/// <summary>Reads the list of packages that the build of the application embedded in it.</summary>
internal static class PackageList
{
    /// <summary>The name the build gives the embedded resource, written by <c>build/Plover.Client.targets</c>.</summary>
    public const string ResourceName = "Plover.Packages";

    /// <summary>The end of the name under which some SDKs, such as the web SDK, embed the file instead, prefixed with the name of the project.</summary>
    private const string FileSuffix = "plover.packages.txt";

    /// <summary>Reads the list from an assembly.</summary>
    /// <param name="application">The application, normally the entry assembly; <see langword="null"/> when there is none.</param>
    /// <returns>The packages; an empty list when the application was built without it, so it can be told from one that has no packages.</returns>
    public static PackagesReport Read(Assembly? application)
    {
        var name = application?.GetManifestResourceNames()
            .FirstOrDefault(candidate => candidate == ResourceName || candidate.EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase));
        using var stream = name is null ? null : application!.GetManifestResourceStream(name);
        if (stream is null)
        {
            return new PackagesReport(Runtime(), []);
        }

        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Reads the text of the list: <c>D name</c> for a package the project asks for itself, <c>P name version</c> for every package built in.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The packages ordered by name; lines that are not understood are skipped.</returns>
    public static PackagesReport Parse(string text)
    {
        var direct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packages = new SortedDictionary<string, PackageReport>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts is ["D", var name])
            {
                direct.Add(name);
            }
            else if (parts is ["P", var package, var version])
            {
                // The same package can be listed for several target frameworks; the first one is kept.
                packages.TryAdd(package, new PackageReport(package, version, Direct: false));
            }
        }

        return new PackagesReport(
            Runtime(),
            [.. packages.Values.Select(package => package with { Direct = direct.Contains(package.Name) })]);
    }

    private static string Runtime() => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
}
