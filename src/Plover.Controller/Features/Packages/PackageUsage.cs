namespace Plover.Controller.Features.Packages;

/// <summary>A service that uses one version of a package.</summary>
/// <param name="Scope">The scope the service runs in.</param>
/// <param name="Service">The name of the service.</param>
/// <param name="Instances">How many live instances of the service use this version.</param>
/// <param name="Direct"><see langword="true"/> when the service asks for the package itself, <see langword="false"/> when it comes in through another package.</param>
public sealed record PackageUsage(string Scope, string Service, int Instances, bool Direct);
