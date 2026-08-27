using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What a pasted command turned into: either something this check can print, or a
/// reason it cannot. There is no third answer, and an unrunnable command is never
/// quietly treated as one that agreed with its paste.
/// </summary>
internal sealed record PastePlan(IReadOnlyList<PastedGrep.Stage>? Stages, string? EchoName, string? Refusal);

/// <summary>
/// Re-runs the `grep` and `git grep` commands a document pastes, over the tree the
/// suite carries as embedded resources rather than over a checkout on disk.
///
/// It is an EVALUATION of the command rather than a shell running it, and that is
/// the bound to read before trusting an agreement: what runs here is this file's
/// reading of the two programs, over the file set DOCUMENT-PASTES.md declares. The
/// suite may not reach a shell, a network or a real repository - the headless rule
/// next to this file says so - so a command is either one this reading covers or it
/// is reported as unjudged. DOCUMENT-PASTES.md carries the whole bound: which flags
/// are read, which regular-expression constructs are translated, and what a paste
/// has to look like to be seen at all.
/// </summary>
internal static class PastedGrep
{
    /// <summary>
    /// One stage of a pipeline. The first stage reads files out of the tree; every
    /// later one reads the lines the stage before it printed.
    /// </summary>
    internal sealed record Stage(
        StageKind Kind,
        Regex? Pattern,
        bool Invert,
        OutputMode Mode,
        bool LineNumbers,
        IReadOnlyList<string> Files,
        bool Prefix,
        bool Git,
        int Count);

    internal enum StageKind
    {
        Files,
        Filter,
        Head,
    }

    internal enum OutputMode
    {
        Lines,
        Count,
        FilesWithMatches,
    }

    private const string ExitSuffix = "echo \"exit=$?\"";
    private const string ReturnCodeSuffix = "echo \"rc=$?\"";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Reads a pasted command into a plan, or says why it will not be judged.
    /// </summary>
    public static PastePlan Plan(string command, IReadOnlyList<string> treePaths)
    {
        var segments = SplitTopLevel(command, ';');
        if (segments is null)
        {
            return Refuse("the command does not read as balanced quoting");
        }

        string? echoName = null;
        if (segments.Count == 2)
        {
            var tail = segments[1].Trim();
            if (string.Equals(tail, ExitSuffix, StringComparison.Ordinal))
            {
                echoName = "exit";
            }
            else if (string.Equals(tail, ReturnCodeSuffix, StringComparison.Ordinal))
            {
                echoName = "rc";
            }
            else
            {
                return Refuse("carries a second command after a semicolon that is not the declared exit-code echo");
            }
        }
        else if (segments.Count > 2)
        {
            return Refuse("carries more than one semicolon");
        }

        var pipeline = SplitTopLevel(segments[0], '|');
        if (pipeline is null)
        {
            return Refuse("the command does not read as balanced quoting");
        }

        var stages = new List<Stage>();
        for (var i = 0; i < pipeline.Count; i++)
        {
            var tokens = Tokenise(pipeline[i]);
            if (tokens is null)
            {
                return Refuse("uses shell expansion, a redirection or a construct this check does not read");
            }

            if (tokens.Count == 0)
            {
                return Refuse("has an empty pipeline stage");
            }

            var stage = PlanStage(tokens, first: i == 0, treePaths, out var refusal);
            if (stage is null)
            {
                return Refuse(refusal!);
            }

            stages.Add(stage);
        }

        return new PastePlan(stages, echoName, null);
    }

    /// <summary>
    /// Runs a plan over the tree and returns the lines it prints, in order.
    /// </summary>
    public static IReadOnlyList<string> Run(PastePlan plan, IReadOnlyDictionary<string, string> tree)
    {
        var stages = plan.Stages ?? throw new InvalidOperationException("A refused plan has nothing to run.");
        IReadOnlyList<string> printed = [];
        var selected = false;

        foreach (var stage in stages)
        {
            switch (stage.Kind)
            {
                case StageKind.Files:
                    (printed, selected) = RunOverFiles(stage, tree);
                    break;
                case StageKind.Filter:
                    (printed, selected) = RunOverLines(stage, printed);
                    break;
                default:
                    printed = printed.Take(stage.Count).ToList();
                    selected = true;
                    break;
            }
        }

        if (plan.EchoName is null)
        {
            return printed;
        }

        return
        [
            .. printed,
            string.Format(CultureInfo.InvariantCulture, "{0}={1}", plan.EchoName, selected ? 0 : 1),
        ];
    }

