using System;
using System.IO;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The settings that are a user's own: where they are kept, what happens when nobody
/// has answered, and which value wins when the user and the server both have.
/// </summary>
/// <remarks>
/// Every test here owns a directory of its own and deletes it afterwards. Nothing
/// reads a shared temporary path, a machine-wide path or the clock.
/// </remarks>
public sealed class PerUserSettingTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly Guid TheFixtureUser = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly TemporaryDirectory _sandbox = new("watchlist-preferences");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// No override. The server's answer is what applies, in both directions, so this
    /// fails on a rule that returns a constant as well as on one that ignores the
    /// server. A block holding no answer is the same case as no block.
    /// </summary>
    /// <param name="serverWide">What the server answers.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithNoAnswerFromTheUserTheServerWideValueApplies(bool serverWide)
    {
        var configuration = new PluginConfiguration
        {
            ProjectionEnabled = serverWide,
            RemoveWhenWatched = serverWide,
        };

        Assert.Equal(serverWide, EffectiveSettings.ProjectionEnabled(configuration, null));
        Assert.Equal(serverWide, EffectiveSettings.RemoveWhenWatched(configuration, null));
        Assert.Equal(serverWide, EffectiveSettings.ProjectionEnabled(configuration, new WatchlistUserPreferences()));
        Assert.Equal(serverWide, EffectiveSettings.RemoveWhenWatched(configuration, new WatchlistUserPreferences()));
    }

    /// <summary>
    /// An override on and an override off, each against a server answering the
    /// opposite, which is the only arrangement in which the user's value can be seen
    /// to have won.
    /// </summary>
    /// <param name="perUser">What the user answered.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnAnswerFromTheUserBeatsTheServerWideValue(bool perUser)
    {
        var configuration = new PluginConfiguration
        {
            ProjectionEnabled = !perUser,
            RemoveWhenWatched = !perUser,
        };

        var preferences = new WatchlistUserPreferences
        {
            ProjectionEnabled = perUser,
            RemoveWhenWatched = perUser,
        };

        Assert.Equal(perUser, EffectiveSettings.ProjectionEnabled(configuration, preferences));
        Assert.Equal(perUser, EffectiveSettings.RemoveWhenWatched(configuration, preferences));
    }

    /// <summary>
    /// A user's answer that happens to equal the server's is still their answer, and it
    /// stays theirs when the server's moves. This is the half of the rule that decides
    /// behaviour rather than storage: collapsing an equal answer into an absence would
    /// let one person's edit change another person's setting.
    /// </summary>
    [Fact]
    public void AnAnswerThatMatchesTheServerTodayStillWinsWhenTheServerMoves()
    {
        var configuration = new PluginConfiguration { RemoveWhenWatched = true };
        var preferences = new WatchlistUserPreferences { RemoveWhenWatched = true };

        Assert.True(EffectiveSettings.RemoveWhenWatched(configuration, preferences));

        configuration.RemoveWhenWatched = false;

        Assert.True(EffectiveSettings.RemoveWhenWatched(configuration, preferences));
    }

    /// <summary>
    /// One answer is one answer. A user who answered one of the two settings does not
    /// thereby answer the other, which is the case a block read as a whole would get
    /// wrong.
    /// </summary>
    [Fact]
    public void AnsweringOneSettingLeavesTheOtherWithTheServer()
    {
        var configuration = new PluginConfiguration { ProjectionEnabled = true, RemoveWhenWatched = true };
        var preferences = new WatchlistUserPreferences { ProjectionEnabled = false };

        Assert.False(EffectiveSettings.ProjectionEnabled(configuration, preferences));
        Assert.True(EffectiveSettings.RemoveWhenWatched(configuration, preferences));
    }

    /// <summary>
    /// A user whose document does not exist yet. The read a caller would make returns
    /// an empty document with no block, so the same rule answers for them without
    /// anything having been provisioned and without a file being created.
    /// </summary>
    [Fact]
    public void AUserWithNoDocumentGetsTheServerWideValue()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        var configuration = new PluginConfiguration { ProjectionEnabled = false, RemoveWhenWatched = true };

        var read = store.Read(AUser);

        Assert.True(read.IsAvailable);
        Assert.Null(read.Document!.Preferences);
        Assert.False(EffectiveSettings.ProjectionEnabled(configuration, read.Document.Preferences));
        Assert.True(EffectiveSettings.RemoveWhenWatched(configuration, read.Document.Preferences));
        Assert.False(File.Exists(store.PathFor(AUser)));
    }

    /// <summary>
    /// A user who never set anything has no block on disk at all, asserted on the bytes
    /// rather than on the object, because the object is what a later read would produce
    /// either way.
    /// </summary>
    [Fact]
    public void AUserWhoNeverSetAnythingHasNoBlockOnDisk()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Write(WatchlistDocumentStore.Empty(AUser));

        var text = File.ReadAllText(store.PathFor(AUser));

        Assert.DoesNotContain(nameof(WatchlistDocument.Preferences), text, StringComparison.Ordinal);
        Assert.DoesNotContain("null", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The answer goes into that user's own document, beside their entries, under the
    /// same schema version, and comes back out of a fresh store rather than out of the
    /// object that wrote it.
    /// </summary>
    [Fact]
    public void AnAnswerIsStoredWithTheUsersEntriesAndReadsBack()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(AUser, AnEntry(), maxEntriesPerUser: 10);

        Assert.True(store.SetPreferences(AUser, new WatchlistUserPreferences { RemoveWhenWatched = true }));

        var read = new WatchlistDocumentStore(DataFolder).Read(AUser);

        Assert.True(read.IsAvailable);
        Assert.Equal(WatchlistDocument.CurrentSchemaVersion, read.Document!.SchemaVersion);
        Assert.Single(read.Document.Entries);
        Assert.True(read.Document.Preferences!.RemoveWhenWatched);
        Assert.Null(read.Document.Preferences.ProjectionEnabled);
    }

    /// <summary>
    /// Withdrawing the last answer leaves the document where it was before the first
    /// rather than leaving an empty block behind. Both spellings of withdrawal reach
    /// the same state, because a block holding no answer says what no block says.
    /// </summary>
    /// <param name="withAnEmptyBlock">Whether the withdrawal is an empty block or nothing.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithdrawingTheLastAnswerLeavesNoBlockOnDisk(bool withAnEmptyBlock)
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.SetPreferences(AUser, new WatchlistUserPreferences { ProjectionEnabled = false });

        Assert.Contains(
            nameof(WatchlistDocument.Preferences),
            File.ReadAllText(store.PathFor(AUser)),
            StringComparison.Ordinal);

        store.SetPreferences(AUser, withAnEmptyBlock ? new WatchlistUserPreferences() : null);

        Assert.DoesNotContain(
            nameof(WatchlistDocument.Preferences),
            File.ReadAllText(store.PathFor(AUser)),
            StringComparison.Ordinal);
        Assert.Null(store.Read(AUser).Document!.Preferences);
    }

    /// <summary>
    /// A document this build cannot read is left alone by a preference save exactly as
    /// it is by a read. Writing here would replace a newer plugin's document with a
    /// shape this one understands, which is the entry loss the read path refuses.
    /// </summary>
    [Fact]
    public void ADocumentThisBuildCannotReadIsNotWrittenTo()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        Place(store.PathFor(AUser), "watchlist-document-from-the-future.json");
        var before = File.ReadAllBytes(store.PathFor(AUser));

        Assert.False(store.SetPreferences(AUser, new WatchlistUserPreferences { ProjectionEnabled = false }));

        Assert.Equal(before, File.ReadAllBytes(store.PathFor(AUser)));
    }

    /// <summary>
    /// A document written before version 2 reads as a user who answered nothing, which
    /// is what the step from version 1 says it is, with its entries intact.
    /// </summary>
    [Fact]
    public void ADocumentFromBeforeTheBlockReadsAsAUserWhoAnsweredNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        Place(store.PathFor(TheFixtureUser), "watchlist-document-v1.json");

        var read = store.Read(TheFixtureUser);

        Assert.True(read.IsAvailable);
        Assert.Equal(WatchlistDocument.CurrentSchemaVersion, read.Document!.SchemaVersion);
        Assert.Equal(3, read.Document.Entries.Count);
        Assert.Null(read.Document.Preferences);
    }

    /// <summary>
    /// The server's answer is required. A rule asked which value applies with no
    /// server-wide value has nothing to fall back to, and answering anyway would mean
    /// inventing a default in the one place that is supposed to hold none.
    /// </summary>
    [Fact]
    public void ThereIsNoAnswerWithoutAServerWideValue()
    {
        Assert.Throws<ArgumentNullException>(() => EffectiveSettings.ProjectionEnabled(null!, null));
        Assert.Throws<ArgumentNullException>(() => EffectiveSettings.RemoveWhenWatched(null!, null));
    }

    private static WatchlistEntry AnEntry() => new()
    {
        ItemId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000009"),
        Kind = WatchlistItemKind.Movie,
        AddedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        Source = WatchlistEntrySource.Api,
    };

    private static void Place(string path, string fixture)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, EmbeddedText("fixture/" + fixture));
    }

    private static string EmbeddedText(string resource)
    {
        using var stream = typeof(PerUserSettingTests).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("The suite is missing the embedded resource " + resource + ".");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
