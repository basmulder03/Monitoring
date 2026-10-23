using Monitoring.Protocol.Reports;

namespace Monitoring.Sidecar.Features.SelfMonitoring;

/// <summary>What the sidecar tells about itself, which goes to the agent with the metrics of the application.</summary>
public interface ISelfMetrics
{
    /// <summary>Records that the application was asked for data.</summary>
    /// <param name="took">How long the question took.</param>
    /// <param name="answered">Whether the application answered, which tells whether the link to it is up.</param>
    void LibraryPulled(TimeSpan took, bool answered);

    /// <summary>Reads the current values.</summary>
    /// <returns>One series per value, named <c>monitoring.sidecar.*</c>.</returns>
    IReadOnlyList<MetricSeries> Read();
}
