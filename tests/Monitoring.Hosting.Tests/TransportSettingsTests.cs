using Microsoft.Extensions.Configuration;
using Monitoring.Common.Transport;
using Monitoring.Hosting;
using Shouldly;
using Xunit;

namespace Monitoring.Hosting.Tests;

public class TransportSettingsTests
{
    [Fact]
    public void Read_defaults_to_plain_tcp_when_nothing_is_set()
    {
        // Arrange
        var section = Reader([]);

        // Act
        var options = TransportSettings.Read(section);

        // Assert
        options.Mode.ShouldBe(TransportMode.None);
        options.HandshakeTimeout.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData("none", TransportMode.None)]
    [InlineData("TLS", TransportMode.Tls)]
    [InlineData("Mtls", TransportMode.Mtls)]
    public void Read_understands_the_mode_whatever_its_case(string text, TransportMode mode)
    {
        // Arrange
        var section = Reader(new() { ["Mode"] = text });

        // Act
        var options = TransportSettings.Read(section);

        // Assert
        options.Mode.ShouldBe(mode);
    }

    [Fact]
    public void Read_names_the_setting_when_the_mode_is_unknown()
    {
        // Arrange
        var section = Reader(new() { ["Mode"] = "ssl" });

        // Act
        var read = () => TransportSettings.Read(section);

        // Assert
        read.ShouldThrow<ConfigurationException>().Message.ShouldContain("Agent:Transport:Mode");
    }

    [Fact]
    public void Read_reads_the_files_names_and_timeout()
    {
        // Arrange
        var section = Reader(new()
        {
            ["Mode"] = "mtls",
            ["Certificate"] = "/certs/tls.crt",
            ["Key"] = "/certs/tls.key",
            ["CaFile"] = "/certs/ca.crt",
            ["ClientCaFile"] = "/certs/clients.crt",
            ["ServerName"] = "controller.example",
            ["HandshakeTimeout"] = "00:00:03",
        });

        // Act
        var options = TransportSettings.Read(section);

        // Assert
        options.CertificateFile.ShouldBe("/certs/tls.crt");
        options.KeyFile.ShouldBe("/certs/tls.key");
        options.CaFile.ShouldBe("/certs/ca.crt");
        options.ClientCaFile.ShouldBe("/certs/clients.crt");
        options.ServerName.ShouldBe("controller.example");
        options.HandshakeTimeout.ShouldBe(TimeSpan.FromSeconds(3));
    }

    private static SettingsReader Reader(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => $"Agent:Transport:{pair.Key}", pair => pair.Value))
            .Build();
        return new SettingsReader(configuration).Section("Agent").Section("Transport");
    }
}
