using Monitoring.Common;
using Microsoft.Extensions.Logging;
using Monitoring.Protocol.Messages;

namespace Monitoring.Agent.Features.ControllerLink;

/// <summary>
/// Default <see cref="IControllerLink"/>: one connection per controller replica. It starts with the bootstrap
/// addresses and follows the replica list the controller sends in <see cref="Config"/>.
/// </summary>
/// <param name="options">The bootstrap addresses.</param>
/// <param name="runReplica">Keeps one replica connected.</param>
/// <param name="logger">Receives address problems.</param>
internal sealed class ControllerLinkService(
    ControllerLinkOptions options,
    RunReplicaAsync runReplica,
    ILogger<ControllerLinkService> logger) : IControllerLink
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Running> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _stopped = [];
    private CancellationToken _lifetime;

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            _lifetime = ct;
            foreach (var address in options.ControllerAddresses)
            {
                Start(address);
            }
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested; every connection saw the same token.
        }

        Task[] everything;
        lock (_gate)
        {
            everything = [.. _running.Values.Select(r => r.Task), .. _stopped];
        }

        await Task.WhenAll(everything).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies the replica list a controller sent: connects to replicas not yet connected and drops the ones
    /// no longer listed, such as the bootstrap address once the individual addresses are known.
    /// </summary>
    private void OnConfig(Config config)
    {
        // An empty list says nothing; keep what is connected.
        if (config.ControllerReplicas.Count == 0)
        {
            return;
        }

        var desired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in config.ControllerReplicas)
        {
            if (HostPort.TryParse(address, out _, out _))
            {
                desired.Add(address.Trim());
            }
            else
            {
                logger.LogWarning("Ignoring controller replica address '{Address}': it is not host:port", address);
            }
        }

        if (desired.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_lifetime.IsCancellationRequested)
            {
                return;
            }

            foreach (var address in desired.Where(a => !_running.ContainsKey(a)))
            {
                Start(address);
            }

            foreach (var address in _running.Keys.Where(a => !desired.Contains(a)).ToList())
            {
                var running = _running[address];
                _running.Remove(address);
                running.Cancel.Cancel();
                _stopped.Add(running.Task);
            }
        }
    }

    /// <summary>Starts a connection; the caller holds the lock.</summary>
    private void Start(string address)
    {
        var key = address.Trim();
        if (_running.ContainsKey(key))
        {
            return;
        }

        var cancel = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        var task = Task.Run(() => runReplica(key, OnConfig, cancel.Token), CancellationToken.None);
        _running[key] = new Running(cancel, task);
    }

    /// <summary>A connection loop and the means to stop it.</summary>
    private sealed record Running(CancellationTokenSource Cancel, Task Task);
}
