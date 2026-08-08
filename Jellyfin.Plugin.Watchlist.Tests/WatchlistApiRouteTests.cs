using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
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
    /// <summary>
    /// The verbs a heading in the document may open with. It is what tells a section
    /// about a route from a section about anything else, so a page can carry prose
    /// headings without either direction of the comparison reading them as routes.
    /// </summary>
    private static readonly string[] Verbs =
    [
        "DELETE ", "GET ", "HEAD ", "OPTIONS ", "PATCH ", "POST ", "PUT ",
    ];

    private static readonly string[] TheRoutes =
    [
        "DELETE Watchlist/Items/{itemId}",
        "GET Watchlist/Items",
        "POST Watchlist/Items/{itemId}",
    ];

    /// <summary>
    /// The pin.
    /// </summary>
    [Fact]
    public void TheRoutesAreTheOnesWrittenDown()
    {
        Assert.Equal(TheRoutes, RoutesOf(Controllers()));
    }

    /// <summary>
    /// A pin over an empty reading holds nothing, so what was read is checked.
    /// </summary>
    [Fact]
    public void ThereAreRoutesToRead()
    {
        Assert.NotEmpty(RoutesOf(Controllers()));
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
            RoutesOf([typeof(AControllerWithAPrefix)]));
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
            RoutesOf([typeof(AControllerWithAnAbsoluteTemplate)]));
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
            RoutesOf([typeof(AControllerWithNoPrefix)]));
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
            RoutesOf([typeof(AControllerWithTwoVerbsOnOneTemplate)]));
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
            RoutesOf([typeof(AControllerWithAPrefix)]),
            RoutesOf([typeof(AControllerWithOneRouteRenamed)]));
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
            RoutesOf([typeof(AControllerWithAPrefix)]),
            RoutesOf([typeof(TheSameControllerUnderAnotherName)]));
    }

    /// <summary>
    /// The document against the same set. A contract written down in one place and
    /// implemented in another is two things that agree until somebody changes one, and
    /// the one that gets changed is never the document.
    /// </summary>
    [Fact]
    public void EveryRouteHasASectionInTheDocument()
    {
        var document = Document();

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
        var orphaned = Document()
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line[3..].Trim())
            .Where(heading => Verbs.Any(verb => heading.StartsWith(verb, StringComparison.Ordinal)))
            .Where(heading => !TheRoutes.Contains(heading, StringComparer.Ordinal))
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
        var document = Document();

        Assert.Contains("## POST Watchlist/Items/{itemId}", document, StringComparison.Ordinal);
        Assert.DoesNotContain("## PUT Watchlist/Items/{itemId}", document, StringComparison.Ordinal);
    }

    /// <summary>
    /// The controllers a server scanning this assembly would add to its route table.
    /// </summary>
    /// <returns>One entry per controller.</returns>
    private static IReadOnlyList<Type> Controllers() => PluginUnderTest.Assembly
        .GetTypes()
        .Where(t => t.IsPublic && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// Every route these controllers claim, as the verb and the full template.
    /// </summary>
    /// <param name="controllers">The controllers to read.</param>
    /// <returns>The routes, ordered so a failure reads the same twice.</returns>
    private static IReadOnlyList<string> RoutesOf(IReadOnlyList<Type> controllers) => controllers
        .SelectMany(controller => controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method
                .GetCustomAttributes(inherit: true)
                .OfType<HttpMethodAttribute>()
                .SelectMany(http => http.HttpMethods.Select(verb =>
                    verb + " " + Joined(PrefixOf(controller), http.Template)))))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(route => route, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// The route prefix a controller carries, or nothing when it carries none.
    /// </summary>
    /// <param name="controller">The controller to read.</param>
    /// <returns>The prefix as written.</returns>
    private static string PrefixOf(Type controller) => controller
        .GetCustomAttributes(inherit: true)
        .OfType<IRouteTemplateProvider>()
        .Where(route => route is not HttpMethodAttribute)
        .Select(route => route.Template ?? string.Empty)
        .FirstOrDefault(string.Empty);

    /// <summary>
    /// The prefix and the template as one path, the way the framework joins them: a
    /// template starting at the root replaces the prefix instead of hanging off it.
    /// </summary>
    /// <param name="prefix">What the controller carries.</param>
    /// <param name="template">What the method carries.</param>
    /// <returns>The path a client would call.</returns>
    /// <summary>
    /// The document, read out of the test assembly rather than off disk, so what is
    /// compared is this tree's file and never a copy that happens to sit beside the
    /// test host.
    /// </summary>
    /// <returns>The document text.</returns>
    private static string Document()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string Resource = "api.md";

        using var stream = assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException(
                Resource + " is not embedded in the test assembly. The assembly carries: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    private static string Joined(string prefix, string? template)
    {
        var tail = template ?? string.Empty;

        if (tail.StartsWith('/'))
        {
            return tail.TrimStart('/');
        }

        if (prefix.Length == 0)
        {
            return tail;
        }

        return tail.Length == 0 ? prefix : prefix.TrimEnd('/') + "/" + tail;
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
