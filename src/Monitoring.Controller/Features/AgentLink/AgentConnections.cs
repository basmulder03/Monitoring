using System.Collections.Concurrent;
using System.Threading.Channels;
using Monitoring.Protocol.Messages;
using Monitoring.Protocol.Versioning;

namespace Monitoring.Controller.Features.AgentLink;

/// <summary>
/// Knows the live agent connections of this replica and carries requests to them. A request is written by the loop that serves the connection,
/// which stays the only writer, and is matched with the answer by its number.
/// </summary>
/// <param name="options">Provides how long an agent may take to answer.</param>
internal sealed class AgentConnections(AgentLinkOptions options) : IAgentRequests
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<AgentConnection>> _byScope = new(StringComparer.Ordinal);
    private long _lastRequest;

    /// <summary>Registers a connection that was accepted.</summary>
    /// <param name="scopeName">The scope the agent reports for.</param>
    /// <param name="protocolVersion">The version the agent announced, which says what it understands.</param>
    /// <returns>The connection, to be passed to <see cref="Remove"/> when it ends.</returns>
    public AgentConnection Add(string scopeName, int protocolVersion)
    {
        var connection = new AgentConnection(scopeName, protocolVersion);
        lock (_gate)
        {
            if (!_byScope.TryGetValue(scopeName, out var list))
            {
                _byScope[scopeName] = list = [];
            }

            list.Add(connection);
        }

        return connection;
    }

    /// <summary>Forgets a connection that ended and lets every request still waiting on it give up.</summary>
    /// <param name="connection">The connection.</param>
    public void Remove(AgentConnection connection)
    {
        lock (_gate)
        {
            if (_byScope.TryGetValue(connection.ScopeName, out var list))
            {
                list.Remove(connection);
            }
        }

        connection.Fail();
    }

    /// <inheritdoc />
    public async Task<string?> GetMetricHistoryAsync(string scopeName, string instanceId, CancellationToken ct)
    {
        AgentConnection? connection;
        lock (_gate)
        {
            connection = _byScope.GetValueOrDefault(scopeName)?.LastOrDefault(c => c.ProtocolVersion >= ProtocolVersion.HistoryRequests);
        }

        if (connection is null)
        {
            return null;
        }

        var id = Interlocked.Increment(ref _lastRequest);
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Pending[id] = answer;
        try
        {
            if (!connection.Outgoing.Writer.TryWrite(new HistoryRequest(id, instanceId)))
            {
                return null;
            }

            return await answer.Task.WaitAsync(options.RequestTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            connection.Pending.TryRemove(id, out _);
        }
    }
}

/// <summary>One live agent connection: what it understands, what is to be sent to it and what is waiting for its answer.</summary>
/// <param name="ScopeName">The scope the agent reports for.</param>
/// <param name="ProtocolVersion">The version the agent announced.</param>
internal sealed record AgentConnection(string ScopeName, int ProtocolVersion)
{
    /// <summary>Gets the requests the serving loop is to write to the agent.</summary>
    public Channel<Message> Outgoing { get; } = Channel.CreateBounded<Message>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropWrite });

    /// <summary>Gets the requests waiting for an answer, by number.</summary>
    public ConcurrentDictionary<long, TaskCompletionSource<string?>> Pending { get; } = new();

    /// <summary>Hands an answer to the request waiting for it; an answer nobody waits for any more is ignored.</summary>
    /// <param name="result">The answer.</param>
    public void Complete(HistoryResult result)
    {
        if (Pending.TryGetValue(result.RequestId, out var answer))
        {
            answer.TrySetResult(result.PayloadJson);
        }
    }

    /// <summary>Ends the wait of every request, because the connection is gone.</summary>
    public void Fail()
    {
        foreach (var answer in Pending.Values)
        {
            answer.TrySetResult(null);
        }
    }
}
