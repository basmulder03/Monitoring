namespace Monitoring.Agent.Features.Discovery.Windows;

/// <summary>What the Service Control Manager says about one Windows service at one moment.</summary>
/// <param name="Name">The short name of the service, which is how it is registered.</param>
/// <param name="DisplayName">The name shown in the Services console.</param>
/// <param name="State">What the service is doing.</param>
/// <param name="StartType">How the service is started.</param>
internal sealed record WindowsServiceStatus(string Name, string DisplayName, WindowsServiceState State, WindowsServiceStartType StartType);

/// <summary>What a Windows service is doing. The numbers are the ones Windows uses, which start at 1.</summary>
public enum WindowsServiceState : byte
{
    /// <summary>Not running.</summary>
    Stopped = 1,

    /// <summary>Starting up.</summary>
    StartPending = 2,

    /// <summary>Shutting down.</summary>
    StopPending = 3,

    /// <summary>Running.</summary>
    Running = 4,

    /// <summary>About to continue after a pause.</summary>
    ContinuePending = 5,

    /// <summary>About to pause.</summary>
    PausePending = 6,

    /// <summary>Paused.</summary>
    Paused = 7,
}

/// <summary>How a Windows service is started. The numbers are the ones Windows uses.</summary>
public enum WindowsServiceStartType : byte
{
    /// <summary>By the system at boot.</summary>
    Boot = 0,

    /// <summary>By the system at start up.</summary>
    System = 1,

    /// <summary>Automatically when the machine starts.</summary>
    Automatic = 2,

    /// <summary>When something asks for it.</summary>
    Manual = 3,

    /// <summary>Not at all.</summary>
    Disabled = 4,
}
