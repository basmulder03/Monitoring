using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Plover.Injector.Features.Mutation;
using Shouldly;
using Xunit;

namespace Plover.Injector.Tests;

public class PodMutatorTests
{
    private static PodMutator Mutator(bool native = true) => new(new InjectorOptions
    {
        SidecarImage = "plover-sidecar:1.2.3",
        AgentAddress = "plover-agent:9441",
        NativeSidecar = native,
        Labels = { ["team"] = "orders" },
    });

    private static JsonObject Pod(string? injectLabel = null, string extra = "") => (JsonObject)JsonNode.Parse($$"""
        {
          "metadata": { "name": "orders-1"{{(injectLabel is null ? "" : $$""", "labels": { "plover/inject": "{{injectLabel}}" }""")}} },
          "spec": { "containers": [ { "name": "app", "image": "orders:1" }, { "name": "proxy", "image": "proxy:1" } ]{{extra}} }
        }
        """)!;

    /// <summary>Applies a JSON patch of "add" operations, as the API server does, so the paths are checked and not only the values.</summary>
    internal static JsonObject Apply(JsonObject pod, JsonArray patch)
    {
        var result = (JsonObject)pod.DeepClone();
        foreach (var operation in patch.Select(node => node!.AsObject()))
        {
            operation["op"]!.GetValue<string>().ShouldBe("add");
            var parts = operation["path"]!.GetValue<string>().Split('/', StringSplitOptions.RemoveEmptyEntries);
            JsonNode current = result;
            foreach (var part in parts[..^1])
            {
                current = current is JsonArray array ? array[int.Parse(part)]! : current[part] ?? throw new InvalidOperationException($"No {part} in the pod for {operation["path"]}.");
            }

            var value = operation["value"]!.DeepClone();
            if (current is JsonArray target)
            {
                parts[^1].ShouldBe("-");
                target.Add(value);
            }
            else
            {
                current.AsObject()[parts[^1]] = value;
            }
        }

        return result;
    }

    private static JsonArray Names(JsonNode? containers) => new([.. ((JsonArray)containers!).Select(c => (JsonNode?)JsonValue.Create(c!["name"]!.GetValue<string>()))]);

    [Fact]
    public void A_pod_of_an_opted_in_namespace_gets_a_native_sidecar_the_socket_volume_and_the_socket_in_each_container()
    {
        // Arrange
        var pod = Pod();

        // Act
        var injected = Apply(pod, Mutator().Mutate(pod, namespaceOptedIn: true));

        // Assert
        injected["spec"]!["initContainers"]![0]!["name"]!.GetValue<string>().ShouldBe("plover-sidecar");
        injected["spec"]!["initContainers"]![0]!["restartPolicy"]!.GetValue<string>().ShouldBe("Always");
        injected["spec"]!["volumes"]![0]!["name"]!.GetValue<string>().ShouldBe("plover-socket");
        foreach (var container in injected["spec"]!["containers"]!.AsArray())
        {
            container!["env"]![0]!["name"]!.GetValue<string>().ShouldBe("PLOVER_SOCKET");
            container["volumeMounts"]![0]!["mountPath"]!.GetValue<string>().ShouldBe("/var/run/plover");
        }

        Names(injected["spec"]!["containers"]).ToJsonString().ShouldBe("""["app","proxy"]""");
    }

    [Fact]
    public void The_sidecar_reports_to_the_agent_with_the_labels_it_was_given_and_runs_with_least_rights()
    {
        // Act
        var sidecar = Mutator().Sidecar();

        // Assert
        var entries = sidecar["env"]!.AsArray();
        var env = entries.Where(e => e!["value"] is not null).ToDictionary(e => e!["name"]!.GetValue<string>(), e => e!["value"]!.GetValue<string>());
        entries.Single(e => e!["name"]!.GetValue<string>() == "PLOVER_Sidecar__Pod__Node")!["valueFrom"]!["fieldRef"]!["fieldPath"]!.GetValue<string>().ShouldBe("spec.nodeName");
        env["PLOVER_Sidecar__Agent"].ShouldBe("plover-agent:9441");
        env["PLOVER_Sidecar__Library__Socket"].ShouldBe("/var/run/plover/plover.sock");
        env["PLOVER_Sidecar__Labels__team"].ShouldBe("orders");
        sidecar["securityContext"]!["allowPrivilegeEscalation"]!.GetValue<bool>().ShouldBeFalse();
        sidecar["securityContext"]!["readOnlyRootFilesystem"]!.GetValue<bool>().ShouldBeTrue();
        sidecar["image"]!.GetValue<string>().ShouldBe("plover-sidecar:1.2.3");
    }

