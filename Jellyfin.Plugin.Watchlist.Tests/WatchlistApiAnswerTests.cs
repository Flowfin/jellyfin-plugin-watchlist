using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The answers each route can give, read off the actions and compared with the table
/// docs/api.md states for that route.
/// </summary>
/// <remarks>
/// <para>
/// The routes are pinned in <see cref="WatchlistApiRouteTests"/> and the answers were
/// not. They are the other half of the same contract: a caller decides what to do from
/// the code it got back, and the table in the document is where whoever writes that
/// caller learns which code means what. Two places hold that decision, the attributes
/// on the action and the rows in the document, and until this comparison existed
/// nothing kept them from drifting apart.
/// </para>
/// <para>
/// Drift is silent in both directions and neither direction is hypothetical. A code
/// added to an action with the table left alone tells a caller to handle an answer the
/// document never mentions. A row left in the table after the action stopped producing
/// it tells them to handle one that never arrives, and the row a reader trusts most is
/// the one nobody has touched in a while.
/// </para>
/// <para>
/// What this does not judge is whether the codes are the right ones. Which outcome gets
/// which code is #29, and the document is where that decision is written down; this
/// reads the two against each other and says nothing about either.
/// </para>
/// </remarks>
public class WatchlistApiAnswerTests
{
    private const string TheAdd = "POST Watchlist/Items/{itemId}";

    private const string TheRemoval = "DELETE Watchlist/Items/{itemId}";

    /// <summary>
    /// The comparison.
    /// </summary>
    [Fact]
    public void EveryRouteAnswersWithWhatTheDocumentStates()
    {
        var disagreements = Disagreements(ApiSurface.Document());

        Assert.True(
            disagreements.Count == 0,
            "docs/api.md and the endpoints disagree about what a route answers: "
                + string.Join("; ", disagreements));
    }

    /// <summary>
    /// A comparison that reads nothing agrees with everything, so what it ran over is
    /// asserted. Both sides are read here, because a document parsed into no rows and an
    /// assembly scanned into no actions produce the same empty agreement.
    /// </summary>
    [Fact]
    public void ThereAreAnswersToRead()
    {
        var declared = ApiSurface.AnswersOf(ApiSurface.Controllers());
        var stated = StatedBy(ApiSurface.Document());

        Assert.NotEmpty(declared);
        Assert.NotEmpty(stated);
        Assert.All(declared, route => Assert.NotEmpty(route.Value));
        Assert.All(stated, route => Assert.NotEmpty(route.Value));
        Assert.Contains(TheAdd, declared.Keys, StringComparer.Ordinal);
        Assert.Contains(TheAdd, stated.Keys, StringComparer.Ordinal);
    }