    /// <summary>
    /// Translates a POSIX basic or extended regular expression into the dialect this
    /// runtime matches with, or says it cannot. A construct that means one thing to
    /// grep and another here is refused rather than translated approximately, which
    /// is the whole reason this returns a reason instead of a best effort.
    /// </summary>
    public static string? Translate(string pattern, bool extended, out string? refusal)
    {
        var translated = new StringBuilder();
        refusal = null;

        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];

            if (c == '\\')
            {
                if (i + 1 >= pattern.Length)
                {
                    refusal = "the pattern ends in a backslash";
                    return null;
                }

                var next = pattern[++i];

                if (next is '<' or '>')
                {
                    translated.Append("\\b");
                }
                else if (!extended && next is '(' or ')' or '{' or '}' or '|' or '+' or '?')
                {
                    translated.Append(next);
                }
                else if ("bBwWsSdD.*+?[]{}()|^$\\/-'\"".Contains(next, StringComparison.Ordinal))
                {
                    translated.Append('\\').Append(next);
                }
                else
                {
                    refusal = string.Format(CultureInfo.InvariantCulture, "the pattern escapes '{0}', which this check does not translate", next);
                    return null;
                }

                continue;
            }

            if (c == '[')
            {
                var bracket = ReadBracket(pattern, i);
                if (bracket is null)
                {
                    refusal = "the pattern has a bracket expression this check does not read";
                    return null;
                }

                translated.Append(bracket);
                i += bracket.Length - 1;
                continue;
            }

            if (!extended && c is '(' or ')' or '{' or '}' or '|' or '+' or '?')
            {
                translated.Append('\\').Append(c);
                continue;
            }

