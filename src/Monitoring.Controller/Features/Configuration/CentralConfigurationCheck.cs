using Monitoring.Hosting;

namespace Monitoring.Controller.Features.Configuration;

/// <summary>Says whether the central configuration file can be used. While the controller runs a file that cannot is only logged and the last good one stays in force, so this is the way to find out before.</summary>
/// <param name="options">Where the file is.</param>
internal sealed class CentralConfigurationCheck(ConfigurationOptions options) : IConfigurationCheck
{
    /// <inheritdoc />
    public IEnumerable<string> Problems()
    {
        if (options.File is null)
        {
            yield break;
        }

        string? problem = null;
        try
        {
            ParsedConfiguration.Parse(File.ReadAllText(options.File));
        }
        catch (ConfigurationRejectedException ex)
        {
            problem = $"The central configuration '{options.File}' is not valid: {ex.Message}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"The central configuration '{options.File}' cannot be read: {ex.Message}";
        }

        if (problem is not null)
        {
            yield return problem;
        }
    }
}
