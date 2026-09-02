using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// One block of a document written twice in a row, with the line each copy starts
/// on. Both lines are carried because a repair needs to know which copy to keep and
/// a reader with only one of the two has to go looking for the other.
/// </summary>
internal sealed record RepeatedBlock(string Document, int FirstLine, int SecondLine, string Text)
{
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0}: the block at line {1} is written again at line {2}: {3}",
        Document,
        FirstLine,
        SecondLine,
        Text);
}

/// <summary>
/// The scan behind <see cref="RepeatedParagraphTests"/>. It splits a document into
/// blocks on blank lines and reports a block that is equal to the block immediately
/// before it once whitespace is normalised.
/// </summary>
/// <remarks>
/// <para>
/// ADJACENCY IS THE WHOLE OF WHAT MAKES THIS REFUSABLE. A block repeated with text
/// between the copies is a shape two documents in this repository are deliberately
/// built out of - one section per endpoint in <c>docs/api.md</c> and one per setting
/// in <c>docs/settings.md</c>, each carrying the same clause - so a rule over that
/// case would refuse how they are written. Two copies with nothing between them have
/// no such reading, which is why this scan compares each block with its predecessor
/// and with nothing else.
/// </para>
/// <para>
/// Whitespace is normalised because the copy that arrives is rarely wrapped the way
/// the original was. A block is joined onto one line, runs of whitespace become one
/// space, and the ends are trimmed, so a paragraph re-wrapped on the way in is still
/// the same block. Nothing else is normalised: case, punctuation and every character
/// that is not whitespace are compared as they stand.
/// </para>
/// </remarks>
internal static class RepeatedParagraphScanner
{
    /// <summary>
    /// Reads one document and reports every block that repeats the one before it.
    /// </summary>
    /// <param name="document">The name the finding is reported under.</param>
    /// <param name="text">The document's text.</param>
    /// <returns>One entry per repeat, in the order they appear.</returns>
    internal static IReadOnlyList<RepeatedBlock> Scan(string document, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var findings = new List<RepeatedBlock>();
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var block = new List<string>();
        var blockStart = 0;
        var previousText = string.Empty;
        var previousStart = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0)
            {
                Close();
                continue;
            }

            if (block.Count == 0)
            {
                blockStart = i + 1;
            }

            block.Add(lines[i]);
        }

        Close();

        return findings;

        void Close()
        {
            if (block.Count == 0)
            {
                return;
            }

            var normalised = Normalise(block);

            if (normalised.Length > 0 && string.Equals(normalised, previousText, StringComparison.Ordinal))
            {
                findings.Add(new RepeatedBlock(document, previousStart, blockStart, normalised));
            }

            previousText = normalised;
            previousStart = blockStart;
            block.Clear();
        }
    }

    /// <summary>
    /// One block as the comparison sees it: the lines joined, runs of whitespace
    /// collapsed to a single space, and the ends trimmed.
    /// </summary>
    /// <param name="block">The lines of one block.</param>
    /// <returns>The text the comparison is made over.</returns>
    internal static string Normalise(IReadOnlyList<string> block)
    {
        ArgumentNullException.ThrowIfNull(block);

        var normalised = new StringBuilder();
        var pendingSpace = false;

        foreach (var line in block)
        {
            foreach (var character in line)
            {
                if (char.IsWhiteSpace(character))
                {
                    pendingSpace = normalised.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    normalised.Append(' ');
                    pendingSpace = false;
                }

                normalised.Append(character);
            }

            pendingSpace = normalised.Length > 0;
        }

        return normalised.ToString();
    }
}
