using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Plover.Common.Transport;
using Plover.Tests.Shared;
using Shouldly;
using Xunit;

namespace Plover.Common.Tests.Transport;

public class TlsTransportSecurityTests
{
    private const string Host = "controller.test";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_client_that_trusts_the_authority_reaches_a_server_with_its_certificate()
    {
        // Arrange
        using var pki = new TestPki();
        var server = pki.Issue(Host);
        await using var link = Link.Between(Server(server), Client(pki));

        // Act
        var (serverStream, clientStream) = await link.ConnectAsync(Host);
        await clientStream.WriteAsync("ping"u8.ToArray(), Ct);
        var received = await ReadAsync(serverStream, 4);

        // Assert
        received.ShouldBe("ping"u8.ToArray());
        ((SslStream)clientStream).RemoteCertificate!.GetCertHashString().ShouldBe(server.Thumbprint, StringCompareShould.IgnoreCase);
    }

    [Fact]
    public async Task Everything_sent_over_the_connection_is_encrypted()
    {
        // Arrange
        using var pki = new TestPki();
        await using var link = Link.Between(Server(pki.Issue(Host)), Client(pki));

        // Act
        var (_, clientStream) = await link.ConnectAsync(Host);

        // Assert
        clientStream.ShouldBeOfType<SslStream>().IsEncrypted.ShouldBeTrue();
    }

    [Fact]
    public async Task A_client_refuses_a_server_whose_certificate_comes_from_another_authority()
    {
        // Arrange
        using var pki = new TestPki();
        using var other = new TestPki("Other CA");
        await using var link = Link.Between(Server(other.Issue(Host)), Client(pki));

        // Act
        var connect = () => link.ConnectAsync(Host);

        // Assert
        await connect.ShouldThrowAsync<AuthenticationException>();
    }

    [Fact]
    public async Task A_client_refuses_a_server_whose_certificate_is_for_another_name()
    {
        // Arrange
        using var pki = new TestPki();
        await using var link = Link.Between(Server(pki.Issue("other.test")), Client(pki));

        // Act
        var connect = () => link.ConnectAsync(Host);

        // Assert
        await connect.ShouldThrowAsync<AuthenticationException>();
    }

    [Fact]
    public async Task A_client_checks_the_configured_server_name_instead_of_the_host_it_connected_to()
    {
        // Arrange
        using var pki = new TestPki();
        await using var link = Link.Between(Server(pki.Issue(Host)), Client(pki, o => o.ServerName = Host));

        // Act
        var (_, clientStream) = await link.ConnectAsync("127.0.0.1");

        // Assert
        clientStream.ShouldBeOfType<SslStream>().IsAuthenticated.ShouldBeTrue();
    }

    [Fact]
    public async Task A_client_refuses_a_server_certificate_that_has_expired()
    {
        // Arrange
        using var pki = new TestPki();
        await using var link = Link.Between(Server(pki.Issue(Host, validFor: TimeSpan.FromDays(-1))), Client(pki));

        // Act
        var connect = () => link.ConnectAsync(Host);

        // Assert
        await connect.ShouldThrowAsync<AuthenticationException>();
    }

    [Fact]
    public async Task Mutual_tls_lets_in_a_client_with_a_certificate_from_the_trusted_authority()
    {
        // Arrange
        using var pki = new TestPki();
        var client = pki.Issue("agent-1", client: true);
        await using var link = Link.Between(
            Server(pki.Issue(Host), o => { o.Mode = TransportMode.Mtls; o.ClientCaFile = pki.CaFile; }),
            Client(pki, o => { o.Mode = TransportMode.Mtls; o.CertificateFile = client.CertificateFile; o.KeyFile = client.KeyFile; }));

        // Act
        var (serverStream, _) = await link.ConnectAsync(Host);

        // Assert
        ((SslStream)serverStream).RemoteCertificate!.GetCertHashString().ShouldBe(client.Thumbprint, StringCompareShould.IgnoreCase);
    }

