using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Plover.Agent.Features.SidecarConnections;

/// <summary>Asks Windows which process opened the other end of a named pipe, which an application cannot lie about.</summary>
internal static class NamedPipeClient
{
    /// <summary>The process id of the client of a pipe that is connected.</summary>
    /// <param name="server">The server end of the pipe.</param>
    /// <returns>The id, or <see langword="null"/> when Windows cannot say, for example because the client already left.</returns>
    public static int? ProcessIdOf(NamedPipeServerStream server)
    {
        var handle = server.SafePipeHandle;
        var added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            return GetNamedPipeClientProcessId(handle.DangerousGetHandle(), out var id) ? (int)id : null;
        }
        finally
        {
            if (added)
            {
                handle.DangerousRelease();
            }
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint clientProcessId);
}
