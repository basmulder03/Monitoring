using System.Reflection;
using Plover.Protocol.Messages;
using Plover.Protocol.Reports;

namespace Plover.Client.Features.Packages;

/// <summary>Answers pulls for the packages the application is built from. The list cannot change while the application runs, so it is read once.</summary>
internal sealed class PackagesPullSource : IPullSource
{
    private readonly Lazy<string> _payload = new(() => ReportPayload.Write(PackageList.Read(Assembly.GetEntryAssembly())));

    /// <inheritdoc />
    public PullKind Kind => PullKind.Packages;

    /// <inheritdoc />
    public ValueTask<PullData> ReadAsync(long cursor, CancellationToken ct) => ValueTask.FromResult(new PullData(_payload.Value, cursor));
}
