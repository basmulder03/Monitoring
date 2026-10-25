using System.Diagnostics;
using Plover.Client.Features.Tracing;
using Plover.Tests.Shared;
using Shouldly;
using Xunit;

namespace Plover.Client.Tests.Budgets;

/// <summary>
/// What the library adds to every call an application makes to a dependency: the time and the allocations of counting it. The limits are well above what was measured, so a shared runner's noise
/// does not fail the build but a change that makes every call allocate or take microseconds longer does.
/// </summary>
[Collection("Meters")]
public class OverheadBudgetTests(ITestOutputHelper output)
{
    private const int Calls = 200_000;

    [Fact]
    public void Counting_a_call_to_a_known_dependency_allocates_nothing_and_takes_well_under_five_microseconds()
    {
        // Arrange
        var edges = new EdgeCounters(500);
        edges.Record("HTTP", "http://service.shop.svc:8080", 12, error: false);
        for (var warmUp = 0; warmUp < 10_000; warmUp++)
        {
            edges.Record("HTTP", "http://service.shop.svc:8080", 12, error: false);
        }

        // Act
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var clock = Stopwatch.StartNew();
        for (var call = 0; call < Calls; call++)
        {
            edges.Record("HTTP", "http://service.shop.svc:8080", 12.5, error: call % 50 == 0);
        }

        clock.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

        // Assert
        var microseconds = clock.Elapsed.TotalMicroseconds / Calls;
        output.WriteLine($"{microseconds:F3} microseconds and {allocated / (double)Calls:F2} bytes per counted call");
        PerfResults.Record("client.count.time", microseconds, "us");
        PerfResults.Record("client.count.allocated", allocated / (double)Calls, "B");
        allocated.ShouldBeLessThan(Calls, "counting a call must not allocate more than a byte per call on average");
        microseconds.ShouldBeLessThan(5);
    }
}
