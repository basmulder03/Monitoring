using System.Security.Cryptography.X509Certificates;

namespace Plover.Client.Features.Certificates;

/// <summary>A TLS certificate the application serves or uses, whose time to expiry is reported so it can be renewed before it lapses.</summary>
/// <param name="Name">The name that tells the certificate apart in the metrics, for example <c>https</c>.</param>
/// <param name="Load">Gets the certificate each time the metrics are read, so a renewed one is noticed; the certificate is disposed after use. May return <see langword="null"/> while there is none.</param>
public sealed record CertificateWatch(string Name, Func<X509Certificate2?> Load)
{
    /// <summary>Watches a certificate in a file, such as the one a Kubernetes secret mounts. The file is read again each time, so a renewal needs no restart.</summary>
    /// <param name="name">The name in the metrics.</param>
    /// <param name="path">A file with one certificate, in PEM or DER form; a private key in it is not read.</param>
    /// <returns>The watch.</returns>
    public static CertificateWatch File(string name, string path) =>
        new(name, () => System.IO.File.Exists(path) ? X509CertificateLoader.LoadCertificateFromFile(path) : null);
}
