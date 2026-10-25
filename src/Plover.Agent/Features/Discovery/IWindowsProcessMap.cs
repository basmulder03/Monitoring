namespace Plover.Agent.Features.Discovery;

/// <summary>Tells which discovered Windows service runs in a process, so an application that connects over the pipe can be matched with its service.</summary>
public interface IWindowsProcessMap
{
    /// <summary>Finds the service that runs in a process.</summary>
    /// <param name="processId">The process id the application announced.</param>
    /// <returns>The id of the instance of the service, or <see langword="null"/> when no listed, running service has that process.</returns>
    string? InstanceOfProcess(int processId);
}
