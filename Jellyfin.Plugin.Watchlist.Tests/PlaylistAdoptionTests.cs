using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Projection;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What happens on a first pass when the user already made a playlist called what this
/// plugin was going to call one.
/// </summary>
/// <remarks>
/// The workaround people use today is a hand-made list called something like
/// Watchlist. Creating a second one beside it is the worst of the available
/// behaviours: the user keeps adding to theirs, the plugin keeps writing to its own,
/// and the two never meet.
/// </remarks>
public sealed class PlaylistAdoptionTests : IDisposable
{
    private const string TheName = "Watchlist (plugin)";

    private static readonly Guid AUser = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private static readonly Guid AnotherUser = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static readonly Guid AFilm = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    private static readonly Guid AShow = Guid.Parse("eeeeeeee-0000-0000-0000-000000000002");

    private readonly TemporaryDirectory _sandbox = new("watchlist-adoption");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// One match. It is adopted rather than duplicated: the identifier is stored, the
    /// rows are read into the store, and nothing is created.
    /// </summary>
    [Fact]
    public async Task OneMatchingPlaylistIsAdoptedRatherThanDuplicated()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var theirs = Guid.Parse("ffffffff-0000-0000-0000-000000000001");
        server.AlreadyHolds(AUser, theirs, TheName);
        server.Rows(theirs, AFilm, AShow);

        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var result = await projector.EnsurePlaylistAsync(TargetOver(store, server), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Adopted, result.Outcome);
        Assert.Equal(theirs, result.Projection!.PlaylistId);
        Assert.Equal(0, server.Creations);
        Assert.Single(server.PlaylistsOf(AUser));
        Assert.Equal(theirs, store.Read(AUser).Document!.Projection!.PlaylistId);
    }

    /// <summary>
    /// And its rows are on the list afterwards, which is the half that makes adoption
    /// worth anything: the user's own list is what the plugin now manages.
    /// </summary>
    [Fact]
    public async Task TheRowsOfAnAdoptedPlaylistAreTakenOntoTheList()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var theirs = Guid.Parse("ffffffff-0000-0000-0000-000000000002");
        server.AlreadyHolds(AUser, theirs, TheName);
        server.Rows(theirs, AFilm, AShow);

        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        await projector.EnsurePlaylistAsync(TargetOver(store, server), CancellationToken.None);

        var entries = store.Read(AUser).Document!.Entries;
        Assert.Equal(2, entries.Count);
        Assert.Equal(new[] { AFilm, AShow }, entries.Select(entry => entry.ItemId).ToArray());
        Assert.All(entries, entry => Assert.Equal(WatchlistEntrySource.PlaylistEdit, entry.Source));
        Assert.Equal(WatchlistItemKind.Movie, entries[0].Kind);
        Assert.Equal(WatchlistItemKind.Series, entries[1].Kind);
    }

    /// <summary>
    /// A row this user may not see, or one of a kind a watchlist does not hold, is left
    /// off. Adoption is not a way past the rules an add through the endpoint meets.
    /// </summary>
    [Fact]
    public async Task ARowTheUserCannotSeeAndARowOfTheWrongKindAreLeftOff()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var theirs = Guid.Parse("ffffffff-0000-0000-0000-000000000003");
        var invisible = Guid.Parse("eeeeeeee-0000-0000-0000-000000000003");
        var aSong = Guid.Parse("eeeeeeee-0000-0000-0000-000000000004");
        server.AlreadyHolds(AUser, theirs, TheName);
        server.Rows(theirs, AFilm, invisible, aSong);

        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        await projector.EnsurePlaylistAsync(
            UserProjectionTarget.For(
                store,
                Configured(),
                new ADescriberOf(
                    (AFilm, AUser, WatchlistItemKind.Movie),
                    (aSong, AUser, WatchlistItemKind.Other)),
                AStoppedClock(),
                AUser),
            CancellationToken.None);

        var entries = store.Read(AUser).Document!.Entries;
        Assert.Equal(new[] { AFilm }, entries.Select(entry => entry.ItemId).ToArray());
    }

    /// <summary>
    /// Adoption is said once for that user, with the number of rows taken over and the
    /// number offered, and nothing out of the rows themselves.
    /// </summary>
    [Fact]
    public async Task AdoptionIsReportedOnceWithACountAndNoTitles()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var theirs = Guid.Parse("ffffffff-0000-0000-0000-000000000004");
        server.AlreadyHolds(AUser, theirs, TheName);
        server.Rows(theirs, AFilm, AShow);

        var log = new RecordingProjectorLogger();
        var projector = new WatchlistProjector(server, log);

        await projector.EnsurePlaylistAsync(TargetOver(store, server), CancellationToken.None);
        await projector.EnsurePlaylistAsync(TargetOver(store, server), CancellationToken.None);

        var line = Assert.Single(log.Lines, entry => entry.Contains("Adopted playlist", StringComparison.Ordinal));
        Assert.Contains("took 2 of its 2 rows", line, StringComparison.Ordinal);
        Assert.DoesNotContain("not read by the projection", line, StringComparison.Ordinal);
        Assert.DoesNotContain(TheName, line, StringComparison.Ordinal);
    }

    /// <summary>
    /// No match. Nothing is adopted and the plugin makes its own, which is every server
    /// where nobody made a list by hand.
    /// </summary>
    [Fact]
    public async Task NoMatchingPlaylistAdoptsNothingAndCreatesOne()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        server.AlreadyHolds(AUser, Guid.Parse("ffffffff-0000-0000-0000-000000000005"), "Saturday night");

        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var result = await projector.EnsurePlaylistAsync(TargetOver(store, server), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Created, result.Outcome);
        Assert.Equal(1, server.Creations);
        Assert.Empty(store.Read(AUser).Document!.Entries);
    }

    /// <summary>
    /// Two matches. None is adopted, the plugin makes its own, and it says why. There is
    /// nothing in either list that says which one the person meant, and guessing gets it
    /// wrong half the time on somebody else's data.
    /// </summary>
    [Fact]
    public async Task TwoMatchingPlaylistsAdoptNeitherAndSayWhy()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var first = Guid.Parse("ffffffff-0000-0000-0000-000000000006");
        var second = Guid.Parse("ffffffff-0000-0000-0000-000000000007");
        server.AlreadyHolds(AUser, first, TheName);
        server.Rows(first, AFilm);
        server.AlreadyHolds(AUser, second, TheName);
        server.Rows(second, AShow);

        var log = new RecordingProjectorLogger();
        var projector = new WatchlistProjector(server, log);

        var result = await projector.EnsurePlaylistAsync(TargetOver(store, server), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Created, result.Outcome);
        Assert.NotEqual(first, result.Projection!.PlaylistId);
        Assert.NotEqual(second, result.Projection.PlaylistId);
        Assert.Empty(store.Read(AUser).Document!.Entries);
        Assert.Contains(log.Lines, line => line.Contains("has 2 playlists carrying the configured list name", StringComparison.Ordinal));
    }

    /// <summary>
    /// A playlist owned by a different user is never a match, whatever it is called. The
    /// server is asked what THIS user owns, so somebody else's list cannot be taken over
    /// by naming it well.
    /// </summary>
    [Fact]
    public async Task APlaylistOwnedByAnotherUserIsNotAdopted()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var somebodyElses = Guid.Parse("ffffffff-0000-0000-0000-000000000008");
        server.AlreadyHolds(AnotherUser, somebodyElses, TheName);
        server.Rows(somebodyElses, AFilm);

        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var result = await projector.EnsurePlaylistAsync(TargetOver(store, server), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Created, result.Outcome);
        Assert.NotEqual(somebodyElses, result.Projection!.PlaylistId);
        Assert.Empty(store.Read(AUser).Document!.Entries);
    }

    /// <summary>
    /// Adoption happens on a first pass and nowhere else. A user whose projected playlist
    /// was deleted is not a first pass: the plugin makes a new one rather than taking
    /// over whatever now carries the name.
    /// </summary>
    [Fact]
    public async Task ATargetWhoseRememberedPlaylistIsGoneDoesNotAdopt()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var made = await projector.EnsurePlaylistAsync(TargetOver(store, server), CancellationToken.None);
        server.NoLongerHolds(AUser, made.Projection!.PlaylistId);

        var theirs = Guid.Parse("ffffffff-0000-0000-0000-000000000009");
        server.AlreadyHolds(AUser, theirs, TheName);
        server.Rows(theirs, AFilm);

        var again = await projector.EnsurePlaylistAsync(TargetOver(store, server), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Created, again.Outcome);
        Assert.NotEqual(theirs, again.Projection!.PlaylistId);
        Assert.Empty(store.Read(AUser).Document!.Entries);
    }

    /// <summary>
    /// An adopted playlist records the configured name as the name last written, so a
    /// later setting change renames it under the rule from #35 like any other. It matched
    /// that name at the moment it was adopted, which is what made it a match.
    /// </summary>
    [Fact]
    public async Task AnAdoptedPlaylistIsRenamedByALaterSettingChangeLikeAnyOther()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var theirs = Guid.Parse("ffffffff-0000-0000-0000-00000000000a");
        server.AlreadyHolds(AUser, theirs, TheName);

        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        await projector.EnsurePlaylistAsync(TargetOver(store, server), CancellationToken.None);

        Assert.Equal(TheName, store.Read(AUser).Document!.Projection!.LastNameWritten);

        var renamed = await projector.EnsurePlaylistAsync(
            TargetOver(store, server, "Films to watch"),
            CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Renamed, renamed.Outcome);
        Assert.Equal(theirs, renamed.Projection!.PlaylistId);
        Assert.Equal("Films to watch", server.PlaylistsOf(AUser)[0].Name);
    }

    /// <summary>
    /// A record that cannot be written adopts nothing. The record is written before the
    /// rows are read, so a failure leaves the playlist and the list exactly as they were
    /// rather than the entries taken and nothing pointing at where they came from.
    /// </summary>
    [Fact]
    public async Task AnAdoptionThatCannotBeRecordedTakesNoRows()
    {
        var server = new APlaylistServerOf();
        var theirs = Guid.Parse("ffffffff-0000-0000-0000-00000000000b");
        server.AlreadyHolds(AUser, theirs, TheName);
        server.Rows(theirs, AFilm);

        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());
        var target = new ATargetThatWillNotRemember(AUser, TheName);

        var result = await projector.EnsurePlaylistAsync(target, CancellationToken.None);

        Assert.Equal(ProjectionOutcome.RefusedRecordUnavailable, result.Outcome);
        Assert.Empty(target.Adopted);
        Assert.Equal(0, server.Creations);
        Assert.DoesNotContain(server.Calls, call => call.StartsWith("entries", StringComparison.Ordinal));
    }

    private static PluginConfiguration Configured(string name = TheName) =>
        new() { ProjectedListName = name };

    private static StoppedClock AStoppedClock() =>
        new(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    private UserProjectionTarget TargetOver(WatchlistDocumentStore store, APlaylistServerOf server, string name = TheName) =>
        UserProjectionTarget.For(
            store,
            Configured(name),
            new ADescriberOf(
                (AFilm, AUser, WatchlistItemKind.Movie),
                (AShow, AUser, WatchlistItemKind.Series)),
            AStoppedClock(),
            AUser);

    /// <summary>
    /// A target that already carries no projection and whose record cannot be written,
    /// so a pass over it reaches the adoption and refuses at the write.
    /// </summary>
    private sealed class ATargetThatWillNotRemember : IProjectionTarget
    {
        public ATargetThatWillNotRemember(Guid ownerUserId, string configuredName)
        {
            OwnerUserId = ownerUserId;
            ConfiguredName = configuredName;
        }

        public Guid OwnerUserId { get; }

        public string ConfiguredName { get; }

        public bool IsRecordAvailable => true;

        public WatchlistProjectionState? Remembered => null;

        public IReadOnlyList<Guid> Adopted { get; private set; } = [];

        public bool Remember(WatchlistProjectionState projection) => false;

        public int Adopt(IReadOnlyList<Guid> itemIds)
        {
            Adopted = itemIds;

            return itemIds.Count;
        }
    }
}
