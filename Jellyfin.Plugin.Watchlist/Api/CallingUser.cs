using System;
using System.Security.Claims;

namespace Jellyfin.Plugin.Watchlist.Api;

/// <summary>
/// Who a request is from. The one place in this plugin that reads it, so that every
/// endpoint asks the same question and gets the same refusal.
/// </summary>
/// <remarks>
/// A private watchlist is private, and the only thing standing between one user's list
/// and another's is that this is read correctly every time. Two readings of it are two
/// chances for one of them to be lenient, and the lenient one is the one that gets
/// found by somebody else.
/// </remarks>
public static class CallingUser
{
    /// <summary>
    /// The claim the server puts the caller's user identifier in.
    /// </summary>
    /// <remarks>
    /// The server's own constant lives in an assembly a plugin does not reference, so
    /// this is a copy of a string rather than a reference to one. What that costs is
    /// stated rather than hidden: a test pins this literal, so an edit here is a red
    /// suite, and nothing anywhere catches the server renaming its own constant. That
    /// failure is silent in the worst direction, because a claim that is no longer
    /// there reads as a caller with no identity rather than as an error.
    /// </remarks>
    public const string Claim = "Jellyfin-UserId";

    /// <summary>
    /// The caller's identifier, or nothing.
    /// </summary>
    /// <param name="principal">The request's principal.</param>
    /// <returns>The identifier, or null when the request carries none this plugin will use.</returns>
    /// <remarks>
    /// Four ways to have no identity and one answer for all of them: no principal, no
    /// claim, a claim that is not an identifier, and the empty identifier. The last is
    /// the one worth spelling out, because it parses. A claim of all zeroes reads as a
    /// perfectly good identifier to <c>Guid.TryParse</c>, and a document named after it
    /// is a file the store would happily write, so a caller supplying it would have a
    /// list of their own that nobody owns. It is refused here rather than somewhere a
    /// later endpoint might forget.
    /// </remarks>
    public static Guid? IdOf(ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirst(Claim)?.Value;

        if (!Guid.TryParse(value, out var userId) || userId == Guid.Empty)
        {
            return null;
        }

        return userId;
    }
}
