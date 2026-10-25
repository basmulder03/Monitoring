namespace Plover.Sidecar.Features.LibraryLink;

/// <summary>Settings of the link to the application's Plover client.</summary>
public sealed class LibraryLinkOptions
{
    /// <summary>The socket the application listens on, on a volume shared with the sidecar.</summary>
    public string SocketPath { get; set; } = "/var/run/plover/plover.sock";

    /// <summary>How long a question to the application may take before it counts as not answering.</summary>
    public TimeSpan CallTimeout { get; set; } = TimeSpan.FromSeconds(5);
}
