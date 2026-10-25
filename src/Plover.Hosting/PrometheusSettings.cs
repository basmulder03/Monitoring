using Plover.Common.Prometheus;

namespace Plover.Hosting;

/// <summary>Reads the optional Prometheus export, which every executable spells the same way.</summary>
public static class PrometheusSettings
{
    /// <summary>Reads the settings of a <c>Prometheus</c> section.</summary>
    /// <param name="prometheus">The section; the export is off unless <c>Port</c> is set.</param>
    /// <param name="options">The options to fill in.</param>
    /// <exception cref="ConfigurationException">A value cannot be read.</exception>
    public static void Read(SettingsReader prometheus, PrometheusOptions options)
    {
        options.Port = prometheus.Int("Port", options.Port);
        options.Address = prometheus.Text("Address", options.Address);
    }
}