            translated.Append(c);
        }

        return translated.ToString();
    }

    private static PastePlan Refuse(string reason) => new(null, null, reason);

    private static Stage? PlanStage(IReadOnlyList<Token> tokens, bool first, IReadOnlyList<string> treePaths, out string? refusal)
    {
        refusal = null;
        var word = tokens[0];

        if (!word.Quoted && string.Equals(word.Text, "head", StringComparison.Ordinal))
        {
            if (first)
            {
                refusal = "starts with a command this check does not run";
                return null;
            }

            if (tokens.Count != 2 || !tokens[1].Text.StartsWith('-')
                || !int.TryParse(tokens[1].Text.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var take))
            {
                refusal = "uses head in a form this check does not read";
                return null;
            }

            return new Stage(StageKind.Head, null, false, OutputMode.Lines, false, [], false, false, take);
        }

        var git = false;
        var start = 0;

        if (!word.Quoted && string.Equals(word.Text, "git", StringComparison.Ordinal))
        {
            if (tokens.Count < 2 || !string.Equals(tokens[1].Text, "grep", StringComparison.Ordinal))
            {
                refusal = "starts with a git subcommand this check does not run";
                return null;
            }

            git = true;
            start = 2;
        }
        else if (!word.Quoted && string.Equals(word.Text, "grep", StringComparison.Ordinal))
        {
            start = 1;
        }
        else
        {
            refusal = string.Format(CultureInfo.InvariantCulture, "uses a command this check does not run: {0}", word.Text);
            return null;
        }

        var extended = false;
        var ignoreCase = false;
        var invert = false;
        var wordMatch = false;
        var lineNumbers = false;
        var mode = OutputMode.Lines;
        string? pattern = null;
        var operands = new List<string>();
        var beforeSeparator = new List<string>();
        var afterSeparator = false;

        for (var i = start; i < tokens.Count; i++)
        {
            var token = tokens[i];

            if (!token.Quoted && string.Equals(token.Text, "--", StringComparison.Ordinal))
            {
                if (!git)
                {
                    refusal = "uses an option separator this check reads only for git grep";
                    return null;
                }

                afterSeparator = true;
                continue;
            }

            if (!token.Quoted && !afterSeparator && token.Text.Length > 1 && token.Text[0] == '-')
            {
                foreach (var flag in token.Text.AsSpan(1))
                {
                    switch (flag)
                    {
                        case 'E': extended = true; break;
                        case 'i': ignoreCase = true; break;
                        case 'v': invert = true; break;
                        case 'w': wordMatch = true; break;
                        case 'n': lineNumbers = true; break;
                        case 'c': mode = OutputMode.Count; break;
                        case 'l': mode = OutputMode.FilesWithMatches; break;
                        case 'I': break;
                        default:
                            refusal = string.Format(CultureInfo.InvariantCulture, "uses the flag -{0}, which this check does not read", flag);
                            return null;
                    }
                }

                continue;
            }

            if (pattern is null)
            {
                pattern = token.Text;
                continue;
            }

            if (git && !afterSeparator)
            {
                beforeSeparator.Add(token.Text);
            }

            operands.Add(token.Text);
        }

        // git grep takes revisions between the pattern and the option separator. It
        // also takes a bare pathspec there when no separator is written, and this
        // repository's documents use both spellings, so which one an operand is has
        // to be decided rather than assumed. A path in the tree is a pathspec; a
        // name that reaches no file in it is read as a commit, and a reading of a
        // commit is outside what this check judges by its own declared bound.
        if (beforeSeparator.Count > 0 && (afterSeparator || !beforeSeparator.TrueForAll(o => Reaches(o, treePaths))))
        {
            refusal = "names a commit or a ref, so it is a reading of that commit rather than of this tree";
            return null;
        }

        if (pattern is null)
        {
            refusal = "names no pattern";
            return null;
        }

        var translated = Translate(pattern, extended, out var patternRefusal);
        if (translated is null)
        {
            refusal = patternRefusal;
            return null;
        }

        if (wordMatch)
        {
            translated = string.Concat("\\b(?:", translated, ")\\b");
        }

        var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        var regex = new Regex(translated, options, MatchTimeout);

        if (!first)
        {
            if (operands.Count > 0)
            {
                refusal = "names files in a stage that reads what the stage before it printed";
                return null;
            }

            return new Stage(StageKind.Filter, regex, invert, mode, lineNumbers, [], false, false, 0);
        }

        if (operands.Count == 0)
        {
            refusal = "names no file, so it reads standard input";
            return null;
        }

        var files = git ? Resolve(operands, treePaths, out refusal) : Name(operands, treePaths, out refusal);
        if (files is null)
        {
            return null;
        }

        return new Stage(StageKind.Files, regex, invert, mode, lineNumbers, files, git || files.Count > 1, git, 0);
    }

    /// <summary>
    /// The paths a git pathspec set reaches, in the order git walks its index, which
    /// is by path. A pathspec that reaches nothing is refused rather than read as an
    /// empty result: a path outside the file set this check carries and a path that
    /// is not in the repository look identical from here.
    /// </summary>
    private static bool Reaches(string spec, IReadOnlyList<string> treePaths)
    {
        var trimmed = spec.EndsWith('/') ? spec[..^1] : spec;
        var prefix = trimmed + "/";

        return string.Equals(trimmed, ".", StringComparison.Ordinal)
            || treePaths.Any(p => string.Equals(p, trimmed, StringComparison.Ordinal) || p.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static IReadOnlyList<string>? Resolve(IReadOnlyList<string> pathspecs, IReadOnlyList<string> treePaths, out string? refusal)
    {
        refusal = null;
        var reached = new List<string>();

        foreach (var spec in pathspecs)
        {
            var trimmed = spec.EndsWith('/') ? spec[..^1] : spec;
            var prefix = trimmed + "/";

            var matched = string.Equals(trimmed, ".", StringComparison.Ordinal)
                ? treePaths
                : treePaths
                    .Where(p => string.Equals(p, trimmed, StringComparison.Ordinal) || p.StartsWith(prefix, StringComparison.Ordinal))
                    .ToList();

            if (matched.Count == 0)
            {
                refusal = string.Format(
                    CultureInfo.InvariantCulture,
                    "names {0}, which is outside the file set this check carries",
                    spec);
                return null;
            }

            reached.AddRange(matched);
        }

        return reached.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The files a plain grep names, in the order it was given them. A directory is
    /// refused because grep without a recursive flag does not read one, and a path
    /// this check does not carry is refused for the reason above.
    /// </summary>
    private static IReadOnlyList<string>? Name(IReadOnlyList<string> operands, IReadOnlyList<string> treePaths, out string? refusal)
    {
        refusal = null;
        var named = new List<string>();

        foreach (var operand in operands)
        {
            if (!treePaths.Contains(operand, StringComparer.Ordinal))
            {
                refusal = string.Format(
                    CultureInfo.InvariantCulture,
                    "names {0}, which is outside the file set this check carries",
                    operand);
                return null;
            }

            named.Add(operand);
        }

        return named;
    }

    private static (IReadOnlyList<string> Printed, bool Selected) RunOverFiles(Stage stage, IReadOnlyDictionary<string, string> tree)
    {
        var printed = new List<string>();
        var selected = false;

        foreach (var file in stage.Files)
        {
            var lines = Lines(tree[file]);
            var hits = new List<(int Number, string Text)>();

            for (var i = 0; i < lines.Count; i++)
            {
                if (stage.Pattern!.IsMatch(lines[i]) != stage.Invert)
                {
                    hits.Add((i + 1, lines[i]));
                }
            }

            selected |= hits.Count > 0;

            switch (stage.Mode)
            {
                case OutputMode.FilesWithMatches:
                    if (hits.Count > 0)
                    {
                        printed.Add(file);
                    }

                    break;
                case OutputMode.Count:
                    // git grep -c names only the files it counted something in; plain
                    // grep -c answers for every file it was given, zero included.
                    if (stage.Git && hits.Count == 0)
                    {
                        break;
                    }

                    printed.Add(stage.Prefix
                        ? string.Format(CultureInfo.InvariantCulture, "{0}:{1}", file, hits.Count)
                        : hits.Count.ToString(CultureInfo.InvariantCulture));

                    break;
                default:
                    foreach (var hit in hits)
                    {
                        printed.Add(Render(stage, file, hit.Number, hit.Text));
                    }

                    break;
            }
        }

        return (printed, selected);
    }

    private static (IReadOnlyList<string> Printed, bool Selected) RunOverLines(Stage stage, IReadOnlyList<string> input)
    {
        var hits = new List<(int Number, string Text)>();

        for (var i = 0; i < input.Count; i++)
        {
            if (stage.Pattern!.IsMatch(input[i]) != stage.Invert)
            {
                hits.Add((i + 1, input[i]));
            }
        }

        if (stage.Mode == OutputMode.Count)
        {
            return ([hits.Count.ToString(CultureInfo.InvariantCulture)], hits.Count > 0);
        }

        var printed = hits
            .Select(hit => stage.LineNumbers
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1}", hit.Number, hit.Text)
                : hit.Text)
            .ToList();

        return (printed, hits.Count > 0);
    }

    private static string Render(Stage stage, string file, int number, string text)
    {
        var head = stage.Prefix ? file + ":" : string.Empty;
        var numbered = stage.LineNumbers
            ? string.Format(CultureInfo.InvariantCulture, "{0}:", number)
            : string.Empty;

        return string.Concat(head, numbered, text);
    }

    private static IReadOnlyList<string> Lines(string text)
    {
        var split = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        return split.Length > 0 && split[^1].Length == 0 ? split[..^1] : split;
    }

    private static string? ReadBracket(string pattern, int start)
    {
        if (pattern.AsSpan(start).StartsWith("[[:", StringComparison.Ordinal))
        {
            return null;
        }

        var i = start + 1;
        if (i < pattern.Length && pattern[i] == '^')
        {
            i++;
        }

        if (i < pattern.Length && pattern[i] == ']')
        {
            i++;
        }

        while (i < pattern.Length && pattern[i] != ']')
        {
            if (pattern[i] == '[' && i + 1 < pattern.Length && pattern[i + 1] == ':')
            {
                return null;
            }

            i++;
        }

        return i < pattern.Length ? pattern[start..(i + 1)] : null;
    }

    private static List<string>? SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quote = '\0';

        foreach (var c in text)
        {
            if (quote != '\0')
            {
                current.Append(c);
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                current.Append(c);
                continue;
            }

            if (c == separator)
            {
                parts.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        if (quote != '\0')
        {
            return null;
        }

        parts.Add(current.ToString());

        return parts;
    }

    private sealed record Token(string Text, bool Quoted);

    private static List<Token>? Tokenise(string text)
    {
        var tokens = new List<Token>();
        var current = new StringBuilder();
        var quoted = false;
        var open = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (c is '\'' or '"')
            {
                var end = text.IndexOf(c, i + 1);
                if (end < 0)
                {
                    return null;
                }

                var body = text[(i + 1)..end];
                if (c == '"' && (body.Contains('$', StringComparison.Ordinal) || body.Contains('`', StringComparison.Ordinal)))
                {
                    return null;
                }

                current.Append(body);
                quoted = true;
                open = true;
                i = end;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (open)
                {
                    tokens.Add(new Token(current.ToString(), quoted));
                    current.Clear();
                    quoted = false;
                    open = false;
                }

                continue;
            }

            if (c is '$' or '`' or '>' or '<' or '&' or '(' or ')' or '{' or '}' or '*' or '?' or '~' or '\\')
            {
                return null;
            }

            current.Append(c);
            open = true;
        }

        if (open)
        {
            tokens.Add(new Token(current.ToString(), quoted));
        }

        return tokens;
    }
}
