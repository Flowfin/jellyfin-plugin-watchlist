using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The routes this plugin claims, pinned. A route is a string, and a rename is a
/// change to a contract that no compiler in this repository or in anybody else's
/// notices.
/// </summary>
/// <remarks>
/// The pin is a set rather than a reading of one signature, so all three ways of
/// breaking the contract land on the same failure: adding a route, removing one, and
/// renaming one. Renaming is the worst of the three, because it looks like a tidy-up
/// in a diff and it is the only one that leaves callers with a 404 rather than with
/// an error naming what it wanted.
///
/// What a route is here is the verb and the template together. Two endpoints on one
/// template differing only in verb are a real shape, and a pin over templates alone
/// would let one turn into the other.
/// </remarks>
public class WatchlistApiRouteTests
{
    /// <summary>
    /// The whole contract. Editing this list is how a route changes; a change reaching
    /// the controller and not this line reds the run instead.
    /// </summary>
    private static readonly string[] TheRoutes =
    [
        "DELETE Watchlist/Items/{itemId}",
        "DELETE Watchlist/Shared/Items/{itemId}",
        "GET Watchlist/Export",
        "GET Watchlist/Items",
        "GET Watchlist/Shared/Items",
        "POST Watchlist/Import",
        "POST Watchlist/Items/{itemId}",
        "POST Watchlist/Shared/Items/{itemId}",
    ];

    /// <summary>
    /// The pin.
    /// </summary>
    [Fact]
    public void TheRoutesAreTheOnesWrittenDown()
    {
        Assert.Equal(TheRoutes, ApiSurface.RoutesOf(ApiSurface.Controllers()));
    }

    /// <summary>
    /// A pin over an empty reading holds nothing, so what was read is checked.
    /// </summary>
    [Fact]
    public void ThereAreRoutesToRead()
    {
        Assert.NotEmpty(ApiSurface.RoutesOf(ApiSurface.Controllers()));
    }

    /// <summary>
    /// The reader puts the prefix at the type together with the template at the method,
    /// which is the shape this plugin's controller has. Read wrongly, every route in
    /// the pin would be missing its prefix and the pin would still be internally
    /// consistent, so this is checked against a fixture whose two halves are known.
    /// </summary>
    [Fact]
    public void TheReaderJoinsThePrefixToTheTemplate()
    {
        Assert.Equal(
            ["DELETE Somewhere/Items/{itemId}", "GET Somewhere/Items"],
            ApiSurface.RoutesOf([typeof(AControllerWithAPrefix)]));
    }

    /// <summary>
    /// A template that starts at the root replaces the prefix rather than being hung
    /// off it. This is what the framework does, and a reader that concatenated anyway
    /// would report a route no client could reach and pin the wrong string.
    /// </summary>
    [Fact]
    public void ATemplateThatStartsAtTheRootReplacesThePrefix()
    {
        Assert.Equal(
            ["GET Elsewhere/Items"],
            ApiSurface.RoutesOf([typeof(AControllerWithAnAbsoluteTemplate)]));
    }

    /// <summary>
    /// A controller with no prefix at all, which is the shape a second controller
    /// arrives in when somebody writes the whole path at the method.
    /// </summary>
    [Fact]
    public void AControllerWithNoPrefixReadsAsItsTemplates()
    {
        Assert.Equal(
            ["POST Watchlist/Something"],
            ApiSurface.RoutesOf([typeof(AControllerWithNoPrefix)]));
    }

    /// <summary>
    /// The verb is part of what is pinned. Two endpoints on one template are one entry
    /// per verb, so an endpoint changing from a read to a write is a change the pin
    /// sees.
    /// </summary>
    [Fact]
    public void TheVerbIsPartOfTheRoute()
    {
        Assert.Equal(
            ["DELETE Somewhere/Items", "GET Somewhere/Items"],
            ApiSurface.RoutesOf([typeof(AControllerWithTwoVerbsOnOneTemplate)]));
    }

    /// <summary>
    /// The near miss for the pin itself, and the reason the pin is a set. One route
    /// renamed by one word, with the count unchanged, so nothing about the shape of the
    /// reading says anything is wrong.
    /// </summary>
    [Fact]
    public void TheReadingMovesWhenOneRouteIsRenamed()
    {
        Assert.NotEqual(
            ApiSurface.RoutesOf([typeof(AControllerWithAPrefix)]),
            ApiSurface.RoutesOf([typeof(AControllerWithOneRouteRenamed)]));
    }

