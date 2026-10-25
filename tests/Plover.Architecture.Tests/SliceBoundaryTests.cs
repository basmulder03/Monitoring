using System.Reflection;
using Plover.Agent.Features.ScopeState;
using Plover.Client.Features.Health;
using Plover.Controller.Features.Registry;
using Plover.Sidecar.Features.Probing;
using Shouldly;
using Xunit;

namespace Plover.Architecture.Tests;

/// <summary>The rules of the code structure in docs/design.md: slices only meet through public contracts, and shared code knows no slice.</summary>
public class SliceBoundaryTests
{
    public static TheoryData<string> Executables =>
        [nameof(Agent), nameof(Client), nameof(Controller), nameof(Sidecar)];

    [Theory]
    [MemberData(nameof(Executables))]
    public void A_slice_uses_another_slice_only_through_its_public_types(string name)
    {
        // Arrange
        var assembly = AssemblyOf(name);
        var violations = new List<string>();

        // Act
        foreach (var type in assembly.GetTypes().Where(IsInSlice))
        {
            foreach (var used in TypeReferences.Of(type).Where(t => t.Assembly == assembly && IsInSlice(t)))
            {
                if (SliceOf(used) != SliceOf(type) && !IsPublic(used))
                {
                    violations.Add($"{type.FullName} uses the internal {used.FullName}");
                }
            }
        }

        // Assert
        violations.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Executables))]
    public void Shared_code_does_not_depend_on_a_slice(string name)
    {
        // Arrange
        var assembly = AssemblyOf(name);
        var violations = new List<string>();

        // Act
        foreach (var type in assembly.GetTypes().Where(t => !IsInSlice(t) && !IsGenerated(t)))
        {
            foreach (var used in TypeReferences.Of(type).Where(t => t.Assembly == assembly && IsInSlice(t)))
            {
                violations.Add($"{type.FullName} depends on {used.FullName}");
            }
        }

        // Assert: composition (Program, Hosting, and the client's one extension class) is the one place allowed to know every slice
        violations
            .Where(v => !v.StartsWith($"Plover.{name}.Hosting.", StringComparison.Ordinal))
            .Where(v => !v.Contains(".Program ") && !v.Contains(".PloverExtensions "))
            .ShouldBeEmpty();
    }

    private static Assembly AssemblyOf(string name) => name switch
    {
        nameof(Agent) => typeof(IScopeStateStore).Assembly,
        nameof(Client) => typeof(PloverTags).Assembly,
        nameof(Controller) => typeof(IScopeRegistry).Assembly,
        nameof(Sidecar) => typeof(IProbeResults).Assembly,
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    private static bool IsInSlice(Type type) => SliceOf(type) is not null;

    private static bool IsGenerated(Type type) => type.Name.StartsWith('<') || type.Namespace is null;

    /// <summary>The slice a type belongs to: the folder below <c>Features</c>; null for shared and composition code.</summary>
    private static string? SliceOf(Type type)
    {
        var parts = (type.Namespace ?? "").Split('.');
        var features = Array.IndexOf(parts, "Features");
        return features >= 0 && features + 1 < parts.Length ? parts[features + 1] : null;
    }

    private static bool IsPublic(Type type) => type.IsPublic || (type.IsNestedPublic && IsPublic(type.DeclaringType!));
}
