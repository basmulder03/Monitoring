using System.Security.Cryptography;
using System.Text;

namespace Plover.Controller.Features.Security;

/// <summary>Hashes agent tokens, so the controller configuration never contains a usable secret.</summary>
public static class AgentTokenHash
{
    /// <summary>Computes the SHA-256 hash of a token.</summary>
    /// <param name="token">The token as handed to the agent.</param>
    /// <returns>The hash as lower-case hexadecimal text.</returns>
    public static string Compute(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
