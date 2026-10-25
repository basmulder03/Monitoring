using Plover.Agent.Features.Discovery.Kubernetes.Models;

namespace Plover.Agent.Tests.Shared;

/// <summary>Builds a Kubernetes pod for tests, starting from a plain running pod and changing only what a test cares about.</summary>
internal sealed class PodBuilder
{
    private readonly Dictionary<string, string> _labels = [];
    private readonly List<OwnerReference> _owners = [];
    private readonly List<ContainerStatus> _containers = [];
    private readonly List<ContainerStatus> _initContainers = [];
    private readonly List<ContainerSpec> _specs = [];
    private readonly List<ContainerSpec> _initSpecs = [];
    private string _name = "pod-1";
    private string _phase = "Running";
    private string? _node;
    private string? _ip;
    private string? _resourceVersion;
    private string? _deletion;
    private bool _ready;

    /// <summary>Sets the pod name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder Named(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>Sets the Kubernetes phase.</summary>
    /// <param name="phase">Pending, Running, Succeeded, Failed or Unknown.</param>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder InPhase(string phase)
    {
        _phase = phase;
        return this;
    }

    /// <summary>Adds a label.</summary>
    /// <param name="key">Label name.</param>
    /// <param name="value">Label value.</param>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder WithLabel(string key, string value)
    {
        _labels[key] = value;
        return this;
    }

    /// <summary>Adds an owner.</summary>
    /// <param name="kind">Kind of the owner.</param>
    /// <param name="name">Name of the owner.</param>
    /// <param name="controller">Whether it is the managing controller.</param>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder OwnedBy(string kind, string name, bool controller = true)
    {
        _owners.Add(new OwnerReference(kind, name, controller));
        return this;
    }

    /// <summary>Marks the pod as terminating.</summary>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder Deleting()
    {
        _deletion = "2026-10-21T10:00:00Z";
        return this;
    }

    /// <summary>Sets the Ready condition to true.</summary>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder PodReady()
    {
        _ready = true;
        return this;
    }

    /// <summary>Sets the node the pod runs on.</summary>
    /// <param name="node">Node name.</param>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder OnNode(string node)
    {
        _node = node;
        return this;
    }

    /// <summary>Sets the pod address.</summary>
    /// <param name="ip">IP address.</param>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder WithIp(string ip)
    {
        _ip = ip;
        return this;
    }

    /// <summary>Sets the resource version.</summary>
    /// <param name="version">The version.</param>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder AtVersion(string version)
    {
        _resourceVersion = version;
        return this;
    }

    /// <summary>Adds a container status.</summary>
    /// <param name="name">Container name.</param>
    /// <param name="ready">Whether it is ready.</param>
    /// <param name="waitingReason">Set when the container is waiting, with this reason.</param>
    /// <param name="terminatedExitCode">Set when the container has exited, with this exit code.</param>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder WithContainer(string name, bool ready, string? waitingReason = null, int? terminatedExitCode = null)
    {
        ContainerState? state = waitingReason is not null
            ? new ContainerState(Waiting: new ContainerStateWaiting(waitingReason))
            : terminatedExitCode is { } code ? new ContainerState(Terminated: new ContainerStateTerminated(code)) : null;
        _containers.Add(new ContainerStatus(name, ready, 0, state));
        return this;
    }

    /// <summary>Adds a container to the spec with what it asks for and may use.</summary>
    /// <param name="name">Container name.</param>
    /// <param name="requests">Requests by resource, such as <c>cpu</c>.</param>
    /// <param name="limits">Limits by resource.</param>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder WithResources(string name, Dictionary<string, string>? requests = null, Dictionary<string, string>? limits = null)
    {
        _specs.Add(new ContainerSpec(name, $"{name}:1", Resources: new ResourceRequirements(requests, limits)));
        return this;
    }

    /// <summary>Adds a running container, with its spec, status and optionally what ended its previous run.</summary>
    /// <param name="name">Container name.</param>
    /// <param name="ready">Whether it is ready.</param>
    /// <param name="restarts">How often it restarted.</param>
    /// <param name="lastReason">Why the previous run ended, such as <c>OOMKilled</c>.</param>
    /// <param name="lastExitCode">Exit code of the previous run.</param>
    /// <param name="digest">The image digest reported for the container.</param>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder WithRunningContainer(string name, bool ready, int restarts = 0, string? lastReason = null, int lastExitCode = 0, string? digest = null)
    {
        _specs.Add(new ContainerSpec(name, $"{name}:1"));
        _containers.Add(new ContainerStatus(
            name,
            ready,
            restarts,
            new ContainerState(Running: new ContainerStateRunning("2026-10-21T10:00:00Z")),
            lastReason is null ? null : new ContainerState(Terminated: new ContainerStateTerminated(lastExitCode, lastReason)),
            $"{name}:1",
            digest));
        return this;
    }

    /// <summary>Adds a container that waits, with its spec.</summary>
    /// <param name="name">Container name.</param>
    /// <param name="reason">Why it waits.</param>
    /// <param name="message">Details about the reason.</param>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder WithWaitingContainer(string name, string reason, string? message = null)
    {
        _specs.Add(new ContainerSpec(name, $"{name}:1"));
        _containers.Add(new ContainerStatus(name, false, 0, new ContainerState(Waiting: new ContainerStateWaiting(reason, message))));
        return this;
    }

    /// <summary>Adds an init container, with its spec and status.</summary>
    /// <param name="name">Container name.</param>
    /// <param name="terminatedExitCode">Set when it has exited, with this exit code; running when not set.</param>
    /// <param name="nativeSidecar">Whether it is a native sidecar, which keeps running next to the application.</param>
    /// <returns>This builder, for chaining.</returns>
    public PodBuilder WithInitContainer(string name, int? terminatedExitCode = null, bool nativeSidecar = false)
    {
        _initSpecs.Add(new ContainerSpec(name, $"{name}:1", nativeSidecar ? "Always" : null));
        _initContainers.Add(new ContainerStatus(
            name,
            !nativeSidecar && terminatedExitCode is not null,
            0,
            terminatedExitCode is { } code
                ? new ContainerState(Terminated: new ContainerStateTerminated(code))
                : new ContainerState(Running: new ContainerStateRunning())));
        return this;
    }

    /// <summary>Creates the pod.</summary>
    /// <returns>The pod.</returns>
    public Pod Build() => new(
        new PodMetadata(
            _name,
            Uid: $"uid-{_name}",
            ResourceVersion: _resourceVersion,
            Labels: _labels.Count > 0 ? new Dictionary<string, string>(_labels) : null,
            OwnerReferences: _owners.Count > 0 ? [.. _owners] : null,
            DeletionTimestamp: _deletion),
        new PodSpec(_node, _specs.Count > 0 ? [.. _specs] : [new ContainerSpec("app", "image:1")], _initSpecs.Count > 0 ? [.. _initSpecs] : null),
        new PodStatus(
            _phase,
            _ip,
            _ready ? [new PodCondition("Ready", "True")] : [new PodCondition("Ready", "False")],
            _containers.Count > 0 ? [.. _containers] : null,
            _initContainers.Count > 0 ? [.. _initContainers] : null));
}
