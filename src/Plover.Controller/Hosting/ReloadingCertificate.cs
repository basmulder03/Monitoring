using System.Security.Cryptography.X509Certificates;

namespace Plover.Controller.Hosting;

/// <summary>A server certificate from PEM files that is read again when either file has changed.</summary>
/// <param name="certificateFile">PEM file with the certificate and its chain.</param>
/// <param name="keyFile">PEM file with the private key.</param>
internal sealed class ReloadingCertificate(string certificateFile, string keyFile)
{
    private readonly object _gate = new();
    private X509Certificate2? _certificate;
    private DateTime _stamp;

    /// <summary>Gives the certificate, reading the files again when they have changed since last time.</summary>
    /// <returns>The certificate.</returns>
    /// <exception cref="IOException">The files cannot be read and there is no earlier certificate.</exception>
    public X509Certificate2 Get()
    {
        var stamp = File.GetLastWriteTimeUtc(certificateFile) > File.GetLastWriteTimeUtc(keyFile) ? File.GetLastWriteTimeUtc(certificateFile) : File.GetLastWriteTimeUtc(keyFile);
        lock (_gate)
        {
            if (_certificate is null || stamp != _stamp)
            {
                try
                {
                    // The ephemeral key is exported and loaded again, because Windows cannot use an ephemeral key for TLS as it is.
                    using var pem = X509Certificate2.CreateFromPemFile(certificateFile, keyFile);
                    _certificate = X509CertificateLoader.LoadPkcs12(pem.Export(X509ContentType.Pfx), null);
                    _stamp = stamp;
                }
                catch (Exception ex) when (_certificate is not null && ex is IOException or System.Security.Cryptography.CryptographicException)
                {
                    // The files are being replaced; keep serving with the last good certificate.
                }
            }

            return _certificate!;
        }
    }
}
