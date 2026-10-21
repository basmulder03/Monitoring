namespace Monitoring.Controller.Features.Registry;

/// <summary>Settings of the registry slice.</summary>
public sealed class RegistryOptions
{
    /// <summary>How long a removed instance stays visible as Gone before it is dropped.</summary>
    public TimeSpan GoneRetention { get; set; } = TimeSpan.FromMinutes(10);
}
