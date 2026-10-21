using Monitoring.Protocol.Messages;

namespace Monitoring.Client;

/// <summary>One kind of data the application can give the sidecar. Each data slice offers its own, so the local server never needs to know the slices.</summary>
internal interface IPullSource
{
    /// <summary>The kind of data this source answers pulls for.</summary>
    PullKind Kind { get; }

    /// <summary>Reads the data.</summary>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The data as the JSON payload of a pull result.</returns>
    ValueTask<string?> ReadAsync(CancellationToken ct);
}
