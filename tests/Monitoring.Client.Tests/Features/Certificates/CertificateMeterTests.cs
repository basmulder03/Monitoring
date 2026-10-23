using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Time.Testing;
using Monitoring.Client.Features.Certificates;
using Monitoring.Client.Features.LocalServer;
using Shouldly;
using Xunit;

namespace Monitoring.Client.Tests.Features.Certificates;

// The metrics collector listens to every meter of the process, so tests that make meters must not overlap with its tests.
[Collection("Meters")]
public class CertificateMeterTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("certs-").FullName;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 2, 8, 0, 0, TimeSpan.Zero));

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WriteCertificate(string name, DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create();
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(notAfter.AddDays(-30), notAfter);
        var path = Path.Combine(_directory, $"{name}.pem");
        File.WriteAllText(path, certificate.ExportCertificatePem());
        return path;
    }

    private Dictionary<string, double> Read(params CertificateWatch[] watches)
    {
        using var meter = new CertificateMeter(new MonitoringOptions { Certificates = [.. watches] }, _clock);
        var found = new Dictionary<string, double>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == CertificateMeter.MeterName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            },
        };
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) => found[(string)tags[0].Value!] = value);
        listener.Start();
        listener.RecordObservableInstruments();
        return found;
    }

    [Fact]
    public void A_certificate_in_a_file_is_reported_with_the_days_it_has_left()
    {
        // Arrange
        var path = WriteCertificate("https", _clock.GetUtcNow().AddDays(10));

        // Act
        var days = Read(CertificateWatch.File("https", path));

        // Assert
        days["https"].ShouldBe(10, tolerance: 0.01);
    }

    [Fact]
    public void An_expired_certificate_has_negative_days()
    {
        // Arrange
        var path = WriteCertificate("old", _clock.GetUtcNow().AddDays(-2));

        // Act
        var days = Read(CertificateWatch.File("old", path));

        // Assert
        days["old"].ShouldBe(-2, tolerance: 0.01);
    }

    [Fact]
    public void A_certificate_that_is_missing_or_unreadable_is_left_out_and_the_others_are_still_reported()
    {
        // Arrange
        var good = WriteCertificate("good", _clock.GetUtcNow().AddDays(5));
        var garbage = Path.Combine(_directory, "garbage.pem");
        File.WriteAllText(garbage, "not a certificate");

        // Act
        var days = Read(
            CertificateWatch.File("missing", Path.Combine(_directory, "none.pem")),
            CertificateWatch.File("garbage", garbage),
            CertificateWatch.File("good", good));

        // Assert
        days.Keys.ShouldBe(["good"]);
    }

    [Fact]
    public void A_renewed_certificate_is_noticed_without_a_restart()
    {
        // Arrange
        var path = WriteCertificate("https", _clock.GetUtcNow().AddDays(1));
        var watch = CertificateWatch.File("https", path);
        Read(watch)["https"].ShouldBe(1, tolerance: 0.01);

        // Act
        File.Copy(WriteCertificate("renewed", _clock.GetUtcNow().AddDays(90)), path, overwrite: true);

        // Assert
        Read(watch)["https"].ShouldBe(90, tolerance: 0.01);
    }

    [Fact]
    public void A_certificate_from_code_is_reported_too()
    {
        // Arrange
        var path = WriteCertificate("code", _clock.GetUtcNow().AddDays(3));

        // Act
        var days = Read(new CertificateWatch("served", () => X509CertificateLoader.LoadCertificateFromFile(path)));

        // Assert
        days["served"].ShouldBe(3, tolerance: 0.01);
    }
}
