namespace Plover.Client;

/// <summary>What a source answers to a pull.</summary>
/// <param name="Payload">The data as JSON; <see langword="null"/> when there is nothing to say.</param>
/// <param name="Cursor">The position to pass in the next pull, so that nothing is sent twice.</param>
internal readonly record struct PullData(string? Payload, long Cursor);
