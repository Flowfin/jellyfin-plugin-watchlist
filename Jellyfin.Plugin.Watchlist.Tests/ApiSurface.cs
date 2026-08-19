using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What this plugin's controllers claim, read off the assembly rather than off a list:
/// the routes a client can call, and the answers each route declares it can give.
/// </summary>
/// <remarks>
/// <para>
/// Two suites read this and neither owns it. <see cref="WatchlistApiRouteTests"/> pins
/// the routes and <see cref="WatchlistApiAnswerTests"/> pins the answers, and both
/// compare what they read against docs/api.md. The join between a controller's prefix
/// and a method's template is the part that had to be shared: a second copy of it
/// would be a second answer to what a route is, and a comparison written from a
/// reader's own output agrees with itself whichever way the reader is wrong.
/// </para>
/// <para>
/// Everything here is a reading. Nothing in this file says what the surface ought to
/// be, so a test that asserts nothing cannot be green by asking a helper that already
/// agreed with it.
/// </para>
/// </remarks>
internal static class ApiSurface
{
    private const string DocumentResource = "api.md";

    /// <summary>
    /// The verbs a heading in the document may open with. It is what tells a section
    /// about a route from a section about anything else, so a page can carry prose
    /// headings without either direction of a comparison reading them as routes.
    /// </summary>
    private static readonly string[] Verbs =
    [
        "DELETE ", "GET ", "HEAD ", "OPTIONS ", "PATCH ", "POST ", "PUT ",
    ];

    /// <summary>
    /// The controllers a server scanning this assembly would add to its route table.
    /// </summary>
    /// <returns>One entry per controller, ordered so a failure reads the same twice.</returns>
    internal static IReadOnlyList<Type> Controllers() => PluginUnderTest.Assembly
        .GetTypes()
        .Where(t => t.IsPublic && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// Every route these controllers claim, as the verb and the full template.
    /// </summary>
    /// <param name="controllers">The controllers to read.</param>
    /// <returns>The routes, ordered so a failure reads the same twice.</returns>
    internal static IReadOnlyList<string> RoutesOf(IReadOnlyList<Type> controllers) => Endpoints(controllers)
        .Select(endpoint => endpoint.Route)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(route => route, StringComparer.Ordinal)
        .ToList();

    /// <summary>
    /// The answers each route declares, as the status codes on its action.
    /// </summary>
    /// <param name="controllers">The controllers to read.</param>
    /// <returns>One entry per route, the codes ascending.</returns>
    /// <remarks>
    /// A route whose action declares nothing keeps its entry, with no codes in it. It
    /// is the shape an endpoint arrives in before somebody writes the attributes, and
    /// dropping it here would make the comparison in
    /// <see cref="WatchlistApiAnswerTests"/> silent about exactly that endpoint.
    /// </remarks>
    internal static IReadOnlyDictionary<string, IReadOnlyList<int>> AnswersOf(IReadOnlyList<Type> controllers) =>
        Endpoints(controllers)
            .GroupBy(endpoint => endpoint.Route, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<int>)group
                    .SelectMany(endpoint => endpoint.Answers)
                    .Distinct()
                    .OrderBy(code => code)
                    .ToList(),
                StringComparer.Ordinal);

    /// <summary>
    /// The document, read out of the test assembly rather than off disk, so what is
    /// compared is this tree's file and never a copy that happens to sit beside the
    /// test host.
    /// </summary>
    /// <returns>The document text.</returns>
    internal static string Document()
    {
        var assembly = Assembly.GetExecutingAssembly();

        using var stream = assembly.GetManifestResourceStream(DocumentResource)
            ?? throw new InvalidOperationException(
                DocumentResource + " is not embedded in the test assembly. The assembly carries: "
                + string.Join(", ", assembly.GetManifestResourceNames()));

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    /// <summary>
    /// The document's route sections, keyed by the route each heading names.
    /// </summary>
    /// <param name="document">The document to read.</param>
    /// <returns>The lines under each route heading, up to the next heading.</returns>
    /// <remarks>
    /// A section runs to the next heading of any level, so a route's own subheadings
    /// stay with it and the next route's do not. What makes a heading a route heading
    /// is the verb it opens with, which is the same test in both directions of every
    /// comparison against this document.
    /// </remarks>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> RouteSectionsIn(string document)
    {
        var sections = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var current = string.Empty;
        var body = new List<string>();

        foreach (var line in Lines(document))
        {
            if (!line.StartsWith("#", StringComparison.Ordinal))
            {
                body.Add(line);
                continue;
            }

            if (current.Length > 0)
            {
                sections[current] = body;
            }

            var heading = line.StartsWith("## ", StringComparison.Ordinal) ? line[3..].Trim() : string.Empty;
            current = Verbs.Any(verb => heading.StartsWith(verb, StringComparison.Ordinal))
                ? heading
                : string.Empty;
            body = new List<string>();
        }

        if (current.Length > 0)
        {
            sections[current] = body;
        }

        return sections;
    }

    /// <summary>
    /// The document's lines, however the file on the machine that wrote it ended them.
    /// </summary>
    /// <param name="document">The document to read.</param>
    /// <returns>One entry per line.</returns>
    internal static IReadOnlyList<string> Lines(string document) => document
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n');

    /// <summary>
    /// Every action these controllers expose, as the route it answers on and the codes
    /// it declares.
    /// </summary>
    /// <param name="controllers">The controllers to read.</param>
    /// <returns>One entry per verb and template pair.</returns>
    private static IReadOnlyList<(string Route, IReadOnlyList<int> Answers)> Endpoints(
        IReadOnlyList<Type> controllers) => controllers
        .SelectMany(controller => controller
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method
                .GetCustomAttributes(inherit: true)
                .OfType<HttpMethodAttribute>()
                .SelectMany(http => http.HttpMethods.Select(verb =>
                    (Route: verb + " " + Joined(PrefixOf(controller), http.Template),
                     Answers: AnswersDeclaredBy(method))))))
        .ToList();

    /// <summary>
    /// The status codes one action declares it can answer with.
    /// </summary>
    /// <param name="method">The action to read.</param>
    /// <returns>The codes, ascending.</returns>
    private static IReadOnlyList<int> AnswersDeclaredBy(MethodInfo method) => method
        .GetCustomAttributes(inherit: true)
        .OfType<ProducesResponseTypeAttribute>()
        .Select(declared => declared.StatusCode)
        .Distinct()
        .OrderBy(code => code)
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
}
