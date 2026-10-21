namespace Monitoring.Protocol.Reports;

/// <summary>One package an application is built from.</summary>
/// <param name="Name">Package id, such as <c>Microsoft.Extensions.Hosting</c>.</param>
/// <param name="Version">The version that was restored and built into the application.</param>
/// <param name="Direct"><see langword="true"/> when the project asks for the package itself, <see langword="false"/> when it comes in through another package.</param>
public sealed record PackageReport(string Name, string Version, bool Direct);
