using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Monitoring.Injector.Hosting;

/// <summary>
/// The certificate the webhook presents, read again from its files when they change, so a renewal by cert-manager or a new secret needs no restart. Kubernetes updates
/// the files of a mounted secret in place, a little while after the secret changed. A certificate that cannot be read is not used: the one that works stays.
/// </summary>
/// <param name="certificateFile">The PEM file with the certificate.</param>
/// <param name="keyFile">The PEM file with its key.</param>
/// <param name="checkEvery">How often the files are looked at, at most; the look happens when a connection is made, so an idle webhook costs nothing.</param>
/// <param name="time">Clock for the interval.</param>
/// <param name="logger">Receives what was loaded and what could not be.</param>
internal sealed class ReloadingCertificate(string certificateFile, string keyFile, TimeSpan checkEvery, TimeProvider time, ILogger logger)
{
    private static readonly TimeSpan WarnBeforeExpiry = TimeSpan.FromDays(14);

    private readonly Lock _gate = new();
    private X509Certificate2? _current;
    private (byte[] Certificate, byte[] Key) _seen = ([], []);
    private long _checkedAt;

    /// <summary>Loads the certificate for the first time, which has to work: a webhook without one cannot start.</summary>
    /// <returns>This holder.</returns>
    /// <exception cref="Exception">The files cannot be read or are not a certificate and its key.</exception>
    public ReloadingCertificate Load()
    {
        lock (_gate)
        {
            _seen = Stamp();
            _current = Read();
            _checkedAt = time.GetTimestamp();
            Report(_current);
            return this;
        }
    }

    /// <summary>Gets the certificate to present now, after looking at the files if it is time to.</summary>
    /// <returns>The certificate.</returns>
    public X509Certificate2 Current()
    {
        lock (_gate)
        {
            if (time.GetElapsedTime(_checkedAt) >= checkEvery)
            {
                _checkedAt = time.GetTimestamp();
                Refresh();
            }

            return _current!;
        }
    }

    private void Refresh()
    {
        try
        {
            var stamp = Stamp();
            if (stamp.Certificate.AsSpan().SequenceEqual(_seen.Certificate) && stamp.Key.AsSpan().SequenceEqual(_seen.Key))
            {
                return;
            }

            var replacement = Read();
            _current = replacement;
            _seen = stamp;
            logger.LogInformation("The certificate was renewed");
            Report(replacement);
        }
        catch (Exception ex) when (ex is IOException or System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            // Often the key is replaced a moment before the certificate; the next look finds both. Until then the old one is presented, and the files are not marked as seen.
            logger.LogWarning("The renewed certificate cannot be used yet, keeping the one in use: {Reason}", ex.Message);
        }
    }

    // The contents, not the modification times: the files of a mounted secret are links, whose own time never changes when the secret does.
    private (byte[] Certificate, byte[] Key) Stamp() => (File.ReadAllBytes(certificateFile), File.ReadAllBytes(keyFile));

    private X509Certificate2 Read()
    {
        // Loaded through a copy that is held in memory, so the files can be replaced while it is in use.
        using var loaded = X509Certificate2.CreateFromPemFile(certificateFile, keyFile);
        return X509CertificateLoader.LoadPkcs12(loaded.Export(X509ContentType.Pkcs12), null);
    }

    private void Report(X509Certificate2 certificate)
    {
        var left = certificate.NotAfter.ToUniversalTime() - time.GetUtcNow().UtcDateTime;
        logger.Log(left < WarnBeforeExpiry ? LogLevel.Warning : LogLevel.Information, "The certificate of the webhook is valid until {Until:u}", certificate.NotAfter.ToUniversalTime());
    }
}
