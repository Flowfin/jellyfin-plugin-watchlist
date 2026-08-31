using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// One place a source file takes the entries off a stored watchlist document.
/// </summary>
internal sealed record EntryRead(string File, int Line, string Text)
{
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0}:{1} reads a stored document's entries ({2})",
        File,
        Line,
        Text);
}

/// <summary>
/// What a declared reader is: one that hands every read to the gate, or one whose
/// subject is the stored document rather than the list somebody is shown.
/// </summary>
internal enum ReaderKind
{
    /// <summary>Every read the file takes is handed to the gate.</summary>
    Gated,

    /// <summary>The file's subject is the document rather than the list.</summary>
    Outside,
}

/// <summary>
/// One line of VISIBILITY-GATE-READERS.txt.
/// </summary>
internal sealed record DeclaredReader(string File, ReaderKind Kind, string Reason)
{
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0} | {1} | {2}",
        File,
        Kind == ReaderKind.Gated ? "gated" : "outside",
        Reason);
}

/// <summary>
/// Reads the register and finds, in a source text, the reads of a stored document's
/// entries and the calls to the one gate.
/// </summary>
/// <remarks>
/// <para>
/// Everything here is a pure function over strings, so the same code runs over the
/// plugin's real sources and over a fixture, and what a fixture proves is what the
/// real scan would do.
/// </para>
/// <para>
/// THE PATTERNS ARE IN THIS FILE AND THAT IS SAFE HERE, which is the opposite of the
/// choice HeadlessRules.txt and Invariants.txt make. Those tables are read against a
/// set of sources this file belongs to, so a .cs file holding their literals would be
/// matched by the scan reading it. This scan reads the PLUGIN's sources, under their
/// own resource prefix, and the suite's sources are not in that set, so no literal
/// here can be found by the scan it feeds.
/// </para>
/// <para>
/// WHAT THE TWO READ PATTERNS MATCH is the two spellings this tree uses: the entries
/// taken off the store's read result, whose member is capitalised, and the entries
/// taken off a local holding a document. Both are shapes of a name rather than of a
/// type, because the scan reads text and has no types. VISIBILITY-GATE-READERS.txt
/// carries what that leaves out.
/// </para>
/// </remarks>
internal static class VisibilityGateScanner
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);
    private static readonly char[] FieldSeparator = ['|'];

    /// <summary>
    /// The entries taken off the store's read result, as in a read whose document
    /// member is dereferenced or null-conditionally dereferenced.
    /// </summary>
    private static readonly Regex ReadOffAReadResult = new(
        @"\bDocument\s*\??\s*\.\s*Entries\b",
        RegexOptions.CultureInvariant,
        MatchTimeout);

    /// <summary>
    /// The entries taken off a local holding a document.
    /// </summary>
    private static readonly Regex ReadOffALocalDocument = new(
        @"\bdocument\s*\??\s*\.\s*Entries\b",
        RegexOptions.CultureInvariant,
        MatchTimeout);

    /// <summary>
    /// A call to the one gate. The call is written on one line in one reader and split
    /// over two in the other, so the member alone is what is counted rather than the
    /// type and the member together.
    /// </summary>
    private static readonly Regex GateCall = new(
        @"\.\s*Resolvable\s*\(",
        RegexOptions.CultureInvariant,
        MatchTimeout);

    /// <summary>
    /// An assignment. A read on a line that also assigns is one parked in a local, and a
    /// local can be read again past the gate.
    /// </summary>
    /// <remarks>
    /// The comparisons, the arrow and the compound assignments are excluded, so what is
    /// left is a value being given a name.
    /// </remarks>
    private static readonly Regex Assignment = new(
        @"(?<![=!<>+\-*/%&|^])=(?![=>])",
        RegexOptions.CultureInvariant,
        MatchTimeout);

    /// <summary>
    /// Every place this source takes the entries off a stored document.
    /// </summary>
    /// <param name="fileName">The name the finding is reported under.</param>
    /// <param name="source">The file's text.</param>
    /// <returns>One entry per line that takes a read, in line order.</returns>
    public static IReadOnlyList<EntryRead> Reads(string fileName, string source)
    {
        var reads = new List<EntryRead>();

        foreach (var (line, number) in Lines(source))
        {
            var match = ReadOffAReadResult.Match(line);

            if (!match.Success)
            {
                match = ReadOffALocalDocument.Match(line);
            }

            if (match.Success)
            {
                reads.Add(new EntryRead(fileName, number, match.Value.Trim()));
            }
        }

        return reads;
    }

    /// <summary>
    /// How many times this source calls the one gate.
    /// </summary>
    /// <param name="source">The file's text.</param>
    /// <returns>The count of lines carrying a call.</returns>
    public static int GateCalls(string source) =>
        Lines(source).Count(l => GateCall.IsMatch(l.Text));

    /// <summary>
    /// Every place this source parks a read in a local instead of handing it straight
    /// on.
    /// </summary>
    /// <param name="fileName">The name the finding is reported under.</param>
    /// <param name="source">The file's text.</param>
    /// <returns>One entry per read that is given a name, in line order.</returns>
    /// <remarks>
    /// This is the arm that catches a read the counting arm cannot see. A file that
    /// gates every read it takes still hands a caller the ungated collection if it names
    /// the read first and uses that name twice, and the counts stay equal while it does.
    /// </remarks>
    public static IReadOnlyList<EntryRead> Parked(string fileName, string source) =>
        Reads(fileName, source)
            .Where(read => Assignment.IsMatch(LineAt(source, read.Line)))
            .ToList();

    /// <summary>
    /// Reads the register. A line that is not three readable fields, or that names a
    /// kind this guard does not know, is reported rather than dropped: a declaration
    /// nobody can read is the thing the register exists against.
    /// </summary>
    /// <param name="register">The register's text.</param>
    /// <returns>The entries, and the lines that could not be read as one.</returns>
    public static (IReadOnlyList<DeclaredReader> Entries, IReadOnlyList<string> Malformed) ParseRegister(string register)
    {
        var entries = new List<DeclaredReader>();
        var malformed = new List<string>();

        foreach (var raw in Lines(register).Select(l => l.Text.Trim()))
        {
            if (raw.Length == 0 || raw.StartsWith('#'))
            {
                continue;
            }

            var fields = raw.Split(FieldSeparator).Select(f => f.Trim()).ToArray();

            if (fields.Length != 3 || fields.Any(string.IsNullOrEmpty))
            {
                malformed.Add(string.Join(" | ", fields));
                continue;
            }

            if (string.Equals(fields[1], "gated", StringComparison.Ordinal))
            {
                entries.Add(new DeclaredReader(fields[0], ReaderKind.Gated, fields[2]));
            }
            else if (string.Equals(fields[1], "outside", StringComparison.Ordinal))
            {
                entries.Add(new DeclaredReader(fields[0], ReaderKind.Outside, fields[2]));
            }
            else
            {
                malformed.Add(string.Join(" | ", fields));
            }
        }

        return (entries, malformed);
    }

    private static string LineAt(string text, int number) =>
        Lines(text).First(l => l.Number == number).Text;

    private static IEnumerable<(string Text, int Number)> Lines(string text) => text
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n')
        .Select((line, index) => (Text: line, Number: index + 1));
}
