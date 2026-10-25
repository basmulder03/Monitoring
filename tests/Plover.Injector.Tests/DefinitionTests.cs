using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Plover.Injector.Features.Mutation;
using Shouldly;
using Xunit;

namespace Plover.Injector.Tests;

/// <summary>
/// The sidecar the injector adds and the sidecar the Helm template includes are meant to be the same, so that a pod does not depend on how it got its sidecar. The golden file
/// holds the definition once: this test makes the injector produce it, and <c>deploy/injector/check-definition.py</c> makes the Helm template produce it too.
/// </summary>
public class DefinitionTests
{
    private static string GoldenPath([CallerFilePath] string file = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "..", "..", "deploy", "injector", "sidecar.golden.json"));

    /// <summary>What the golden file is made from; the check of the template uses the same values.</summary>
    private static PodMutator Mutator() => new(new InjectorOptions
    {
        SidecarImage = "plover-sidecar:1.0.0",
        AgentAddress = "plover-agent:9441",
        Labels = { ["team"] = "orders" },
    });

    private static string Definition()
    {
        var mutator = Mutator();
        var pod = (JsonObject)JsonNode.Parse("""{"metadata":{},"spec":{"containers":[{"name":"app","image":"orders:1"}]}}""")!;
        var patch = mutator.Mutate(pod, namespaceOptedIn: true);
        var injected = PodMutatorTests.Apply(pod, patch);
        var definition = new JsonObject
        {
            ["sidecar"] = injected["spec"]!["initContainers"]![0]!.DeepClone(),
            ["volume"] = injected["spec"]!["volumes"]![0]!.DeepClone(),
            ["appEnv"] = injected["spec"]!["containers"]![0]!["env"]![0]!.DeepClone(),
            ["appMount"] = injected["spec"]!["containers"]![0]!["volumeMounts"]![0]!.DeepClone(),
        };
        return definition.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }

    [Fact]
    public async Task The_injector_makes_the_definition_in_the_golden_file()
    {
        // Arrange
        var actual = Definition();
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            await File.WriteAllTextAsync(GoldenPath(), actual, TestContext.Current.CancellationToken);
        }

        // Act
        var golden = (await File.ReadAllTextAsync(GoldenPath(), TestContext.Current.CancellationToken)).ReplaceLineEndings("\n");

        // Assert
        actual.ShouldBe(golden, "Run with UPDATE_GOLDEN=1 to write the golden file, and make the Helm template match it (deploy/injector/check-definition.py).");
    }
}
