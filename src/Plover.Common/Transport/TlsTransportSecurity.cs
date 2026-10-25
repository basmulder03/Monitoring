using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Plover.Common.Transport;

/// <summary><see cref="ITransportSecurity"/> that wraps the connection in TLS, with a client certificate as well in <see cref="TransportMode.Mtls"/>.</summary>
/// <remarks>Built on <see cref="SslStream"/>, which works with Native AOT. Create it with <see cref="TransportSecurity"/>.</remarks>
internal sealed class TlsTransportSecurity : ITransportSecurity
{
    private readonly TransportSecurityOptions _options;
    private readonly CertificateFile? _certificate;
    private readonly X509Certificate2Collection? _ca;
    private readonly X509Certificate2Collection? _clientCa;

    /// <summary>Creates the security for one side of the connection.</summary>
    /// <param name="options">The settings; the files they name are read now, so a mistake shows at startup.</param>
    /// <param name="server">Whether this is the controller side.</param>
    /// <exception cref="ArgumentException">A setting that the mode needs is missing, or a file cannot be read or holds no certificate.</exception>
    public TlsTransportSecurity(TransportSecurityOptions options, bool server)
    {
        _options = options;
        var mtls = options.Mode == TransportMode.Mtls;

        if (server || mtls)
        {
            if (string.IsNullOrWhiteSpace(options.CertificateFile))
            {
                throw new ArgumentException(server
                    ? "A server certificate file is required for TLS."
                    : "A client certificate file is required for mutual TLS.", nameof(options));
            }

            _certificate = new CertificateFile(options.CertificateFile, string.IsNullOrWhiteSpace(options.KeyFile) ? null : options.KeyFile);
            Reading(options.CertificateFile, () => _certificate.Get());
        }

        if (server && mtls)
        {
            _clientCa = LoadAuthority(options.ClientCaFile, "A client certificate authority file is required for mutual TLS.");
        }

        if (!server && !string.IsNullOrWhiteSpace(options.CaFile))
        {
            _ca = LoadAuthority(options.CaFile, "");
        }
    }

    /// <inheritdoc />
    public bool IsEncrypted => true;

    /// <inheritdoc />
    public async Task<Stream> SecureServerAsync(Stream stream, CancellationToken ct)
    {
        var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
        var mtls = _options.Mode == TransportMode.Mtls;
        var authentication = new SslServerAuthenticationOptions
        {
            ServerCertificate = _certificate!.Get(),
            ClientCertificateRequired = mtls,
            EnabledSslProtocols = SslProtocols.None,
            CertificateChainPolicy = mtls ? PolicyTrusting(_clientCa!) : null,
        };

        await HandshakeAsync(ssl, token => ssl.AuthenticateAsServerAsync(authentication, token), ct).ConfigureAwait(false);
        return ssl;
    }

    /// <inheritdoc />
    public async Task<Stream> SecureClientAsync(Stream stream, string host, CancellationToken ct)
    {
        var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
        var authentication = new SslClientAuthenticationOptions
        {
            TargetHost = string.IsNullOrWhiteSpace(_options.ServerName) ? host : _options.ServerName,
            EnabledSslProtocols = SslProtocols.None,
            CertificateChainPolicy = _ca is null ? null : PolicyTrusting(_ca),
            ClientCertificates = _options.Mode == TransportMode.Mtls ? [_certificate!.Get()] : null,
        };

        await HandshakeAsync(ssl, token => ssl.AuthenticateAsClientAsync(authentication, token), ct).ConfigureAwait(false);
        return ssl;
    }

    /// <summary>Runs a handshake within the time allowed; on any failure the connection is closed.</summary>
    private async Task HandshakeAsync(SslStream ssl, Func<CancellationToken, Task> handshake, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.HandshakeTimeout);
        try
        {
            await handshake(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            throw new TimeoutException($"The TLS handshake took longer than {_options.HandshakeTimeout}.");
        }
        catch
        {
            await ssl.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>A policy that trusts only the given certificate authorities, instead of those of the machine.</summary>
    /// <remarks>shortcut: revocation is not checked, because a private authority rarely publishes a list and the check would fail every handshake offline; rely on short-lived certificates, and add revocation when a list is available.</remarks>
    private static X509ChainPolicy PolicyTrusting(X509Certificate2Collection authorities)
    {
        var policy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
        policy.CustomTrustStore.AddRange(authorities);
        return policy;
    }

    private static X509Certificate2Collection LoadAuthority(string? file, string missingMessage)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            throw new ArgumentException(missingMessage, nameof(file));
        }

        var collection = new X509Certificate2Collection();
        Reading(file, () => collection.ImportFromPemFile(file));
        if (collection.Count == 0)
        {
            throw new ArgumentException($"'{file}' holds no certificate.", nameof(file));
        }

        return collection;
    }

    /// <summary>Runs a read of a file and reports a failure as a setting that is wrong, which names the file.</summary>
    private static void Reading(string file, Action read)
    {
        try
        {
            read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new ArgumentException($"Cannot read the certificate file '{file}': {ex.Message}", nameof(file), ex);
        }
    }
}
