using Monitoring.Client.Features.Health;
using Monitoring.Client.Features.Info;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Reports;

namespace Monitoring.Client.Features.LocalServer;

/// <summary>Answers the sidecar's pulls with what the application knows.</summary>
/// <param name="describer">Answers pulls for the application's description.</param>
/// <param name="health">Answers pulls for the application's health.</param>
internal sealed class PullHandler(InstanceDescriber describer, HealthReporter health)
{
    /// <summary>Answers one pull.</summary>
    /// <param name="pull">The question.</param>
    /// <param name="ct">Cancels the answer.</param>
    /// <returns>The result; with no payload for kinds of data this version of the client does not provide yet.</returns>
    public async Task<PullResult> HandleAsync(Pull pull, CancellationToken ct)
    {
        var payload = pull.Kind switch
        {
            PullKind.Info => ReportPayload.Write(describer.Describe()),
            PullKind.Health => ReportPayload.Write(await health.ReportAsync(ct).ConfigureAwait(false)),
            _ => null,
        };

        return new PullResult(pull.RequestId, pull.Kind, pull.Cursor, payload);
    }
}
