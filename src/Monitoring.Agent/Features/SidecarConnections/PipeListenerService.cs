using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Monitoring.Agent.Features.Discovery;
using Monitoring.Protocol.Messages;

namespace Monitoring.Agent.Features.SidecarConnections;

/// <summary>
/// Listens on a named pipe for the applications of the machine, which speak the same protocol as a sidecar does, and serves them with the same handler.
/// On Windows the pipe is limited to the accounts that run monitored applications. The application is matched with its service by process id.
/// </summary>
/// <param name="handler">Serves each connection.</param>
/// <param name="options">The name of the pipe and who may use it.</param>
/// <param name="processes">Matches a process with the service that runs in it; without it an application is taken at its word.</param>
/// <param name="logger">Receives faults.</param>
internal sealed class PipeListenerService(
    SidecarConnectionHandler handler, SidecarConnectionsOptions options, ILogger<PipeListenerService> logger, IWindowsProcessMap? processes = null) : BackgroundService
{
    /// <summary>The label in which an application tells its process id.</summary>
    public const string ProcessIdLabel = "process.id";

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connections = new List<Task>();
        try
        {
            logger.LogInformation("Listening for applications on the pipe '{Pipe}'", options.PipeName);
            while (!stoppingToken.IsCancellationRequested)
            {
                var server = CreateServer(options.PipeName!, options.PipeAllow);
                try
                {
                    await server.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                }
                catch
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                connections.RemoveAll(task => task.IsCompleted);
                connections.Add(ServeAsync(server, stoppingToken));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown requested.
        }
        finally
        {
            await Task.WhenAll(connections).ConfigureAwait(false);
        }
    }

    /// <summary>Creates the pipe the next application connects to; with an access list on Windows.</summary>
    /// <param name="name">The name of the pipe.</param>
    /// <param name="allow">The accounts that may use it, besides the ones that always may.</param>
    /// <returns>A server stream that has not yet been connected.</returns>
    internal static NamedPipeServerStream CreateServer(string name, IReadOnlyList<string> allow)
    {
        if (OperatingSystem.IsWindows())
        {
            return NamedPipeServerStreamAcl.Create(
                name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, SecurityFor(allow));
        }

        return new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
    }

    /// <summary>The access list: the system, the administrators and the account of the agent in full, the built-in service accounts and the configured ones to read and write.</summary>
    /// <param name="allow">The configured accounts.</param>
    /// <returns>The security of the pipe.</returns>
    /// <exception cref="ArgumentException">An account in the list does not exist.</exception>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static PipeSecurity SecurityFor(IReadOnlyList<string> allow)
    {
        var security = new PipeSecurity();
        void Add(IdentityReference who, PipeAccessRights rights) =>
            security.AddAccessRule(new PipeAccessRule(who, rights, AccessControlType.Allow));

        Add(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl);
        Add(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl);
        Add(WindowsIdentity.GetCurrent().User!, PipeAccessRights.FullControl);
        Add(new SecurityIdentifier(WellKnownSidType.LocalServiceSid, null), PipeAccessRights.ReadWrite);
        Add(new SecurityIdentifier(WellKnownSidType.NetworkServiceSid, null), PipeAccessRights.ReadWrite);
        foreach (var account in allow)
        {
            try
            {
                Add(new NTAccount(account).Translate(typeof(SecurityIdentifier)), PipeAccessRights.ReadWrite);
            }
            catch (IdentityNotMappedException)
            {
                throw new ArgumentException($"The account '{account}' for the pipe does not exist on this machine.", nameof(allow));
            }
        }

        return security;
    }

    private async Task ServeAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        await using (server.ConfigureAwait(false))
        {
            try
            {
                await handler.HandleAsync(server, Admit, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown requested.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unexpected failure while serving an application on the pipe");
            }
        }
    }

    /// <summary>Goes by the instance of the service that runs in the process the application names, because the process id is what the machine vouches for.</summary>
    private Hello? Admit(Hello hello)
    {
        if (processes is null)
        {
            return hello;
        }

        return hello.Labels.TryGetValue(ProcessIdLabel, out var text) && int.TryParse(text, out var processId) && processes.InstanceOfProcess(processId) is { } instance
            ? hello with { InstanceId = instance }
            : null;
    }
}
