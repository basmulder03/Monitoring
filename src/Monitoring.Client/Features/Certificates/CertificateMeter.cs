using System.Diagnostics.Metrics;
using Monitoring.Client.Features.LocalServer;

namespace Monitoring.Client.Features.Certificates;

/// <summary>Reports the days left until each watched certificate expires, as the gauge <c>monitoring.certificate.expiry_days</c> tagged with the name of the certificate.</summary>
/// <remarks>A certificate that cannot be read is left out for that reading, so a gap in the series is the sign; reading it never takes the application down.</remarks>
internal sealed class CertificateMeter : IDisposable
{
    /// <summary>The name of the meter, which carries the one instrument.</summary>
    public const string MeterName = "Monitoring.Client.Certificates";

    private readonly Meter _meter = new(MeterName);
    private readonly IReadOnlyList<CertificateWatch> _watches;
    private readonly TimeProvider _time;

    /// <summary>Creates the instrument.</summary>
    /// <param name="options">Lists the certificates to watch.</param>
    /// <param name="time">Clock for the expiry.</param>
    public CertificateMeter(MonitoringOptions options, TimeProvider time)
    {
        _watches = [.. options.Certificates];
        _time = time;
        _meter.CreateObservableGauge("monitoring.certificate.expiry_days", Observe, unit: "d", description: "Days until the certificate expires; negative once it has.");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<double>> Observe()
    {
        foreach (var watch in _watches)
        {
            double? days = null;
            try
            {
                using var certificate = watch.Load();
                if (certificate is not null)
                {
                    days = (certificate.NotAfter.ToUniversalTime() - _time.GetUtcNow().UtcDateTime).TotalDays;
                }
            }
            catch (Exception ex) when (ex is IOException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
            {
                // Unreadable right now, for example while a renewal is being written; the next reading tries again.
            }

            if (days is { } left)
            {
                yield return new Measurement<double>(left, new KeyValuePair<string, object?>("certificate", watch.Name));
            }
        }
    }
}
