using Monitoring.Agent.Features.Discovery.Kubernetes;
using Shouldly;
using Xunit;

namespace Monitoring.Agent.Tests.Features.Discovery.Kubernetes;

public class QuantityTests
{
    [Theory]
    [InlineData("250m", 0.25)]
    [InlineData("1", 1)]
    [InlineData("1.5", 1.5)]
    [InlineData("128Mi", 134217728)]
    [InlineData("1Gi", 1073741824)]
    [InlineData("2G", 2e9)]
    [InlineData("100k", 1e5)]
    [InlineData("3000000n", 0.003)]
    [InlineData("1e3", 1000)]
    public void Parse_reads_the_quantities_kubernetes_writes(string text, double expected)
    {
        Quantity.Parse(text)!.Value.ShouldBe(expected, tolerance: 1e-9);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lots")]
    public void Parse_gives_nothing_for_what_is_not_a_quantity(string? text)
    {
        Quantity.Parse(text).ShouldBeNull();
    }
}
