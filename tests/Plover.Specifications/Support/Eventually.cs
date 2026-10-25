namespace Plover.Specifications.Support;

/// <summary>Waits for something that happens by itself, which is how a monitoring system works: nothing is instant, everything has a deadline.</summary>
public static class Eventually
{
    /// <summary>Checks until the condition holds or the time is up.</summary>
    /// <param name="what">What was waited for, for the message.</param>
    /// <param name="within">How long it may take.</param>
    /// <param name="holds">Returns <see langword="true"/> when it has happened; may throw while it has not.</param>
    /// <returns>A task that completes when it holds.</returns>
    /// <exception cref="TimeoutException">It did not happen in time.</exception>
    public static async Task HoldsAsync(string what, TimeSpan within, Func<Task<bool>> holds)
    {
        var deadline = DateTime.UtcNow + within;
        while (true)
        {
            try
            {
                if (await holds())
                {
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
            {
                // Not there yet.
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Within {within.TotalSeconds:0} seconds: {what}");
            }

            await Task.Delay(500);
        }
    }
}
