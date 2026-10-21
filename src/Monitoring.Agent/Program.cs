using Monitoring.Agent.Hosting;
using Monitoring.Hosting;

return await MonitoringHost.RunAsync(args, AgentComposition.Compose);
