using System.Net;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Plover.Controller.Features.Alerting;
using Plover.Controller.Features.StateStore;

namespace Plover.Controller.Features.Notifications;

/// <summary>Registers the notifications slice.</summary>
public static class NotificationsSlice
{
    /// <summary>
    /// Adds the channels that alerts are sent to. Nothing is added when no channel is configured. It reads the alerts, so <c>AddAlerting</c> must be called as well, and it keeps what it told in the state store.
    /// </summary>
    /// <param name="services">The service collection to extend.</param>
    /// <param name="configure">Sets the <see cref="NotificationOptions"/>.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The interval is not positive.</exception>
    /// <exception cref="ArgumentException">A channel has an address that is not absolute http or https, the email channel misses its host, sender or receivers, or the severity or leader mode is unknown.</exception>
    public static IServiceCollection AddNotifications(this IServiceCollection services, Action<NotificationOptions> configure)
    {
        var options = new NotificationOptions();
        configure(options);
        var email = options.Email.Host is { Length: > 0 };
        if (options.Webhooks.Count == 0 && options.Chats.Count == 0 && !email)
        {
            return services;
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Interval, TimeSpan.Zero);
        Validate(options, email);

        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton<IReadOnlyList<IAlertSink>>(_ =>
        {
            var source = options.Source ?? Environment.MachineName;
            var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            List<IAlertSink> sinks = [];
            sinks.AddRange(options.Webhooks.Select(target => new WebhookSink(http, target, source)));
            sinks.AddRange(options.Chats.Select(target => new ChatSink(http, target, source)));
            if (email)
            {
                sinks.Add(new EmailSink(new SmtpSender(options.Email), options.Email.Host!, source));
            }

            return sinks;
        });

        if (options.Leader.Mode.Equals("lease", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton(provider => new LeaseLeaderElection(
                new HttpLeaseApi(InClusterClient(options.Leader), options.Leader.LeaseNamespace ?? CurrentNamespace(), options.Leader.LeaseName),
                options.Leader.LeaseName, Environment.MachineName, options.Leader.LeaseDuration, provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<LeaseLeaderElection>>()));
            services.AddSingleton<ILeaderElection>(provider => provider.GetRequiredService<LeaseLeaderElection>());
            services.AddHostedService(provider => provider.GetRequiredService<LeaseLeaderElection>());
        }
        else if (options.Leader.Mode.Equals("peers", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<ILeaderElection>(provider => new PeerLeaderElection(provider.GetRequiredService<IReplicaPeers>()));
        }
        else
        {
            services.AddSingleton<ILeaderElection, AlwaysLeader>();
        }

        services.TryAddSingleton<IStateStore, MemoryStateStore>();
        services.AddHostedService(provider => new AlertNotifier(
            provider.GetRequiredService<IAlertState>(), provider.GetRequiredService<IReadOnlyList<IAlertSink>>(), provider.GetRequiredService<ILeaderElection>(),
            options, provider.GetRequiredService<IStateStore>(), provider.GetRequiredService<ILogger<AlertNotifier>>()));
        return services;
    }

    private static void Validate(NotificationOptions options, bool email)
    {
        foreach (var url in options.Webhooks.Select(target => target.Url).Concat(options.Chats.Select(target => target.Url)))
        {
            if (!url.IsAbsoluteUri || url.Scheme is not ("http" or "https"))
            {
                throw new ArgumentException($"The address of a channel must be an absolute http or https address, but is '{url}'.", nameof(options));
            }
        }

        if (email && (string.IsNullOrWhiteSpace(options.Email.From) || options.Email.To.Count == 0))
        {
            throw new ArgumentException("The email channel needs a sender (From) and at least one receiver (To).", nameof(options));
        }

        if (options.MinSeverity.ToLowerInvariant() is not ("warning" or "critical"))
        {
            throw new ArgumentException($"The lowest severity must be 'warning' or 'critical', but is '{options.MinSeverity}'.", nameof(options));
        }

        if (options.Leader.Mode.ToLowerInvariant() is not ("none" or "lease" or "peers"))
        {
            throw new ArgumentException($"The leader mode must be 'none', 'lease' or 'peers', but is '{options.Leader.Mode}'.", nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(options.Leader.LeaseDuration, TimeSpan.FromSeconds(3));
    }

    private const string InCluster = "/var/run/secrets/kubernetes.io/serviceaccount";

    private static string CurrentNamespace() => File.Exists($"{InCluster}/namespace") ? File.ReadAllText($"{InCluster}/namespace").Trim() : "default";

    /// <summary>The client for the API server of the cluster the controller runs in; its requests carry the token of the controller, read again for each since Kubernetes rotates it.</summary>
    private static HttpClient InClusterClient(LeaderOptions leader)
    {
        var host = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
        var address = leader.ApiServer
            ?? (host is { Length: > 0 } ? $"https://{(host.Contains(':') ? $"[{host}]" : host)}:{Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT") ?? "443"}" : null)
            ?? throw new ArgumentException("Choosing the replica that sends alerts with a lease needs the address of the API server (Notifications:Leader:ApiServer), or a controller that runs in a cluster.", nameof(leader));

        var inner = new SocketsHttpHandler { UseProxy = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        if (File.Exists($"{InCluster}/ca.crt"))
        {
            var authorities = new X509Certificate2Collection();
            authorities.ImportFromPemFile($"{InCluster}/ca.crt");
            inner.SslOptions.RemoteCertificateValidationCallback = (_, certificate, chain, errors) => Trusts(authorities, certificate, errors);
        }

        return new HttpClient(new TokenHandler($"{InCluster}/token") { InnerHandler = inner }, disposeHandler: true) { BaseAddress = new Uri(address.EndsWith('/') ? address : address + "/"), Timeout = TimeSpan.FromSeconds(10) };
    }

    private static bool Trusts(X509Certificate2Collection authorities, X509Certificate? certificate, System.Net.Security.SslPolicyErrors errors)
    {
        if (errors == System.Net.Security.SslPolicyErrors.None)
        {
            return true;
        }

        if (certificate is null || (errors & ~System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors) != 0)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(authorities);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(new X509Certificate2(certificate));
    }

    private sealed class TokenHandler(string file) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (File.Exists(file))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", File.ReadAllText(file).Trim());
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
