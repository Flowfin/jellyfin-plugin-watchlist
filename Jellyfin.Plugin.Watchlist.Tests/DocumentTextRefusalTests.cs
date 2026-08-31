using System.Text.Json;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What the two readers say when the text on disk parses and is not a document.
/// </summary>
/// <remarks>
/// <para>
/// `null` is valid JSON and deserialises to nothing, so without the refusal below a
/// file holding four characters becomes a null document handed to callers that all
/// dereference it. Both readers refuse it, and that much was covered.
/// </para>
/// <para>
/// WHAT IS PINNED HERE IS THE SENTENCE, and that is the part a mutation run found
/// missing: the message could be emptied and the suite stayed green. It is worth
/// pinning because it is the whole difference between two repairs for whoever meets it
/// in a log - a file that holds the literal null was written that way, and a file that
/// holds something else that is not an object was written by something other than this
/// plugin.
/// </para>
/// </remarks>
public sealed class DocumentTextRefusalTests
{
    /// <summary>
    /// A user's document that is the JSON literal null is refused, and the refusal says
    /// which of the two it met.
    /// </summary>
    [Fact]
    public void AUserDocumentThatIsTheLiteralNullIsRefusedBySentenceAndNotOnlyByThrowing()
    {
        var refusal = Assert.Throws<JsonException>(() => WatchlistDocumentFormat.Read("null"));

        Assert.Contains("JSON literal null", refusal.Message, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// And the shared list's reader says the same thing about the same text.
    /// </summary>
    [Fact]
    public void TheSharedListReaderRefusesTheLiteralNullTheSameWay()
    {
        var refusal = Assert.Throws<JsonException>(() => WatchlistDocumentFormat.ReadShared("null"));

        Assert.Contains("JSON literal null", refusal.Message, System.StringComparison.Ordinal);
    }
}
