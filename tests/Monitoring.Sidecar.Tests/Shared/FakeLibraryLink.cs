using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;
using Monitoring.Sidecar.Features.LibraryLink;

namespace Monitoring.Sidecar.Tests.Shared;

/// <summary>An application that answers the pulls the sidecar passes on as the test says.</summary>
internal sealed class FakeLibraryLink : ILibraryLink
{
    private readonly Dictionary<PullKind, (string Payload, long Cursor)> _answers = [];

    /// <summary>What the application was asked, so a test can check that the agent's cursor reached it.</summary>
    public List<(PullKind Kind, long Cursor)> Asked { get; } = [];

    /// <summary>Makes the application answer pulls of a kind; kinds without an answer play an application that cannot be reached.</summary>
    /// <param name="kind">The kind of data.</param>
    /// <param name="payload">The JSON the application answers with.</param>
    /// <param name="cursor">The position it answers with.</param>
    /// <returns>This fake, for chaining.</returns>
    public FakeLibraryLink Answering(PullKind kind, string payload, long cursor = 0)
    {
        _answers[kind] = (payload, cursor);
        return this;
    }

    /// <inheritdoc />
    public Task<InstanceDescription?> GetDescriptionAsync(CancellationToken ct) => Task.FromResult<InstanceDescription?>(null);

    /// <inheritdoc />
    public Task<HealthReport?> GetHealthAsync(CancellationToken ct) => Task.FromResult<HealthReport?>(null);

    /// <inheritdoc />
    public Task<PullResult?> PullAsync(PullKind kind, long cursor, CancellationToken ct)
    {
        Asked.Add((kind, cursor));
        return Task.FromResult(_answers.TryGetValue(kind, out var answer) ? new PullResult(0, kind, answer.Cursor, answer.Payload) : null);
    }
}
