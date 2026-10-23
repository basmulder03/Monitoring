namespace Monitoring.Controller.Features.StateStore;

/// <summary>
/// Where the controller keeps what it would otherwise lose when it restarts, as ordered logs of small JSON values. Almost everything the controller shows is rebuilt from what the agents send,
/// so the store holds only what no agent can tell it again, such as the history of changes. The default keeps it in memory, which loses it on a restart; SQLite keeps it on a volume.
/// </summary>
public interface IStateStore
{
    /// <summary>Reads a log, oldest first.</summary>
    /// <param name="log">The name of the log.</param>
    /// <returns>The values.</returns>
    IReadOnlyList<string> Read(string log);

    /// <summary>Adds a value to the end of a log and drops the oldest ones beyond a number.</summary>
    /// <param name="log">The name of the log.</param>
    /// <param name="json">The value.</param>
    /// <param name="keep">How many values the log keeps.</param>
    void Append(string log, string json, int keep);
}
