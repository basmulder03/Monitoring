using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Monitoring.Agent.Features.ScopeState;
using Monitoring.Protocol.Instances;

namespace Monitoring.Agent.Features.Discovery.Windows;

/// <summary>
/// Keeps the scope state in step with the Windows services on an allowlist: asks the Service Control Manager at a fixed interval, shows each listed
/// service as an instance, and counts how often a service came back after it was not running.
/// </summary>
/// <param name="manager">The Service Control Manager.</param>
/// <param name="store">The scope state to keep current.</param>
/// <param name="options">The allowlist and the interval.</param>
/// <param name="time">Clock for the interval.</param>
/// <param name="logger">Receives failures.</param>
/// <param name="probe">Measures the machine and the processes of the services; optional, without it only the state of the services is shown.</param>
internal sealed class WindowsDiscoveryService(
    IServiceControlManager manager, IScopeStateStore store, WindowsDiscoveryOptions options, TimeProvider time, ILogger<WindowsDiscoveryService> logger, IHostProbe? probe = null)
    : BackgroundService, IWindowsProcessMap
{
    private readonly Dictionary<string, InstanceInfo> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (WindowsServiceState Last, int Restarts)> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ProcessSample?> _processes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private Dictionary<int, string> _byProcess = [];
    private PeriodicTimer? _timer;
    private DateTimeOffset _measuredAt = DateTimeOffset.MinValue;
    private InstanceInfo? _host;

    /// <inheritdoc />
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Created here, so the interval counts from the start of the service.
        _timer = new PeriodicTimer(options.PollInterval, time);
        return base.StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    public string? InstanceOfProcess(int processId)
    {
        lock (_gate)
        {
            return _byProcess.GetValueOrDefault(processId);
        }
    }

    /// <summary>Asks the Service Control Manager once and makes the scope state match; a failure leaves it as it was.</summary>
    public void Poll()
    {
        IReadOnlyList<WindowsServiceStatus> services;
        try
        {
            services = manager.List();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning("Cannot read the Windows services: {Reason}", ex.Message);
            return;
        }

        var listed = services.Where(Listed).ToList();
        Measure(listed);
        lock (_gate)
        {
            _byProcess = listed.Where(service => service.ProcessId is not null && service.State == WindowsServiceState.Running)
                .GroupBy(service => service.ProcessId!.Value).ToDictionary(group => group.Key, group => group.First().Name);
        }

        var current = new Dictionary<string, InstanceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in listed)
        {
            current[service.Name] = WindowsServiceMapper.Map(service, CountRestarts(service), _processes.GetValueOrDefault(service.Name));
        }

        // The machine itself, which is not a service and so is not on the allowlist.
        if (_host is not null)
        {
            current[_host.Id] = _host;
        }

        foreach (var instance in current.Values)
        {
            Upsert(instance);
        }

        foreach (var name in _known.Keys.Where(name => !current.ContainsKey(name)).ToList())
        {
            _known.Remove(name);
            _seen.Remove(name);
            store.Remove(name);
        }

        store.Publish();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            do
            {
                Poll();
            }
            while (await _timer!.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
    }

    /// <summary>Measures the machine and the processes of the running listed services, when it is time to.</summary>
    private void Measure(List<WindowsServiceStatus> listed)
    {
        var now = time.GetUtcNow();
        if (probe is null || now - _measuredAt < options.MeasureInterval)
        {
            return;
        }

        _measuredAt = now;
        try
        {
            _host = HostMapper.Map(probe.ReadHost());
            _processes.Clear();
            foreach (var service in listed.Where(service => service.ProcessId is not null))
            {
                _processes[service.Name] = probe.ReadProcess(service.ProcessId!.Value);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning("Cannot measure the machine: {Reason}", ex.Message);
        }
    }

    private bool Listed(WindowsServiceStatus service) => options.Services.Any(pattern =>
        pattern.EndsWith('*')
            ? service.Name.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : service.Name.Equals(pattern, StringComparison.OrdinalIgnoreCase));

    /// <summary>A service that is Running after it was seen in another state has restarted; the first sighting counts for nothing.</summary>
    private int CountRestarts(WindowsServiceStatus service)
    {
        var restarts = 0;
        if (_seen.TryGetValue(service.Name, out var before))
        {
            restarts = before.Restarts + (before.Last != WindowsServiceState.Running && service.State == WindowsServiceState.Running ? 1 : 0);
        }

        _seen[service.Name] = (service.State, restarts);
        return restarts;
    }

    private void Upsert(InstanceInfo instance)
    {
        if (_known.TryGetValue(instance.Id, out var existing) && Same(existing, instance))
        {
            return;
        }

        _known[instance.Id] = instance;
        store.Upsert(instance);
    }

    private static bool Same(InstanceInfo a, InstanceInfo b) =>
        a.Lifecycle == b.Lifecycle
        && a.Health == b.Health
        && a.Labels.Count == b.Labels.Count
        && a.Labels.All(label => b.Labels.TryGetValue(label.Key, out var value) && value == label.Value)
        && (a.Checks ?? []).SequenceEqual(b.Checks ?? [])
        && (a.Metrics ?? []).Select(m => m.Value).SequenceEqual((b.Metrics ?? []).Select(m => m.Value));
}
