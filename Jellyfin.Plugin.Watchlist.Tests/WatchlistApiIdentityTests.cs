using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using Jellyfin.Plugin.Watchlist.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// Who a request is from, and the two properties that keep one user's list away from
/// another: the identity is read in one place, and no route names a user at all.
/// </summary>
public class WatchlistApiIdentityTests
{
    private const string PluginSourcePrefix = "pluginsource/";

    private static readonly Guid AUser = Guid.Parse("33333333-3333-3333-3333-333333333333");

    /// <summary>
    /// The claim string, pinned. What this catches is an edit to this repository's copy,
    /// which stops being the string the server sets and would then read as every request
    /// having no identity.
    /// </summary>
    /// <remarks>
    /// What it does not catch is stated here rather than left to be assumed: the server
    /// renaming its own constant. That constant is in an assembly a plugin does not
    /// reference, so nothing in this suite can compare the two, and the failure it would
    /// produce is silent in the worst direction. This test is a pin on one end of a rope
    /// whose other end nobody here can see.
    /// </remarks>
    [Fact]
    public void TheClaimIsTheOneTheServerPutsTheUserIdentifierIn()
    {
        Assert.Equal("Jellyfin-UserId", CallingUser.Claim);
    }

    /// <summary>
    /// The identity is read in one place. Counted over the plugin's own sources rather
    /// than asserted, so a second reader added tomorrow is a red suite on the day it is
    /// added and not on the day somebody notices the two have drifted apart.
    /// </summary>
    [Fact]
    public void OnlyOneFileInThePluginNamesTheClaim()
    {
        var naming = PluginSources()
            .Where(source => source.Text.Contains(CallingUser.Claim, StringComparison.Ordinal))
            .Select(source => source.Name)
            .ToList();

        Assert.Equal(["CallingUser.cs"], naming);
    }

    /// <summary>
    /// A check that reads nothing passes everything, so what was read is checked rather
    /// than trusted.
    /// </summary>
    [Fact]
    public void ThePluginSourcesAreThereToBeRead()
    {
        Assert.NotEmpty(PluginSources());
    }

    /// <summary>
    /// No principal at all, which is what a call made outside a request looks like.
    /// </summary>
    [Fact]
    public void NoPrincipalIsNoIdentity()
    {
        Assert.Null(CallingUser.IdOf(null));
    }

    /// <summary>
    /// A principal with no such claim, which is what an unauthenticated request looks
    /// like.
    /// </summary>
    [Fact]
    public void AnAbsentClaimIsNoIdentity()
    {
        Assert.Null(CallingUser.IdOf(PrincipalWith(null)));
    }

    /// <summary>
    /// A claim that is not an identifier. Refused rather than parsed into something.
    /// </summary>
    [Fact]
    public void AClaimThatIsNotAnIdentifierIsNoIdentity()
    {
        Assert.Null(CallingUser.IdOf(PrincipalWith("not-an-identifier")));
    }

    /// <summary>
    /// The near miss, and the only one of these with no symptom. All zeroes parses, so
    /// a caller supplying it would reach the store, and the store would name a document
    /// after it and write one. It is a user nobody is.
    /// </summary>
    [Fact]
    public void TheEmptyIdentifierIsNoIdentity()
    {
        Assert.True(Guid.TryParse(Guid.Empty.ToString(), out _));
        Assert.Null(CallingUser.IdOf(PrincipalWith(Guid.Empty.ToString())));
    }

    /// <summary>
    /// And the case that has to work, so the four refusals above are not a helper that
    /// refuses everything.
    /// </summary>
    [Fact]
    public void AClaimThatIsAnIdentifierIsRead()
    {
        Assert.Equal(AUser, CallingUser.IdOf(PrincipalWith(AUser.ToString())));
    }

