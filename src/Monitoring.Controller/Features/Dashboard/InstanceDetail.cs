using Monitoring.Protocol.Health;
using Monitoring.Protocol.Instances;

namespace Monitoring.Controller.Features.Dashboard;

/// <summary>One instance (a pod or a process) of a service.</summary>
/// <param name="Id">Id of the instance.</param>
/// <param name="Lifecycle">Where the instance is in its lifetime; Gone instances are kept briefly so the page can show what disappeared.</param>
/// <param name="Health">Health of the instance.</param>
/// <param name="Labels">Labels of the instance, such as its node.</param>
public sealed record InstanceDetail(string Id, InstanceLifecycle Lifecycle, HealthState Health, IReadOnlyDictionary<string, string> Labels);
