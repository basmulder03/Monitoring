namespace Monitoring.Protocol.Reports;

/// <summary>What an application is built from; the answer to a pull for <see cref="Messages.PullKind.Packages"/>.</summary>
/// <param name="Runtime">The runtime the application runs on, such as <c>.NET 10.0.0</c>.</param>
/// <param name="Packages">The packages, ordered by name; empty when the application was built without the list.</param>
public sealed record PackagesReport(string Runtime, IReadOnlyList<PackageReport> Packages);
