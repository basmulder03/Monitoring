using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Monitoring.Controller.Features.Configuration;
using Monitoring.Controller.Features.Liveness;
using Monitoring.Controller.Features.Registry;
using Monitoring.Controller.Features.Vulnerabilities;

namespace Monitoring.Controller.Features.Alerting;

/// <summary>Registers the alerting slice.</summary>
public static class AlertingSlice
{
    /// <summary>
    /// Adds the default rules, the evaluator and the background service that runs it. It reads from the registry and the liveness tracking, so <c>AddRegistry</c>, <c>AddLiveness</c>, <c>AddConfiguration</c>, <c>AddPackages</c> and <c>AddVulnerabilities</c> must be called as well.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Optionally changes the <see cref="AlertingOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A configured duration is negative or the evaluation interval is not positive.</exception>
    public static IServiceCollection AddAlerting(this IServiceCollection services, Action<AlertingOptions>? configure = null)
    {
        var options = new AlertingOptions();
        configure?.Invoke(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.StartupGrace, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.InstanceUnhealthyWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ServiceDownWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ScopeUnknownWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PackageDriftWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.VulnerablePackageWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.RestartLoopCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.EdgeErrorWindow, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.EdgeErrorShare, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.EdgeErrorShare, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.EdgeErrorMinCalls, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ConfigurationDriftWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.DroppedDataWindow, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.DroppedDataSpans, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ReplicaLagThreshold, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ReplicaBehindWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ReplicaUnreachableWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.QueueWithoutConsumersWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ConnectionPoolFullWait, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.CertificateCriticalDays, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.CertificateWarningDays, options.CertificateCriticalDays);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.RestartLoopWindow, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.EvaluationInterval, TimeSpan.Zero);
        foreach (var silence in options.Silences.Where(silence => string.IsNullOrWhiteSpace(silence.Rule)))
        {
            throw new ArgumentException($"A silence needs the name of a rule, or '*' for every rule (it ends {silence.Until:u}).", nameof(configure));
        }

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<IAlertRule, InstanceUnhealthyRule>();
        services.AddSingleton<IAlertRule, ServiceDownRule>();
        services.AddSingleton<IAlertRule, ScopeUnknownRule>();
        services.AddSingleton<IAlertRule, RestartLoopRule>();
        services.AddSingleton<IAlertRule, PackageDriftRule>();
        services.AddSingleton<IAlertRule>(provider => new VulnerablePackageRule(
            "vulnerable-package-critical", AlertSeverity.Critical, serious: true, provider.GetRequiredService<IVulnerabilities>(), options));
        services.AddSingleton<IAlertRule>(provider => new VulnerablePackageRule(
            "vulnerable-package", AlertSeverity.Warning, serious: false, provider.GetRequiredService<IVulnerabilities>(), options));
        services.AddSingleton<IAlertRule>(provider => new CertificateExpiryRule(
            "certificate-expiring", AlertSeverity.Warning, provider.GetRequiredService<IScopeRegistry>(), options.CertificateWarningDays, options.CertificateCriticalDays));
        services.AddSingleton<IAlertRule>(provider => new CertificateExpiryRule(
            "certificate-expired-soon", AlertSeverity.Critical, provider.GetRequiredService<IScopeRegistry>(), options.CertificateCriticalDays, null));
        services.AddSingleton<IAlertRule>(provider => new ConnectionPoolRule(options.ConnectionPoolFullWait, provider.GetRequiredService<IScopeRegistry>()));
        services.AddSingleton<IAlertRule>(provider => new QueueRule(
            "queue-dead-letters", AlertSeverity.Warning, TimeSpan.Zero, provider.GetRequiredService<IScopeRegistry>(), queue => queue.DeadLetters > 0));
        services.AddSingleton<IAlertRule>(provider => new QueueRule(
            "queue-without-consumers", AlertSeverity.Critical, options.QueueWithoutConsumersWait, provider.GetRequiredService<IScopeRegistry>(), queue => queue.Depth > 0 && queue.Consumers == 0));
        services.AddSingleton<IAlertRule>(provider => new ReplicaRule(
            "replica-behind", provider.GetRequiredService<IScopeRegistry>(), options.ReplicaBehindWait, report => report.Lag >= options.ReplicaLagThreshold));
        services.AddSingleton<IAlertRule>(provider => new ReplicaRule(
            "replica-unreachable", provider.GetRequiredService<IScopeRegistry>(), options.ReplicaUnreachableWait, report => !report.Connected));
        services.AddSingleton<IAlertRule>(provider => new EdgeErrorRateRule(provider.GetRequiredService<IScopeRegistry>(), options, provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IAlertRule>(provider => new DroppedDataRule(provider.GetRequiredService<IScopeRegistry>(), options, provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IAlertRule>(provider => new ConfigurationDriftRule(
            provider.GetRequiredService<IScopeRegistry>(), provider.GetRequiredService<IAgentLiveness>(), provider.GetRequiredService<IAppliedConfigurations>(), options));
        services.AddSingleton<AlertEvaluator>();
        services.AddSingleton<IAlertEvaluator>(provider => provider.GetRequiredService<AlertEvaluator>());
        services.AddSingleton<IAlertState>(provider => provider.GetRequiredService<AlertEvaluator>());
        services.AddHostedService<AlertEvaluationService>();
        return services;
    }
}
