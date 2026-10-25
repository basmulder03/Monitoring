namespace Plover.Controller.Features.Configuration;

/// <summary>The central configuration cannot be used; the message says why in terms of the file.</summary>
/// <param name="message">What is wrong.</param>
/// <param name="inner">The parse error behind it, if any.</param>
public sealed class ConfigurationRejectedException(string message, Exception? inner = null) : Exception(message, inner);
