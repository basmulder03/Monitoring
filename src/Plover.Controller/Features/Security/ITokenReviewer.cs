namespace Plover.Controller.Features.Security;

/// <summary>Asks the Kubernetes API server who a token belongs to.</summary>
internal interface ITokenReviewer
{
    /// <summary>Checks a token.</summary>
    /// <param name="token">The token an agent presented.</param>
    /// <param name="audience">The audience the token has to be made for.</param>
    /// <param name="ct">Cancels the check.</param>
    /// <returns>The user name of a valid token made for the audience, such as <c>system:serviceaccount:shop:plover-agent</c>; <see langword="null"/> for any other token.</returns>
    /// <exception cref="HttpRequestException">The API server cannot be reached or refuses the controller, which is a fault of the controller and not of the agent.</exception>
    ValueTask<string?> ReviewAsync(string token, string audience, CancellationToken ct);
}