    [Fact]
    public async Task Mutual_tls_refuses_a_client_with_a_certificate_from_another_authority()
    {
        // Arrange
        using var pki = new TestPki();
        using var other = new TestPki("Other CA");
        var client = other.Issue("agent-1", client: true);
        await using var link = Link.Between(
            Server(pki.Issue(Host), o => { o.Mode = TransportMode.Mtls; o.ClientCaFile = pki.CaFile; }),
            Client(pki, o => { o.Mode = TransportMode.Mtls; o.CertificateFile = client.CertificateFile; o.KeyFile = client.KeyFile; }));

        // Act
        var connect = () => link.ConnectAsync(Host);

        // Assert
        await connect.ShouldThrowAsync<Exception>();
        (await link.ServerFailure).ShouldNotBeNull();
    }

    [Fact]
    public async Task Mutual_tls_refuses_a_client_without_a_certificate()
    {
        // Arrange
        using var pki = new TestPki();
        var server = Server(pki.Issue(Host), o => { o.Mode = TransportMode.Mtls; o.ClientCaFile = pki.CaFile; });
        var client = TransportSecurity.ForClient(new TransportSecurityOptions { Mode = TransportMode.Tls, CaFile = pki.CaFile });
        await using var link = Link.Between(server, client);

        // Act
        var connect = async () =>
        {
            var (_, clientStream) = await link.ConnectAsync(Host);

            // With TLS 1.3 the client finishes its handshake before the server has judged its certificate, so the refusal shows on first use.
            await clientStream.WriteAsync("x"u8.ToArray(), Ct);
            await clientStream.ReadExactlyAsync(new byte[1], Ct);
        };

        // Assert
        await connect.ShouldThrowAsync<Exception>();
        (await link.ServerFailure).ShouldNotBeNull();
    }

    [Fact]
    public async Task A_renewed_certificate_is_used_for_the_next_connection_without_a_restart()
    {
        // Arrange
        using var pki = new TestPki();
        var live = pki.Issue(Host);
        var renewed = pki.Issue(Host);
        await using var link = Link.Between(Server(live), Client(pki));
        var (_, before) = await link.ConnectAsync(Host);

        // Act
        TestPki.Replace(live, renewed);
        var (_, after) = await link.ConnectAsync(Host);

        // Assert
        ((SslStream)before).RemoteCertificate!.GetCertHashString().ShouldNotBe(((SslStream)after).RemoteCertificate!.GetCertHashString());
        ((SslStream)after).RemoteCertificate!.GetCertHashString().ShouldBe(renewed.Thumbprint, StringCompareShould.IgnoreCase);
    }

