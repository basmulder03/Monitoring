namespace Plover.Hosting;

/// <summary>Raised when a setting is missing or has a value that cannot be used. The message names the setting.</summary>
/// <param name="message">What is wrong, including the full name of the setting.</param>
public sealed class ConfigurationException(string message) : Exception(message);