    /// <summary>
    /// And the other direction, so the comparison is not one that separates every pair
    /// put in front of it: the same controller written twice under two names reads as
    /// the same contract.
    /// </summary>
    [Fact]
    public void TheReadingHoldsStillWhenNothingAboutTheRoutesChanged()
    {
        Assert.Equal(
            ApiSurface.RoutesOf([typeof(AControllerWithAPrefix)]),
            ApiSurface.RoutesOf([typeof(TheSameControllerUnderAnotherName)]));
    }

    /// <summary>
    /// The document against the same set. A contract written down in one place and
    /// implemented in another is two things that agree until somebody changes one, and
    /// the one that gets changed is never the document.
    /// </summary>
    [Fact]
    public void EveryRouteHasASectionInTheDocument()
    {
        var document = ApiSurface.Document();

        var undocumented = TheRoutes
            .Where(route => !document.Contains("## " + route, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            undocumented.Count == 0,
            "These routes have no section in docs/api.md: " + string.Join(", ", undocumented));
    }

    /// <summary>
    /// And the other direction, because an entry left behind by a removal reads as an
    /// endpoint somebody can call and cannot.
    /// </summary>
    [Fact]
    public void EverySectionInTheDocumentNamesARoute()
    {
        var orphaned = ApiSurface.RouteSectionsIn(ApiSurface.Document())
            .Keys
            .Where(heading => !TheRoutes.Contains(heading, StringComparer.Ordinal))
            .OrderBy(heading => heading, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            orphaned.Count == 0,
            "docs/api.md describes these, and this plugin has no such route: "
                + string.Join(", ", orphaned));
    }

    /// <summary>
    /// The near miss for both, and the reason the headings are matched on the whole
    /// route rather than on the path. A heading naming the right path under the wrong
    /// verb documents an endpoint nobody can call, and it is one word away from the
    /// right one.
    /// </summary>
    [Fact]
    public void ASectionUnderTheWrongVerbIsNotASectionForTheRoute()
    {
        var document = ApiSurface.Document();

        Assert.Contains("## POST Watchlist/Items/{itemId}", document, StringComparison.Ordinal);
        Assert.DoesNotContain("## PUT Watchlist/Items/{itemId}", document, StringComparison.Ordinal);
    }

    /// <summary>
    /// Not shipped. The reader is checked against a controller whose two halves are
    /// known, because a reader that dropped the prefix would agree with a pin written
    /// from its own output and neither would be the contract.
    /// </summary>
    [Route("Somewhere")]
    private sealed class AControllerWithAPrefix : ControllerBase
    {
        [HttpGet("Items")]
        public IActionResult GetItems() => Ok();

        [HttpDelete("Items/{itemId}")]
        public IActionResult RemoveItem(Guid itemId) => Ok(itemId);
    }

    /// <summary>
    /// The same contract under another type name, which is what the comparison has to
    /// call unchanged.
    /// </summary>
    [Route("Somewhere")]
    private sealed class TheSameControllerUnderAnotherName : ControllerBase
    {
        [HttpGet("Items")]
        public IActionResult ReadTheItems() => Ok();

        [HttpDelete("Items/{itemId}")]
        public IActionResult DropTheItem(Guid itemId) => Ok(itemId);
    }

    /// <summary>
    /// One word changed in one route and nothing else, which is the mistake a rename
    /// makes and the one a count would miss.
    /// </summary>
    [Route("Somewhere")]
    private sealed class AControllerWithOneRouteRenamed : ControllerBase
    {
        [HttpGet("Entries")]
        public IActionResult GetItems() => Ok();

        [HttpDelete("Items/{itemId}")]
        public IActionResult RemoveItem(Guid itemId) => Ok(itemId);
    }

    /// <summary>
    /// A template written from the root, which the framework does not hang off the
    /// prefix.
    /// </summary>
    [Route("Somewhere")]
    private sealed class AControllerWithAnAbsoluteTemplate : ControllerBase
    {
        [HttpGet("/Elsewhere/Items")]
        public IActionResult GetItems() => Ok();
    }

    /// <summary>
    /// No prefix at the type, the whole path at the method.
    /// </summary>
    private sealed class AControllerWithNoPrefix : ControllerBase
    {
        [HttpPost("Watchlist/Something")]
        public IActionResult AddSomething() => Ok();
    }

    /// <summary>
    /// Two verbs on one template, which is what adding and removing an item on one
    /// path looks like.
    /// </summary>
    [Route("Somewhere")]
    private sealed class AControllerWithTwoVerbsOnOneTemplate : ControllerBase
    {
        [HttpGet("Items")]
        public IActionResult GetItems() => Ok();

        [HttpDelete("Items")]
        public IActionResult RemoveItems() => Ok();
    }
}
