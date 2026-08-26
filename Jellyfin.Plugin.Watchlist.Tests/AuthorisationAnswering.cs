using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The server's answer to whether a caller may do something, fixed by the test.
/// </summary>
/// <remarks>
/// The plugin asks the server's own elevation policy rather than deciding for itself
/// who is an administrator, which means the question leaves this plugin through
/// <see cref="IAuthorizationService"/>. A test drives both answers by handing the
/// controller one of these, and no policy handler, no server and no host is involved.
///
/// It answers the same way whatever it is asked. The plugin asks one question, and a
/// fake that varied by policy name would be asserting the name from the inside rather
/// than letting a test assert it from the outside.
/// </remarks>
public sealed class AuthorisationAnswering : IAuthorizationService
{
    private readonly bool _succeeds;

    private AuthorisationAnswering(bool succeeds)
    {
        _succeeds = succeeds;
    }

    /// <summary>
    /// Gets the policies this was asked about, in order, so a test can assert which
    /// question the plugin asked rather than only what it did with the answer.
    /// </summary>
    public List<string> Asked { get; } = [];

    /// <summary>
    /// A server that says yes.
    /// </summary>
    /// <returns>The service.</returns>
    public static AuthorisationAnswering Yes() => new(true);

    /// <summary>
    /// A server that says no.
    /// </summary>
    /// <returns>The service.</returns>
    public static AuthorisationAnswering No() => new(false);

    /// <inheritdoc />
    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        object? resource,
        IEnumerable<IAuthorizationRequirement> requirements) => Task.FromResult(Answer());

    /// <inheritdoc />
    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user,
        object? resource,
        string policyName)
    {
        Asked.Add(policyName);

        return Task.FromResult(Answer());
    }

    private AuthorizationResult Answer() =>
        _succeeds ? AuthorizationResult.Success() : AuthorizationResult.Failed();
}
