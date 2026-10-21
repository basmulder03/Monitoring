using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;

namespace Monitoring.Client.Features.Info;

/// <summary>Answers pulls for the application's name and version.</summary>
/// <param name="describer">Works out the description.</param>
internal sealed class InfoPullSource(InstanceDescriber describer) : IPullSource
{
    /// <inheritdoc />
    public PullKind Kind => PullKind.Info;

    /// <inheritdoc />
    public ValueTask<PullData> ReadAsync(long cursor, CancellationToken ct) =>
        ValueTask.FromResult(new PullData(ReportPayload.Write(describer.Describe()), cursor));
}
