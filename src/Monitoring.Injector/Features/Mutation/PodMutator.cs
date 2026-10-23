using System.Text.Json.Nodes;

namespace Monitoring.Injector.Features.Mutation;

/// <summary>
/// Adds the monitoring sidecar to a pod: the container, a small in-memory volume for the socket the application listens on, and for each container of the
/// application the volume and the name of the socket. It builds the change from the same definition the Helm template makes, which a test checks, so injecting
/// and including the template give the same pod.
/// </summary>
/// <param name="options">What the sidecar looks like.</param>
public sealed class PodMutator(InjectorOptions options)
{
    /// <summary>The volume the socket lives on.</summary>
    public const string SocketVolume = "monitoring-socket";

    /// <summary>The folder the volume is mounted at, in the sidecar and in the application.</summary>
    public const string SocketFolder = "/var/run/monitoring";

    /// <summary>The socket the application listens on and the sidecar connects to.</summary>
    public const string SocketPath = SocketFolder + "/monitoring.sock";

    /// <summary>Works out the JSON patch that adds the sidecar to a pod.</summary>
    /// <param name="pod">The pod as the API server sends it for admission.</param>
    /// <param name="namespaceOptedIn">Whether the webhook was called because the namespace of the pod has the label, which the API server does not say in the review: the registration for namespaces calls with it, the one for pods without.</param>
    /// <returns>The operations of the patch; empty when the pod is not to be changed, because it opted out, is not opted in, or already has the sidecar.</returns>
    public JsonArray Mutate(JsonObject pod, bool namespaceOptedIn)
    {
        var patch = new JsonArray();
        if (!WantsSidecar(pod, namespaceOptedIn) || HasSidecar(pod))
        {
            return patch;
        }

        var spec = pod["spec"] as JsonObject ?? throw new ArgumentException("The pod has no spec.", nameof(pod));
        var sidecars = spec[options.NativeSidecar ? "initContainers" : "containers"] as JsonArray;
        Add(patch, $"/spec/{(options.NativeSidecar ? "initContainers" : "containers")}", sidecars, Sidecar());
        Add(patch, "/spec/volumes", spec["volumes"] as JsonArray, Volume());

        // The containers of the application, as they were: the sidecar added to the same list is not one of them.
        var containers = spec["containers"] as JsonArray ?? [];
        for (var i = 0; i < containers.Count; i++)
        {
            if (containers[i] is not JsonObject container || container["name"]?.GetValue<string>() == InjectorOptions.SidecarContainer)
            {
                continue;
            }

            Add(patch, $"/spec/containers/{i}/env", container["env"] as JsonArray, Json("""{"name":"MONITORING_SOCKET","value":"/var/run/monitoring/monitoring.sock"}"""));
            Add(patch, $"/spec/containers/{i}/volumeMounts", container["volumeMounts"] as JsonArray, Mount());
        }

        return patch;
    }

    /// <summary>The sidecar container.</summary>
    /// <returns>The container, as the pod spec has it.</returns>
    public JsonObject Sidecar()
    {
        var env = new JsonArray();
        Append(env, Json($$"""{"name":"MONITORING_Sidecar__Agent","value":{{Quote(options.AgentAddress)}}}"""));
        Append(env, Json($$"""{"name":"MONITORING_Sidecar__Library__Socket","value":"{{SocketPath}}"}"""));
        foreach (var (key, value) in options.Labels.OrderBy(label => label.Key, StringComparer.Ordinal))
        {
            Append(env, Json($$"""{"name":"MONITORING_Sidecar__Labels__{{key}}","value":{{Quote(value)}}}"""));
        }

        var container = Json($$"""
            {
              "name": "{{InjectorOptions.SidecarContainer}}",
              "image": {{Quote(options.SidecarImage)}},
              "imagePullPolicy": {{Quote(options.ImagePullPolicy)}}
            }
            """);
        if (options.NativeSidecar)
        {
            container["restartPolicy"] = "Always";
        }

        container["securityContext"] = Json("""{"allowPrivilegeEscalation":false,"readOnlyRootFilesystem":true,"capabilities":{"drop":["ALL"]}}""");
        container["env"] = env;
        var mounts = new JsonArray();
        Append(mounts, Mount());
        container["volumeMounts"] = mounts;
        container["resources"] = Json("""{"requests":{"cpu":"10m","memory":"16Mi"},"limits":{"memory":"64Mi"}}""");
        return container;
    }

    /// <summary>The volume the socket lives on: memory only and tiny, because it holds a socket and nothing else.</summary>
    /// <returns>The volume, as the pod spec has it.</returns>
    public static JsonObject Volume() => Json("""{"name":"monitoring-socket","emptyDir":{"medium":"Memory","sizeLimit":"1Mi"}}""");

    private static JsonObject Mount() => Json("""{"name":"monitoring-socket","mountPath":"/var/run/monitoring"}""");

    /// <summary>A pod is given the sidecar when it or its namespace says so, and the pod does not say no.</summary>
    private static bool WantsSidecar(JsonObject pod, bool namespaceOptedIn)
    {
        var podLabel = pod["metadata"]?["labels"]?[InjectorOptions.InjectLabel]?.GetValue<string>();
        return podLabel is null ? namespaceOptedIn : string.Equals(podLabel, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSidecar(JsonObject pod) =>
        new[] { "initContainers", "containers" }.Any(list =>
            pod["spec"]?[list] is JsonArray containers && containers.Any(c => c?["name"]?.GetValue<string>() == InjectorOptions.SidecarContainer));

    /// <summary>Adds an item to a list of the pod: to the list when it is there, and by creating the list with it when it is not.</summary>
    private static void Add(JsonArray patch, string path, JsonArray? existing, JsonNode item) =>
        Append(patch, existing is null
            ? Json($$"""{"op":"add","path":"{{path}}","value":[{{item.ToJsonString()}}]}""")
            : Json($$"""{"op":"add","path":"{{path}}/-","value":{{item.ToJsonString()}}}"""));

    /// <summary>Adds a node to an array. Typed as a node, so the overload that needs no code generated at run time is the one used, which Native AOT demands.</summary>
    private static void Append(JsonArray array, JsonNode item) => array.Add(item);

    private static JsonObject Json(string text) => (JsonObject)JsonNode.Parse(text)!;

    private static string Quote(string value) => JsonValue.Create(value)!.ToJsonString();
}