    /// <summary>
    /// The near miss, and the mistake it is drawn from: an outcome that stops being
    /// produced, or was never produced, with the row for it still in the table. The
    /// mutation runs on the document because a test cannot rewrite the attributes it was
    /// compiled against, and it produces the same disagreement from the same side.
    /// </summary>
    [Fact]
    public void ADocumentStatingAnAnswerNoEndpointGivesIsRefused()
    {
        var drifted = WithoutTheAnswer(ApiSurface.Document(), TheAdd, StatusCodes.Status409Conflict);

        var disagreements = Disagreements(drifted);

        Assert.Single(disagreements);
        Assert.Contains(TheAdd, disagreements[0], StringComparison.Ordinal);
        Assert.Contains("409", disagreements[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// The other direction, which is the likelier of the two: a code added to an action
    /// and the table left as it was, so the document is a contract short of one answer.
    /// The mutation is on the document for the reason above, and it is the same missing
    /// row.
    /// </summary>
    [Fact]
    public void ADocumentSilentAboutAnAnswerAnEndpointGivesIsRefused()
    {
        var drifted = WithoutTheAnswer(
            ApiSurface.Document(),
            TheRemoval,
            StatusCodes.Status503ServiceUnavailable);

        var disagreements = Disagreements(drifted);

        Assert.Single(disagreements);
        Assert.Contains(TheRemoval, disagreements[0], StringComparison.Ordinal);
        Assert.Contains("503", disagreements[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// A whole section taken out, which is what a route renamed in the document looks
    /// like from this side, and it is refused as an absence rather than passing because
    /// there is nothing left to compare.
    /// </summary>
    [Fact]
    public void ARouteWithNoTableInTheDocumentIsRefused()
    {
        var drifted = WithoutTheSection(ApiSurface.Document(), TheRemoval);

        var disagreements = Disagreements(drifted);

        Assert.Single(disagreements);
        Assert.Contains(TheRemoval, disagreements[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// The one-change neighbour of all three mutations is the document as it is
    /// committed. Without this they would prove the mutations are unusual rather than
    /// that the comparison reads them.
    /// </summary>
    [Fact]
    public void TheCommittedDocumentTripsNoneOfThem()
    {
        Assert.Empty(Disagreements(ApiSurface.Document()));
    }

    /// <summary>
    /// The reader on the code side, checked against an action whose attributes are
    /// known. Read wrongly it would return the same thing for every action, and a
    /// document written from its output would agree with it.
    /// </summary>
    [Fact]
    public void TheReaderTakesTheCodesOffTheAction()
    {
        Assert.Equal(
            new[] { StatusCodes.Status200OK, StatusCodes.Status503ServiceUnavailable },
            ApiSurface.AnswersOf([typeof(AControllerWithTwoAnswers)])["GET Somewhere/Items"]);
    }

    /// <summary>
    /// An action that declares nothing keeps its route in the reading, with no answers
    /// under it. That is the shape an endpoint arrives in before the attributes are
    /// written, and it is refused above rather than skipped, because a route the reading
    /// drops is a route the comparison is silent about.
    /// </summary>
    [Fact]
    public void AnActionDeclaringNothingKeepsItsRouteWithNoAnswers()
    {
        Assert.Empty(ApiSurface.AnswersOf([typeof(AControllerDeclaringNothing)])["GET Somewhere/Items"]);
    }

    /// <summary>
    /// The near miss for the reader: one attribute taken off one action, which is the
    /// edit that makes the document wrong without touching it.
    /// </summary>
    [Fact]
    public void TheReadingMovesWhenOneAnswerIsTakenOffAnAction()
    {
        Assert.NotEqual(
            ApiSurface.AnswersOf([typeof(AControllerWithTwoAnswers)])["GET Somewhere/Items"],
            ApiSurface.AnswersOf([typeof(AControllerWithOneAnswerFewer)])["GET Somewhere/Items"]);
    }

    /// <summary>
    /// And the other direction, so the reader is not one that separates every pair put
    /// in front of it: the same answers under another method name read the same.
    /// </summary>
    [Fact]
    public void TheReadingHoldsStillWhenOnlyTheMethodNameChanged()
    {
        Assert.Equal(
            ApiSurface.AnswersOf([typeof(AControllerWithTwoAnswers)])["GET Somewhere/Items"],
            ApiSurface.AnswersOf([typeof(TheSameAnswersUnderAnotherMethodName)])["GET Somewhere/Items"]);
    }

    /// <summary>
    /// Every route where the actions and the document do not say the same thing, with
    /// both readings in the sentence so a failure says which way round it is.
    /// </summary>
    /// <param name="document">The document to read.</param>
    /// <returns>One sentence per route that disagrees.</returns>
    private static IReadOnlyList<string> Disagreements(string document)
    {
        var declared = ApiSurface.AnswersOf(ApiSurface.Controllers());
        var stated = StatedBy(document);

        return declared.Keys
            .Concat(stated.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(route => route, StringComparer.Ordinal)
            .Select(route => Disagreement(
                route,
                declared.TryGetValue(route, out var answers) ? answers : null,
                stated.TryGetValue(route, out var rows) ? rows : null))
            .OfType<string>()
            .ToList();
    }

    /// <summary>
    /// What one route's two readings disagree about, or nothing where they agree.
    /// </summary>
    /// <param name="route">The route being read.</param>
    /// <param name="declared">What the action declares, or null where there is no such route.</param>
    /// <param name="stated">What the document states, or null where it has no table for it.</param>
    /// <returns>The sentence, or null.</returns>
    private static string? Disagreement(string route, IReadOnlyList<int>? declared, IReadOnlyList<int>? stated)
    {
        if (declared is null)
        {
            return route + ": docs/api.md states answers for it and this plugin has no such route";
        }

        if (stated is null)
        {
            return route + ": the action declares " + Written(declared)
                + " and docs/api.md states no table for it";
        }

        return declared.SequenceEqual(stated)
            ? null
            : route + ": the action declares " + Written(declared)
                + " and docs/api.md states " + Written(stated);
    }

    /// <summary>
    /// A set of codes as a failure should read it.
    /// </summary>
    /// <param name="answers">The codes.</param>
    /// <returns>The codes, or a word for none of them.</returns>
    private static string Written(IReadOnlyList<int> answers) => answers.Count == 0
        ? "nothing"
        : string.Join(", ", answers.Select(code => code.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// The answers the document states, taken from the first column of every table row
    /// in a route's section that opens with a status code.
    /// </summary>
    /// <param name="document">The document to read.</param>
    /// <returns>One entry per route section, the codes ascending.</returns>
    /// <remarks>
    /// A section's other tables are read by the same rule and contribute nothing: a
    /// parameter table's first column is a name, and a name is not a number. That is why
    /// the row is matched on what its first cell is rather than on which table it sits
    /// in, which would need the document to keep its tables in an order.
    /// </remarks>
    private static IReadOnlyDictionary<string, IReadOnlyList<int>> StatedBy(string document) =>
        ApiSurface.RouteSectionsIn(document)
            .ToDictionary(
                section => section.Key,
                section => (IReadOnlyList<int>)section.Value
                    .Select(CodeIn)
                    .OfType<int>()
                    .Distinct()
                    .OrderBy(code => code)
                    .ToList(),
                StringComparer.Ordinal);

    /// <summary>
    /// The status code a table row opens with, or nothing where its first cell is not
    /// one.
    /// </summary>
    /// <param name="line">The line to read.</param>
    /// <returns>The code, or null.</returns>
    private static int? CodeIn(string line)
    {
        if (!line.StartsWith("|", StringComparison.Ordinal))
        {
            return null;
        }

        var cells = line.Split('|');

        return cells.Length > 1
            && int.TryParse(
                cells[1].Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var code)
            ? code
            : null;
    }

    /// <summary>
    /// The committed document with one answer's row taken out of one route's table, and
    /// nothing else touched. The removal is confined to that route's own section, so a
    /// second route stating the same code keeps its row.
    /// </summary>
    /// <param name="document">The document to mutate.</param>
    /// <param name="route">The route whose table is rewritten.</param>
    /// <param name="code">The answer whose row is taken out.</param>
    /// <returns>The mutated document.</returns>
    private static string WithoutTheAnswer(string document, string route, int code)
    {
        var lines = ApiSurface.Lines(document).ToList();
        var section = SectionOf(lines, route);

        var row = lines.FindIndex(section.Start, section.Length, line => CodeIn(line) == code);
        Assert.True(row >= 0, "The section for " + route + " states no " + code + " to take out.");

        lines.RemoveAt(row);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// The committed document with one route's section taken out entirely, heading and
    /// all.
    /// </summary>
    /// <param name="document">The document to mutate.</param>
    /// <param name="route">The route whose section is removed.</param>
    /// <returns>The mutated document.</returns>
    private static string WithoutTheSection(string document, string route)
    {
        var lines = ApiSurface.Lines(document).ToList();
        var section = SectionOf(lines, route);

        lines.RemoveRange(section.Start - 1, section.Length + 1);

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Where one route's section body sits in the document.
    /// </summary>
    /// <param name="lines">The document's lines.</param>
    /// <param name="route">The route whose heading is looked for.</param>
    /// <returns>The first line under the heading and how many lines the section holds.</returns>
    private static (int Start, int Length) SectionOf(IReadOnlyList<string> lines, string route)
    {
        var all = lines.ToList();

        var heading = all.FindIndex(line => string.Equals(line, "## " + route, StringComparison.Ordinal));
        Assert.True(heading >= 0, "docs/api.md has no section for " + route + " to mutate.");

        var next = all.FindIndex(heading + 1, line => line.StartsWith("#", StringComparison.Ordinal));
        var end = next < 0 ? all.Count : next;

        return (heading + 1, end - heading - 1);
    }

    /// <summary>
    /// Not shipped. The reader is checked against an action whose attributes are known,
    /// because a reader returning the same set for everything would agree with a table
    /// written from its own output.
    /// </summary>
    [Route("Somewhere")]
    private sealed class AControllerWithTwoAnswers : ControllerBase
    {
        /// <summary>
        /// Answers two ways.
        /// </summary>
        /// <returns>Nothing a test reads.</returns>
        [HttpGet("Items")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public IActionResult GetItems() => Ok();
    }

    /// <summary>
    /// The same two answers under another method name, which is what the reader has to
    /// call unchanged.
    /// </summary>
    [Route("Somewhere")]
    private sealed class TheSameAnswersUnderAnotherMethodName : ControllerBase
    {
        /// <summary>
        /// Answers two ways.
        /// </summary>
        /// <returns>Nothing a test reads.</returns>
        [HttpGet("Items")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public IActionResult ReadTheItems() => Ok();
    }

    /// <summary>
    /// One attribute fewer and nothing else, which is the edit that leaves a table
    /// stating an answer nobody gives.
    /// </summary>
    [Route("Somewhere")]
    private sealed class AControllerWithOneAnswerFewer : ControllerBase
    {
        /// <summary>
        /// Answers one way.
        /// </summary>
        /// <returns>Nothing a test reads.</returns>
        [HttpGet("Items")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetItems() => Ok();
    }

    /// <summary>
    /// An action carrying no answer at all, which is how an endpoint is written before
    /// anybody says what it can return.
    /// </summary>
    [Route("Somewhere")]
    private sealed class AControllerDeclaringNothing : ControllerBase
    {
        /// <summary>
        /// Answers, and says nothing about how.
        /// </summary>
        /// <returns>Nothing a test reads.</returns>
        [HttpGet("Items")]
        public IActionResult GetItems() => Ok();
    }
}
