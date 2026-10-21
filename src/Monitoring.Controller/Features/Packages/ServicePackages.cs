using Monitoring.Protocol.Reports;

namespace Monitoring.Controller.Features.Packages;

/// <summary>What one service is built from.</summary>
/// <param name="Scope">The scope the service runs in.</param>
/// <param name="Service">The name of the service.</param>
/// <param name="Version">The version of the service; when instances run different versions, the highest.</param>
/// <param name="Packages">The packages of the newest instance, ordered by name.</param>
public sealed record ServicePackages(string Scope, string Service, string? Version, IReadOnlyList<PackageReport> Packages);