    [Fact]
    public void Lists_the_pod_already_has_are_added_to_and_not_replaced()
    {
        // Arrange
        var pod = Pod(extra: """
            , "initContainers": [ { "name": "migrate", "image": "m:1" } ],
              "volumes": [ { "name": "data", "emptyDir": {} } ]
            """);
        pod["spec"]!["containers"]![0]!["env"] = JsonNode.Parse("""[ { "name": "A", "value": "1" } ]""");
        pod["spec"]!["containers"]![0]!["volumeMounts"] = JsonNode.Parse("""[ { "name": "data", "mountPath": "/data" } ]""");

        // Act
        var injected = Apply(pod, Mutator().Mutate(pod, true));

        // Assert
        Names(injected["spec"]!["initContainers"]).ToJsonString().ShouldBe("""["migrate","plover-sidecar"]""");
        Names(injected["spec"]!["volumes"]).ToJsonString().ShouldBe("""["data","plover-socket"]""");
        injected["spec"]!["containers"]![0]!["env"]!.AsArray().Count.ShouldBe(2);
        injected["spec"]!["containers"]![0]!["volumeMounts"]!.AsArray().Count.ShouldBe(2);
    }

    [Fact]
    public void Without_native_sidecars_the_sidecar_becomes_a_second_container_and_is_not_given_the_socket_of_itself_twice()
    {
        // Arrange
        var pod = Pod();

        // Act
        var injected = Apply(pod, Mutator(native: false).Mutate(pod, true));

        // Assert
        Names(injected["spec"]!["containers"]).ToJsonString().ShouldBe("""["app","proxy","plover-sidecar"]""");
        injected["spec"]!.AsObject().ContainsKey("initContainers").ShouldBeFalse();
        injected["spec"]!["containers"]![2]!.AsObject().ContainsKey("restartPolicy").ShouldBeFalse();
        injected["spec"]!["containers"]![2]!["volumeMounts"]!.AsArray().Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData("true", false, true)]
    [InlineData("true", true, true)]
    [InlineData("false", true, false)]
    [InlineData("false", false, false)]
    public void A_pod_is_changed_when_it_or_its_namespace_opts_in_and_the_pod_can_opt_out_again(string? podLabel, bool namespaceOptedIn, bool changed)
    {
        // Act
        var patch = Mutator().Mutate(Pod(podLabel), namespaceOptedIn);

        // Assert
        (patch.Count > 0).ShouldBe(changed);
    }

    [Fact]
    public void A_pod_that_already_has_the_sidecar_is_left_alone_so_two_registrations_do_not_add_it_twice()
    {
        // Arrange
        var pod = Pod();
        var once = Apply(pod, Mutator().Mutate(pod, true));

        // Act
        var again = Mutator().Mutate(once, true);

        // Assert
        again.Count.ShouldBe(0);
    }

    [Fact]
    public void A_pod_without_a_spec_is_refused_with_an_argument_exception()
    {
        // Act
        var mutate = () => Mutator().Mutate((JsonObject)JsonNode.Parse("""{"metadata":{}}""")!, true);

        // Assert
        mutate.ShouldThrow<ArgumentException>();
    }

    private static JsonObject Review(object? pod, string uid = "abc-123") =>
        (JsonObject)JsonNode.Parse(JsonSerializer.Serialize(new Dictionary<string, object?> { ["request"] = new Dictionary<string, object?> { ["uid"] = uid, ["object"] = pod } }))!;

    [Fact]
    public void An_admission_review_is_answered_with_a_base64_patch_for_the_uid_of_the_request()
    {
        // Arrange
        var review = Review(JsonSerializer.Deserialize<JsonElement>(Pod().ToJsonString()));

        // Act
        var answer = (JsonObject)JsonNode.Parse(AdmissionEndpoints.Answer(review, Mutator(), fromNamespace: true, NullLogger.Instance))!;

        // Assert
        answer["apiVersion"]!.GetValue<string>().ShouldBe("admission.k8s.io/v1");
        answer["kind"]!.GetValue<string>().ShouldBe("AdmissionReview");
        var response = answer["response"]!;
        response["uid"]!.GetValue<string>().ShouldBe("abc-123");
        response["allowed"]!.GetValue<bool>().ShouldBeTrue();
        response["patchType"]!.GetValue<string>().ShouldBe("JSONPatch");
        var patch = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(response["patch"]!.GetValue<string>())))!.AsArray();
        patch.Count.ShouldBeGreaterThan(2);
    }

    [Theory]
    [InlineData(false)]
    public void A_pod_that_is_not_to_change_is_allowed_without_a_patch(bool fromNamespace)
    {
        // Arrange
        var review = Review(JsonSerializer.Deserialize<JsonElement>(Pod().ToJsonString()));

        // Act
        var answer = (JsonObject)JsonNode.Parse(AdmissionEndpoints.Answer(review, Mutator(), fromNamespace, NullLogger.Instance))!;

        // Assert
        answer["response"]!["allowed"]!.GetValue<bool>().ShouldBeTrue();
        answer["response"]!.AsObject().ContainsKey("patch").ShouldBeFalse();
    }

    [Fact]
    public void Something_that_is_not_a_pod_never_stops_one_from_starting()
    {
        // Act
        var answer = (JsonObject)JsonNode.Parse(AdmissionEndpoints.Answer(Review(new { spec = 5 }), Mutator(), true, NullLogger.Instance))!;

        // Assert
        answer["response"]!["allowed"]!.GetValue<bool>().ShouldBeTrue();
        answer["response"]!.AsObject().ContainsKey("patch").ShouldBeFalse();
    }
}
