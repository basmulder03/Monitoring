namespace Monitoring.Controller.Features.Security;

/// <summary>Settings for letting agents prove who they are with the token of their Kubernetes service account, which the controller checks with the API server.</summary>
public sealed class ServiceAccountOptions
{
    /// <summary>Gets or sets whether service account tokens are accepted besides the configured agent tokens.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the audience the agents' tokens are made for, and which the controller asks the API server to check, so a token made for something else is refused.</summary>
    public string Audience { get; set; } = "monitoring-controller";

    /// <summary>Gets or sets the address of the API server; the one of the cluster the controller runs in by default.</summary>
    public string? ApiServer { get; set; }

    /// <summary>Gets or sets the file with the token the controller asks the API server with; the service account of the pod by default.</summary>
    public string? TokenFile { get; set; }

    /// <summary>Gets or sets the PEM file with the authority that signed the certificate of the API server; the one of the pod by default.</summary>
    public string? CaFile { get; set; }

    /// <summary>
    /// Gets or sets which scope an agent of a namespace may report as, for namespaces whose scope is not named after them.
    /// A namespace that is not listed reports as the scope with its own name, and no other.
    /// </summary>
    public Dictionary<string, string> Scopes { get; set; } = new(StringComparer.Ordinal);
}
