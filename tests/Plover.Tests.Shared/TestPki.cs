using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Plover.Tests.Shared;

/// <summary>A throw-away certificate authority that issues certificates as PEM files in a temporary folder, for tests of TLS.</summary>
public sealed class TestPki : IDisposable
{
    private readonly X509Certificate2 _ca;
    private int _issued;

    /// <summary>Creates an authority and the folder that holds its files.</summary>
    /// <param name="name">Common name of the authority.</param>
    public TestPki(string name = "Test CA")
    {
        Directory = System.IO.Directory.CreateTempSubdirectory("pki-").FullName;
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        _ca = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(30));
        CaFile = Path.Combine(Directory, "ca.pem");
        File.WriteAllText(CaFile, _ca.ExportCertificatePem());
    }

    /// <summary>The folder with every file this authority wrote.</summary>
    public string Directory { get; }

    /// <summary>The PEM file with the certificate of the authority.</summary>
    public string CaFile { get; }

    /// <summary>Issues a certificate and writes it and its key as PEM files.</summary>
    /// <param name="dnsName">The name the certificate is valid for.</param>
    /// <param name="client">Whether it is a client certificate instead of a server certificate.</param>
    /// <param name="validFor">How long it stays valid; a negative value gives one that has expired.</param>
    /// <returns>The files and the thumbprint of the certificate.</returns>
    public Issued Issue(string dnsName, bool client = false, TimeSpan? validFor = null)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest($"CN={dnsName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid(client ? "1.3.6.1.5.5.7.3.2" : "1.3.6.1.5.5.7.3.1")], false));
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName(dnsName);
        request.CertificateExtensions.Add(names.Build());

        var life = validFor ?? TimeSpan.FromDays(7);
        var notBefore = life < TimeSpan.Zero ? DateTimeOffset.UtcNow.AddDays(-10) : DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = life < TimeSpan.Zero ? DateTimeOffset.UtcNow.AddDays(-5) : DateTimeOffset.UtcNow.Add(life);
        var serial = BitConverter.GetBytes(Interlocked.Increment(ref _issued));
        using var issued = request.Create(_ca, notBefore, notAfter, serial);
        using var withKey = issued.CopyWithPrivateKey(key);

        var id = Guid.NewGuid().ToString("N");
        var certificateFile = Path.Combine(Directory, $"{id}.crt");
        var keyFile = Path.Combine(Directory, $"{id}.key");
        File.WriteAllText(certificateFile, withKey.ExportCertificatePem());
        File.WriteAllText(keyFile, key.ExportPkcs8PrivateKeyPem());
        return new Issued(certificateFile, keyFile, withKey.Thumbprint);
    }

    /// <summary>Replaces the files of an issued certificate with those of a new one, as a renewal does.</summary>
    /// <param name="existing">The files to overwrite.</param>
    /// <param name="renewed">The certificate whose content to copy.</param>
    public static void Replace(Issued existing, Issued renewed)
    {
        File.Copy(renewed.CertificateFile, existing.CertificateFile, overwrite: true);
        File.Copy(renewed.KeyFile, existing.KeyFile, overwrite: true);

        // Renewal tools write both files at once; make sure the change shows even on a file system with coarse timestamps.
        File.SetLastWriteTimeUtc(existing.CertificateFile, DateTime.UtcNow.AddSeconds(5));
        File.SetLastWriteTimeUtc(existing.KeyFile, DateTime.UtcNow.AddSeconds(5));
    }

    /// <summary>Deletes the files.</summary>
    public void Dispose()
    {
        _ca.Dispose();
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder that stays behind is harmless.
        }
    }

    /// <summary>A certificate the authority issued, as files.</summary>
    /// <param name="CertificateFile">The PEM certificate.</param>
    /// <param name="KeyFile">The PEM private key.</param>
    /// <param name="Thumbprint">Identifies the certificate, so a test can tell which one a peer saw.</param>
    public sealed record Issued(string CertificateFile, string KeyFile, string Thumbprint);
}
