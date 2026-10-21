namespace Monitoring.Common.Transport;

/// <summary><see cref="ITransportSecurity"/> that leaves the connection as it is.</summary>
public sealed class NoTransportSecurity : ITransportSecurity
{
    /// <summary>The only instance.</summary>
    public static NoTransportSecurity Instance { get; } = new();

    private NoTransportSecurity()
    {
    }

    /// <inheritdoc />
    public bool IsEncrypted => false;

    /// <inheritdoc />
    public Task<Stream> SecureServerAsync(Stream stream, CancellationToken ct) => Task.FromResult(stream);

    /// <inheritdoc />
    public Task<Stream> SecureClientAsync(Stream stream, string host, CancellationToken ct) => Task.FromResult(stream);
}
