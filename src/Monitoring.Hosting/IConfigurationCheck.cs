namespace Monitoring.Hosting;

/// <summary>
/// Something that can say whether a part of the configuration is usable without starting the executable. What the settings themselves get wrong stops the host from being
/// built, which already says so; this is for what is only read later, such as a file that is looked at while the executable runs.
/// </summary>
public interface IConfigurationCheck
{
    /// <summary>Looks at what the check is about.</summary>
    /// <returns>What is wrong, one sentence each; empty when all is well.</returns>
    IEnumerable<string> Problems();
}
