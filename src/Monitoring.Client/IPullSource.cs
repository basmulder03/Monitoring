using Monitoring.Protocol.Messages;

namespace Monitoring.Client;

/// <summary>One kind of data the application can give the sidecar. Each data slice offers its own, so the local server never needs to know the slices.</summary>
internal interface IPullSource
{
    /// <summary>The kind of data this source answers pulls for.</summary>
    PullKind Kind { get; }

    /// <summary>Reads the data.</summary>
    /// <param name="cursor">The position the parent has seen so far, as it passed in the pull; 0 asks for everything. Sources whose data is a running total ignore it.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <returns>The data as the JSON payload of a pull result, and the position to pass in the next pull.</returns>
    ValueTask<PullData> ReadAsync(long cursor, CancellationToken ct);
}
