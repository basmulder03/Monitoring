using Monitoring.Controller.Features.Packages;

namespace Monitoring.Controller.Features.Alerting;

/// <summary>Fires for a package that must be used in the same version everywhere and is not.</summary>
/// <param name="inventory">Says which packages break their alignment rule.</param>
/// <param name="options">Provides the wait time.</param>
internal sealed class PackageDriftRule(IPackageInventory inventory, AlertingOptions options) : IAlertRule
{
    /// <inheritdoc />
    public string Name => "package-drift";

    /// <inheritdoc />
    public AlertSeverity Severity => AlertSeverity.Warning;

    /// <inheritdoc />
    public TimeSpan Wait => options.PackageDriftWait;

    /// <inheritdoc />
    public IEnumerable<string> FindFailingSubjects() =>
        inventory.GetCatalog().Packages.Where(entry => entry.Violation is not null).Select(entry => entry.Name);
}
