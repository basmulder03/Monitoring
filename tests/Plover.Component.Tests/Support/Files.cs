namespace Plover.Component.Tests.Support;

/// <summary>Helpers for files in tests.</summary>
internal static class Files
{
    /// <summary>A path where no file exists, to keep a host from reading a configuration file on the machine.</summary>
    /// <returns>The path.</returns>
    public static string Missing() => Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json");
}
