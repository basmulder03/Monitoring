using System.Security.Cryptography.X509Certificates;

namespace Monitoring.Common.Transport;

/// <summary>A certificate and key on disk that are read again when the files change, so a renewed certificate is used without a restart.</summary>
/// <param name="certificateFile">The PEM certificate with its chain.</param>
/// <param name="keyFile">The PEM private key; <see langword="null"/> when the certificate file holds it.</param>
internal sealed class CertificateFile(string certificateFile, string? keyFile)
{
    private readonly Lock _gate = new();
    private X509Certificate2? _certificate;
    private (DateTime Certificate, DateTime Key) _stamp;

    /// <summary>Gets the certificate, reading the files again first if they changed since the last call.</summary>
    /// <returns>The certificate with its private key, ready for TLS.</returns>
    /// <exception cref="IOException">A file cannot be read.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The files do not hold a certificate and its key.</exception>
    public X509Certificate2 Get()
    {
        lock (_gate)
        {
            var stamp = (File.GetLastWriteTimeUtc(certificateFile), keyFile is null ? default : File.GetLastWriteTimeUtc(keyFile));
            if (_certificate is null || stamp != _stamp)
            {
                var loaded = X509Certificate2.CreateFromPemFile(certificateFile, keyFile);

                // A key loaded from PEM is ephemeral, which Windows cannot use for TLS; exporting and loading it again makes it persistent.
                _certificate = X509CertificateLoader.LoadPkcs12(loaded.Export(X509ContentType.Pfx), null);
                loaded.Dispose();
                _stamp = stamp;
            }

            return _certificate;
        }
    }
}
