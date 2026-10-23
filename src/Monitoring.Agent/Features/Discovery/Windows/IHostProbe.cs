namespace Monitoring.Agent.Features.Discovery.Windows;

/// <summary>Reads what the machine and its processes use.</summary>
internal interface IHostProbe
{
    /// <summary>Reads the state of the machine now.</summary>
    /// <returns>The sample.</returns>
    HostSample ReadHost();

    /// <summary>Reads what a process uses.</summary>
    /// <param name="processId">The process id of a running service.</param>
    /// <returns>The sample, or <see langword="null"/> when the process does not exist or may not be read.</returns>
    ProcessSample? ReadProcess(int processId);
}
