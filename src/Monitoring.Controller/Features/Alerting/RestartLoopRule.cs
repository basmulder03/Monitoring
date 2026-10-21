using Monitoring.Controller.Features.Registry;
using Monitoring.Protocol.Instances;

namespace Monitoring.Controller.Features.Alerting;

/// <summary>Fires for an instance whose containers restarted several times within a short window.</summary>
/// <remarks>
/// A restart count only grows, so seeing a loop means watching it change. The rule keeps a few recent samples per instance for that,
/// which is rebuilt within one window after a restart of the controller; nothing is persisted.
/// </remarks>
/// <param name="registry">Source of the instances.</param>
/// <param name="options">Provides how many restarts in which window count as a loop.</param>
/// <param name="time">Clock for the samples.</param>
internal sealed class RestartLoopRule(IScopeRegistry registry, AlertingOptions options, TimeProvider time) : IAlertRule
{
    private readonly Dictionary<string, Queue<(DateTimeOffset At, int Restarts)>> _samples = [];

    /// <inheritdoc />
    public string Name => "restart-loop";

    /// <inheritdoc />
    public AlertSeverity Severity => AlertSeverity.Warning;

    /// <inheritdoc />
    public TimeSpan Wait => TimeSpan.Zero;

    /// <inheritdoc />
    public IEnumerable<string> FindFailingSubjects()
    {
        var now = time.GetUtcNow();
        var failing = new List<string>();
        var seen = new HashSet<string>();

        foreach (var scope in registry.GetScopeNames())
        {
            foreach (var instance in registry.GetInstances(scope).Where(i => i.Lifecycle == InstanceLifecycle.Running && i.Containers is not null))
            {
                var subject = $"{scope}/{instance.ServiceName}/{instance.Id}";
                seen.Add(subject);

                var restarts = instance.Containers!.Where(c => c.Role != ContainerRole.Init).Sum(c => c.Restarts);
                if (Observe(subject, now, restarts) >= options.RestartLoopCount)
                {
                    failing.Add(subject);
                }
            }
        }

        // Forget instances that are gone, so the samples never outgrow the instances.
        foreach (var subject in _samples.Keys.Where(subject => !seen.Contains(subject)).ToList())
        {
            _samples.Remove(subject);
        }

        return failing;
    }

    /// <summary>Records the restarts now and returns how many happened within the window.</summary>
    private int Observe(string subject, DateTimeOffset now, int restarts)
    {
        if (!_samples.TryGetValue(subject, out var samples))
        {
            _samples[subject] = samples = new Queue<(DateTimeOffset, int)>();
        }

        samples.Enqueue((now, restarts));

        // Keep one sample from before the window as its starting point.
        while (samples.Count > 1 && now - samples.ElementAt(1).At >= options.RestartLoopWindow)
        {
            samples.Dequeue();
        }

        return restarts - samples.Peek().Restarts;
    }
}
