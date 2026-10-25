namespace Plover.Protocol.Reports;

/// <summary>What an application says about itself; the answer to a pull for <see cref="Messages.PullKind.Info"/>.</summary>
/// <param name="ServiceName">Name of the service the application is part of.</param>
/// <param name="Version">Version of the application; <see langword="null"/> when it does not say.</param>
public sealed record InstanceDescription(string ServiceName, string? Version = null);
