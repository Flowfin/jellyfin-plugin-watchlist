using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// One command pasted into a tracked document, with the lines that stand under it.
/// The line is the document line the command starts on, which is what a failure
/// prints and what an entry in DOCUMENT-PASTE-EXCEPTIONS.txt names.
/// </summary>
internal sealed record DocumentPaste(string Document, int Line, string Command, IReadOnlyList<string> Output)
{
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0}:{1} {2}",
        Document,
        Line,
        Command);
}

/// <summary>
/// One paste this check did not judge, with the reason it did not. The reason is
/// the thing a reader needs: an unjudged paste is not a passing one, and a run that
/// says nothing about what it skipped reads exactly like one that read everything.
/// </summary>
internal sealed record UnjudgedPaste(DocumentPaste Paste, string Reason)
{
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0}:{1} not judged [{2}]: {3}",
        Paste.Document,
        Paste.Line,
        Reason,
        Paste.Command);
}

/// <summary>
/// One disagreement between what a pasted command prints and what stands under it.
/// </summary>
internal sealed record PasteMismatch(DocumentPaste Paste, IReadOnlyList<string> Printed)
{
    public override string ToString()
    {
        var indent = Environment.NewLine + "      ";
        var pasted = Paste.Output.Count == 0 ? "(nothing)" : string.Join(indent, Paste.Output);
        var printed = Printed.Count == 0 ? "(nothing)" : string.Join(indent, Printed);

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1}{2}  command: {3}{2}  pasted:{4}{2}  printed:{5}",
            Paste.Document,
            Paste.Line,
            Environment.NewLine,
            Paste.Command,
            indent + pasted,
            indent + printed);
    }
}

/// <summary>
/// One declared departure, read from DOCUMENT-PASTE-EXCEPTIONS.txt. The site is a
/// document and a line rather than a file alone, because a document holds many
/// pastes and a departure is about one of them.
/// </summary>
internal sealed record PasteException(string Document, int Line, string Reason)
{
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0} | {1} | {2}",
        Document,
        Line,
        Reason);
}

/// <summary>
/// Finds the pasted commands in a document and the lines that stand under them.
/// Everything here is a pure function over strings, so the same code runs over the
/// documents this repository ships and over a fixture, and a fixture proves what
/// the real scan would do.
///
/// What a paste has to look like to be seen at all is in DOCUMENT-PASTES.md next to
/// this file, and it is a bound rather than a formatting preference: a command
/// written in any other shape is outside this check rather than passing it.
/// </summary>
internal static class DocumentPasteScanner
{
    /// <summary>
    /// The indent a pasted block carries in this repository's documents. A command
    /// indented by less is prose and a command indented by more is inside another
    /// block, and neither is read here.
    /// </summary>
    public const int BlockIndent = 4;

    private static readonly char[] FieldSeparator = ['|'];

    /// <summary>
    /// The words a command line in this repository's documents starts with. A line
    /// under a pasted command that starts with one of them ends the output block
    /// rather than being read as output, because a block holding two commands is a
    /// shape these documents use. It is a list of what has been written rather than
    /// a guarantee: a command word nobody has used yet reads as output, which is a
    /// mismatch a reader sees rather than a silence.
    /// </summary>
    private static readonly string[] CommandWords =
    [
        "grep", "git", "gh", "go", "curl", "sed", "awk", "dotnet", "cat", "for",
        "while", "docker", "python", "python3", "ls", "echo", "jq", "head", "tail",
        "sort", "uniq", "cut", "wc", "find", "bash", "sh", "export", "printf",
        "diff", "xargs", "tr", "base64", "openssl", "node", "npm", "make", "set",
    ];

    public static IReadOnlyList<DocumentPaste> Find(string document, string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var pastes = new List<DocumentPaste>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsPastedCommand(lines[i]))
            {
                continue;
            }

            var first = i;
            var command = lines[i].Trim();

            while (command.EndsWith('\\') && i + 1 < lines.Length)
            {
                i++;
                command = string.Concat(command.AsSpan(0, command.Length - 1).TrimEnd(), " ", lines[i].Trim());
            }

            var output = new List<string>();
            var j = i + 1;

            while (j < lines.Length && IsBlockLine(lines[j]) && !StartsACommand(lines[j]))
            {
                output.Add(lines[j][BlockIndent..].TrimEnd());
                j++;
            }

            pastes.Add(new DocumentPaste(document, first + 1, command, output));
            i = j - 1;
        }

        return pastes;
    }

    /// <summary>
    /// Reads the register. A line that is not three readable fields, or whose line
    /// number is not a number, is reported rather than dropped: a dispensation
    /// nobody can read is what this register exists to stop.
    /// </summary>
    public static (IReadOnlyList<PasteException> Entries, IReadOnlyList<string> Malformed) ParseExceptions(string register)
    {
        var entries = new List<PasteException>();
        var malformed = new List<string>();

        foreach (var raw in register.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var fields = line.Split(FieldSeparator).Select(f => f.Trim()).ToArray();
            if (fields.Length != 3
                || Array.Exists(fields, string.IsNullOrEmpty)
                || !int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var at))
            {
                malformed.Add(string.Join(" | ", fields));
                continue;
            }

            entries.Add(new PasteException(fields[0], at, fields[2]));
        }

        return (entries, malformed);
    }

    /// <summary>
    /// Splits a mismatch set against the register. What comes back is what the guard
    /// fails on: mismatches no entry covers, and entries that name no paste in the
    /// population at all.
    /// </summary>
    public static (IReadOnlyList<PasteMismatch> Undeclared, IReadOnlyList<PasteException> Stale) Apply(
        IReadOnlyList<PasteMismatch> mismatches,
        IReadOnlyList<DocumentPaste> population,
        IReadOnlyList<PasteException> exceptions)
    {
        static bool Covers(PasteException e, DocumentPaste p) =>
            string.Equals(e.Document, p.Document, StringComparison.Ordinal) && e.Line == p.Line;

        var undeclared = mismatches
            .Where(m => !exceptions.Any(e => Covers(e, m.Paste)))
            .ToList();

        var stale = exceptions
            .Where(e => !population.Any(p => Covers(e, p)))
            .ToList();

        return (undeclared, stale);
    }

    private static bool IsBlockLine(string line) =>
        line.Length > BlockIndent
        && line.AsSpan(0, BlockIndent).IsWhiteSpace()
        && !char.IsWhiteSpace(line[BlockIndent]);

    private static bool IsPastedCommand(string line)
    {
        if (!IsBlockLine(line))
        {
            return false;
        }

        var text = line.Trim();

        return text.StartsWith("grep ", StringComparison.Ordinal)
            || text.StartsWith("git grep ", StringComparison.Ordinal);
    }

    private static bool StartsACommand(string line)
    {
        var word = line.Trim().Split(' ')[0];

        return Array.Exists(CommandWords, w => string.Equals(w, word, StringComparison.Ordinal));
    }
}
