using System.Diagnostics;

namespace Monitoring.Client.Features.Tracing;

/// <summary>Works out from a finished client operation what kind of dependency it called and which one.</summary>
internal static class EdgeClassifier
{
    /// <summary>Classifies an operation.</summary>
    /// <param name="activity">A finished operation.</param>
    /// <param name="type">Receives <c>HTTP</c>, <c>DB</c> or <c>MQ</c>.</param>
    /// <param name="target">Receives where the call went.</param>
    /// <returns><see langword="true"/> when the operation is a call to a dependency that is worth an edge.</returns>
    public static bool TryClassify(Activity activity, out string type, out string target)
    {
        type = target = "";
        if (activity.Kind is not (ActivityKind.Client or ActivityKind.Producer))
        {
            return false;
        }

        var host = Tag(activity, "server.address") ?? Tag(activity, "net.peer.name");
        var port = Tag(activity, "server.port") ?? Tag(activity, "net.peer.port");

        if ((Tag(activity, "db.system.name") ?? Tag(activity, "db.system")) is { } database)
        {
            var name = Tag(activity, "db.namespace") ?? Tag(activity, "db.name");
            type = "DB";
            target = $"{database}://{Endpoint(host, port)}{(name is null ? "" : "/" + name)}";
            return host is not null || name is not null;
        }

        if (Tag(activity, "messaging.system") is { } broker)
        {
            var destination = Tag(activity, "messaging.destination.name") ?? Tag(activity, "messaging.destination");
            type = "MQ";
            target = $"{broker}://{destination ?? Endpoint(host, port)}";
            return destination is not null || host is not null;
        }

        if (host is not null && (Tag(activity, "http.request.method") ?? Tag(activity, "http.method")) is not null)
        {
            type = "HTTP";
            target = Endpoint(host, port);
            return true;
        }

        return false;
    }

    private static string Endpoint(string? host, string? port) => host is null ? "" : port is null ? host : $"{host}:{port}";

    private static string? Tag(Activity activity, string key) => activity.GetTagItem(key)?.ToString();
}
