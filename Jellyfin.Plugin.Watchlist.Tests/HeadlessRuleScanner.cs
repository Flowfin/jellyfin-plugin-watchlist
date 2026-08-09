using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// One rule the headless guard refuses, read from HeadlessRules.txt.
/// </summary>
internal sealed record HeadlessRule(string Id, Regex Pattern, string Note);

/// <summary>
/// One place in a source file where a rule matched.
/// </summary>
internal sealed record HeadlessFinding(string File, int Line, string RuleId, string Note, string Text)
{
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0}:{1} [{2}] {3} ({4})",
        File,
        Line,
        RuleId,
        Note,
        Text);
}

/// <summary>
/// One declared departure, read from HEADLESS-EXCEPTIONS.txt.
/// </summary>
internal sealed record HeadlessException(string File, string RuleId, string Reason)
{
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0} | {1} | {2}",
        File,
        RuleId,
        Reason);
}

/// <summary>
/// Reads the rule table and the exception register, and scans source text against
/// them. Everything here is a pure function over strings, so the same code runs
/// over the suite's own sources and over a fixture, and a fixture proves what the
/// real scan would do.
/// </summary>
internal static class HeadlessRuleScanner
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(2);
    private static readonly char[] FieldSeparator = ['|'];

    public static IReadOnlyList<HeadlessRule> ParseRules(string table)
    {
        var rules = new List<HeadlessRule>();
        foreach (var fields in ContentLines(table))
        {
            if (fields.Length != 3)
            {
                throw new FormatException($"A rule line has {fields.Length} fields instead of three: {string.Join(" | ", fields)}");
            }

            rules.Add(new HeadlessRule(
                fields[0],
                new Regex(fields[1], RegexOptions.CultureInvariant, MatchTimeout),
                fields[2]));
        }

        return rules;
    }

    /// <summary>
    /// Reads the register. A line that is not three non-empty fields is reported
    /// rather than dropped, because a dispensation nobody can read is the thing
    /// this register exists to stop.
    /// </summary>
    public static (IReadOnlyList<HeadlessException> Entries, IReadOnlyList<string> Malformed) ParseExceptions(string register)
    {
        var entries = new List<HeadlessException>();
        var malformed = new List<string>();

        foreach (var fields in ContentLines(register))
        {
            if (fields.Length != 3 || fields.Any(string.IsNullOrEmpty))
            {
                malformed.Add(string.Join(" | ", fields));
                continue;
            }

            entries.Add(new HeadlessException(fields[0], fields[1], fields[2]));
        }

        return (entries, malformed);
    }

    public static IReadOnlyList<HeadlessFinding> Scan(string fileName, string source, IReadOnlyList<HeadlessRule> rules)
    {
        var findings = new List<HeadlessFinding>();
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var rule in rules)
            {
                var match = rule.Pattern.Match(lines[i]);
                if (match.Success)
                {
                    findings.Add(new HeadlessFinding(fileName, i + 1, rule.Id, rule.Note, match.Value.Trim()));
                }
            }
        }

        return findings;
    }

    /// <summary>
    /// Splits a finding set against the register. What comes back is what the guard
    /// fails on: findings no entry covers, and entries that covered nothing.
    /// </summary>
    public static (IReadOnlyList<HeadlessFinding> Undeclared, IReadOnlyList<HeadlessException> Stale) Apply(
        IReadOnlyList<HeadlessFinding> findings,
        IReadOnlyList<HeadlessException> exceptions)
    {
        bool Covers(HeadlessException e, HeadlessFinding f) =>
            string.Equals(e.File, f.File, StringComparison.Ordinal)
            && string.Equals(e.RuleId, f.RuleId, StringComparison.Ordinal);

        var undeclared = findings
            .Where(f => !exceptions.Any(e => Covers(e, f)))
            .ToList();

        var stale = exceptions
            .Where(e => !findings.Any(f => Covers(e, f)))
            .ToList();

        return (undeclared, stale);
    }

    private static IEnumerable<string[]> ContentLines(string text)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(raw => raw.Trim());

        foreach (var line in lines)
        {
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            yield return line.Split(FieldSeparator).Select(f => f.Trim()).ToArray();
        }
    }
}
