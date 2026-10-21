using Monitoring.Protocol.Messages;

namespace Monitoring.Client.Features.LocalServer;

/// <summary>Answers the sidecar's pulls with what the application knows.</summary>
/// <param name="sources">One source per kind of data the application provides.</param>
internal sealed class PullHandler(IEnumerable<IPullSource> sources)
{
    private readonly Dictionary<PullKind, IPullSource> _sources = sources.ToDictionary(source => source.Kind);

    /// <summary>Answers one pull.</summary>
    /// <param name="pull">The question.</param>
    /// <param name="ct">Cancels the answer.</param>
    /// <returns>The result; with no payload for kinds of data this version of the client does not provide yet.</returns>
    public async Task<PullResult> HandleAsync(Pull pull, CancellationToken ct)
    {
        var payload = _sources.TryGetValue(pull.Kind, out var source) ? await source.ReadAsync(ct).ConfigureAwait(false) : null;
        return new PullResult(pull.RequestId, pull.Kind, pull.Cursor, payload);
    }
}