    /// <summary>
    /// The property that makes the refusals above sufficient: there is no route by which
    /// a caller names somebody else. An endpoint that takes a user identifier is one
    /// authorisation mistake away from serving another user's private list, and no
    /// reading of the identity helper would catch it.
    /// </summary>
    [Fact]
    public void NoEndpointInThisPluginTakesAUserIdentifier()
    {
        var offenders = Controllers()
            .SelectMany(UserIdentifierParametersOf)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "These endpoint parameters name a user: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// A check that reads no controllers passes over any plugin, including one with an
    /// endpoint per user.
    /// </summary>
    [Fact]
    public void ThereAreControllersToRead()
    {
        Assert.NotEmpty(Controllers());
    }

    /// <summary>
    /// The near miss for the check above, which is the shape somebody writes when they
    /// want the same list from a script: a route with the user in it. It is not on any
    /// controller this plugin ships, so it is written here to be refused.
    /// </summary>
    [Fact]
    public void TheScanFindsAnEndpointThatTakesAUserIdentifier()
    {
        Assert.Equal(
            ["GetSomebodysList(userId)"],
            UserIdentifierParametersOf(typeof(AControllerSomebodyMightWrite)));
    }

    /// <summary>
    /// And the other direction, so the scan is not one that refuses every parameter. An
    /// item identifier in a route is what adding and removing will need.
    /// </summary>
    [Fact]
    public void TheScanPassesAnEndpointThatTakesAnItemIdentifier()
    {
        Assert.Empty(UserIdentifierParametersOf(typeof(AControllerWithAnItemInItsRoute)));
    }

    private static ClaimsPrincipal PrincipalWith(string? claimValue)
    {
        var identity = new ClaimsIdentity();

        if (claimValue is not null)
        {
            identity.AddClaim(new Claim(CallingUser.Claim, claimValue));
        }

        return new ClaimsPrincipal(identity);
    }

    private static IReadOnlyList<Type> Controllers() => PluginUnderTest.Assembly
        .GetTypes()
        .Where(t => t.IsPublic && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// The parameters of a controller's endpoints that name a user, by the name the
    /// parameter carries. A name rather than a type, because the type a user identifier
    /// arrives as is the same one an item identifier arrives as, and refusing that type
    /// would refuse the routes #26 needs.
    /// </summary>
    /// <param name="controller">The controller to read.</param>
    /// <returns>One entry per offending parameter, as method and parameter.</returns>
    private static IReadOnlyList<string> UserIdentifierParametersOf(Type controller) => controller
        .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        .Where(method => method.GetCustomAttributes(typeof(HttpMethodAttribute), inherit: true).Length > 0)
        .SelectMany(method => method.GetParameters().Select(parameter => new { method, parameter }))
        .Where(pair => pair.parameter.Name is not null
            && pair.parameter.Name.Contains("user", StringComparison.OrdinalIgnoreCase))
        .Select(pair => pair.method.Name + "(" + pair.parameter.Name + ")")
        .OrderBy(name => name, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// The plugin's own sources, as the test assembly carries them for the invariant
    /// guard to read. Named by file rather than by path, the way that guard reports
    /// them, because the separator inside a resource name is the build machine's and
    /// this suite runs on three of them.
    /// </summary>
    /// <returns>One entry per source file.</returns>
    private static IReadOnlyList<(string Name, string Text)> PluginSources()
    {
        var assembly = Assembly.GetExecutingAssembly();

        return assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(PluginSourcePrefix, StringComparison.Ordinal))
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);

                return (Name: name.Split('/', '\\')[^1], Text: reader.ReadToEnd());
            })
            .OrderBy(source => source.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Not shipped. It exists so the scan above has something to refuse, because a scan
    /// that has never refused anything is a scan nobody has watched work.
    /// </summary>
    private sealed class AControllerSomebodyMightWrite : ControllerBase
    {
        [HttpGet("Watchlist/Users/{userId}/Items")]
        public IActionResult GetSomebodysList(Guid userId) => Ok(userId);
    }

    /// <summary>
    /// Also not shipped, and the shape the scan has to leave alone.
    /// </summary>
    private sealed class AControllerWithAnItemInItsRoute : ControllerBase
    {
        [HttpDelete("Watchlist/Items/{itemId}")]
        public IActionResult RemoveAnItem(Guid itemId) => Ok(itemId);
    }
}
