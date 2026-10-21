using Shouldly;
using Xunit;

namespace Monitoring.Component.Tests.Support;

/// <summary>Retries an assertion until it holds or time runs out, for state that arrives asynchronously.</summary>
internal static class Eventually
{
    public static async Task AssertAsync(Action assertion)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            try
            {
                assertion();
                return;
            }
            catch (ShouldAssertException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(25, TestContext.Current.CancellationToken);
            }
        }
    }
}