    [Fact]
    public async Task The_handshake_gives_up_when_the_other_side_stays_silent()
    {
        // Arrange
        using var pki = new TestPki();
        var client = Client(pki, o => o.HandshakeTimeout = TimeSpan.FromMilliseconds(300));
        using var silent = new TcpListener(IPAddress.Loopback, 0);
        silent.Start();
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)silent.LocalEndpoint).Port, Ct);

        // Act
        var secure = () => client.SecureClientAsync(tcp.GetStream(), Host, Ct);

        // Assert
        await secure.ShouldThrowAsync<TimeoutException>();
    }

    [Fact]
    public void Server_security_without_a_certificate_is_refused()
    {
        // Arrange
        var options = new TransportSecurityOptions { Mode = TransportMode.Tls };

        // Act
        var create = () => TransportSecurity.ForServer(options);

        // Assert
        create.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Mutual_tls_on_a_server_without_a_client_authority_is_refused()
    {
        // Arrange
        using var pki = new TestPki();
        var server = pki.Issue(Host);
        var options = new TransportSecurityOptions { Mode = TransportMode.Mtls, CertificateFile = server.CertificateFile, KeyFile = server.KeyFile };

        // Act
        var create = () => TransportSecurity.ForServer(options);

        // Assert
        create.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Mutual_tls_on_a_client_without_a_certificate_is_refused()
    {
        // Arrange
        var options = new TransportSecurityOptions { Mode = TransportMode.Mtls };

        // Act
        var create = () => TransportSecurity.ForClient(options);

        // Assert
        create.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void A_certificate_file_that_does_not_exist_is_refused_at_creation()
    {
        // Arrange
        var options = new TransportSecurityOptions { Mode = TransportMode.Tls, CertificateFile = "/nonexistent/server.crt" };

        // Act
        var create = () => TransportSecurity.ForServer(options);

        // Assert
        create.ShouldThrow<ArgumentException>().Message.ShouldContain("/nonexistent/server.crt");
    }

    [Fact]
    public void A_client_authority_file_without_a_certificate_is_refused()
    {
        // Arrange
        using var pki = new TestPki();
        var empty = Path.Combine(pki.Directory, "empty.pem");
        File.WriteAllText(empty, "");
        var options = new TransportSecurityOptions { Mode = TransportMode.Tls, CaFile = empty };

        // Act
        var create = () => TransportSecurity.ForClient(options);

        // Assert
        create.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public async Task Plain_transport_does_not_encrypt_and_leaves_the_stream_alone()
    {
        // Arrange
        var security = TransportSecurity.ForClient(new TransportSecurityOptions());
        using var stream = new MemoryStream();

        // Act
        var secured = await security.SecureClientAsync(stream, Host, Ct);

        // Assert
        security.IsEncrypted.ShouldBeFalse();
        secured.ShouldBeSameAs(stream);
    }

    [Fact]
    public void A_handshake_timeout_that_is_not_positive_is_refused()
    {
        // Arrange
        var options = new TransportSecurityOptions { HandshakeTimeout = TimeSpan.Zero };

        // Act
        var create = () => TransportSecurity.ForClient(options);

        // Assert
        create.ShouldThrow<ArgumentOutOfRangeException>();
    }

    private static ITransportSecurity Server(TestPki.Issued certificate, Action<TransportSecurityOptions>? change = null)
    {
        var options = new TransportSecurityOptions { Mode = TransportMode.Tls, CertificateFile = certificate.CertificateFile, KeyFile = certificate.KeyFile };
        change?.Invoke(options);
        return TransportSecurity.ForServer(options);
    }

    private static ITransportSecurity Client(TestPki pki, Action<TransportSecurityOptions>? change = null)
    {
        var options = new TransportSecurityOptions { Mode = TransportMode.Tls, CaFile = pki.CaFile };
        change?.Invoke(options);
        return TransportSecurity.ForClient(options);
    }

    private static async Task<byte[]> ReadAsync(Stream stream, int count)
    {
        var buffer = new byte[count];
        await stream.ReadExactlyAsync(buffer, Ct);
        return buffer;
    }

    /// <summary>A real loopback listener whose connections are secured by the server side, and the client side that connects to it.</summary>
    private sealed class Link : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly ITransportSecurity _server;
        private readonly ITransportSecurity _client;
        private TaskCompletionSource<Exception?> _failure = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<IDisposable> _open = [];

        private Link(ITransportSecurity server, ITransportSecurity client)
        {
            _server = server;
            _client = client;
            _listener.Start();
        }

        /// <summary>How the last handshake on the server side ended: the exception, or null when it succeeded.</summary>
        public Task<Exception?> ServerFailure => _failure.Task;

        public static Link Between(ITransportSecurity server, ITransportSecurity client) => new(server, client);

        public async Task<(Stream Server, Stream Client)> ConnectAsync(string host)
        {
            _failure = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var accepting = AcceptAsync();

            var tcp = new TcpClient();
            _open.Add(tcp);
            await tcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)_listener.LocalEndpoint).Port, Ct);
            Stream client;
            try
            {
                client = await _client.SecureClientAsync(tcp.GetStream(), host, Ct);
            }
            catch
            {
                // Let the server side finish too, so its outcome can be looked at.
                await Task.WhenAny(accepting, Task.Delay(2000, Ct));
                throw;
            }

            _open.Add(client);
            return (await accepting, client);
        }

        private async Task<Stream> AcceptAsync()
        {
            var accepted = await _listener.AcceptTcpClientAsync(Ct);
            _open.Add(accepted);
            try
            {
                var stream = await _server.SecureServerAsync(accepted.GetStream(), Ct);
                _open.Add(stream);
                _failure.TrySetResult(null);
                return stream;
            }
            catch (Exception ex)
            {
                _failure.TrySetResult(ex);
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            foreach (var item in _open)
            {
                item.Dispose();
            }

            _listener.Stop();
            return ValueTask.CompletedTask;
        }
    }
}
