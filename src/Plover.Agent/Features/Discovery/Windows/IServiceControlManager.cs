namespace Plover.Agent.Features.Discovery.Windows;

/// <summary>The part of the Windows Service Control Manager that discovery needs: which services exist and what state they are in.</summary>
internal interface IServiceControlManager
{
    /// <summary>Lists every service on the machine.</summary>
    /// <returns>The services, in no particular order.</returns>
    /// <exception cref="InvalidOperationException">The Service Control Manager cannot be read, for example for lack of permission.</exception>
    IReadOnlyList<WindowsServiceStatus> List();
}
