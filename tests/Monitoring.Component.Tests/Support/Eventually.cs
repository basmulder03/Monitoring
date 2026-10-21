using Xunit;

namespace Monitoring.Component.Tests.Support;

/// <summary>Retries a check until it passes or time runs out, for state that arrives asynchronously.</summary>
internal static class Eventually
{
    /// <summary>Runs the check again and again until it passes.</summary>
    /// <param name="assertion">
    /// The check. Any exception counts as "not yet", because reading data that has not arrived yet fails in many ways
    /// (a failed assertion, a missing item, an empty list).
    /// </param>
    /// <returns>A task that completes once the check passed.</returns>
    /// <exception cref="Exception">The last failure of the check, once ten seconds have passed.</exception>
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
            catch (Exception ex) when (ex is not OperationCanceledException && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25, TestContext.Current.CancellationToken);
            }
        }
    }
}
