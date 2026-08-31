using System;
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
/// The one list the whole server shares, projected into one playlist everybody can see:
/// which playlist, who may see it, what goes in it, and what a server that has no such
/// list costs.
/// </summary>
/// <remarks>
/// <para>
/// The difference calculation, the ordering, the series rule and the adoption are the
/// same ones a private list goes through, so they are not driven again here. What this
/// file holds is what is only true of the shared list: it is opened to everybody and
/// opened once, whose eyes decide what goes on it, who a client edit is attributed to,
/// what a removal from the playlist may and may not take off, and that a server with no
/// shared list is not touched at all.
/// </para>
/// <para>
/// Nothing here needs a server, a library or a file outside its own temporary directory,
/// and no test reads the machine clock.
/// </para>
/// </remarks>
public sealed class SharedProjectionTests : IDisposable
{
    private static readonly Guid TheOwner = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AUser = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static readonly Guid TheListId = Guid.Parse("dddddddd-0000-0000-0000-000000000009");

    private static readonly Guid AFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static readonly Guid AnotherFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static readonly Guid AThirdFilm = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");

    private static readonly Guid ASong = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");

    private static readonly Guid AShow = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    private static readonly Guid FirstEpisode = Guid.Parse("cccccccc-0000-0000-0000-000000000001");

    private static readonly DateTimeOffset WhenItWasAdded = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("watchlist-shared-projection");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// A server that has no shared list gets no shared playlist and costs no call. Both
    /// ways of having none: the setting is off, and the setting is on and nobody has made
    /// one.
    /// </summary>
    /// <remarks>
    /// This is the condition the assembly-reading guard in the administration suite used
    /// to stand in for, back when no shared target existed to build. One exists now, so
    /// the claim is made by counting calls instead.
    /// </remarks>
    [Fact]
    public async Task AServerWithNoSharedListIsNotTouched()
    {
        var store = AStore();
        var server = new APlaylistServerOf();

        await Pass(store, server, Off()).RunAsync(null, CancellationToken.None);
        Assert.Empty(server.Calls);

        await Pass(store, server, On()).RunAsync(null, CancellationToken.None);
        Assert.Empty(server.Calls);

        Assert.Null(store.ReadShared().Document);
    }

    /// <summary>
    /// And the list existing while the setting is off is still no playlist. Turning the
    /// switch off is how an administrator stops offering the list, and a projection that
    /// carried on would leave it on every client.
    /// </summary>
    [Fact]
    public async Task ASharedListWhoseSwitchIsOffIsNotProjected()
    {
        var store = AStore();
        MakeTheList(store);
        AddShared(store, AFilm);

        var server = new APlaylistServerOf();
        await Pass(store, server, Off()).RunAsync(null, CancellationToken.None);

        Assert.Empty(server.Calls);
        Assert.Null(store.ReadShared().Document!.Projection);
    }

    /// <summary>
    /// One playlist, owned by the administrator the record names, holding the list, and
    /// its identifier written into that record.
    /// </summary>
    [Fact]
    public async Task TheSharedListGetsExactlyOnePlaylistHeldInItsOwnRecord()
    {
        var store = AStore();
        MakeTheList(store);
        AddShared(store, AFilm);
        AddShared(store, AnotherFilm);

        var server = new APlaylistServerOf();
        await Pass(store, server, On()).RunAsync(null, CancellationToken.None);

        var projection = store.ReadShared().Document!.Projection;

        Assert.NotNull(projection);
        Assert.Equal(1, server.Creations);
        Assert.Contains(
            server.Calls,
            call => call.StartsWith("create " + TheOwner, StringComparison.Ordinal));
        Assert.Equal(
            new[] { AFilm, AnotherFilm },
            RowsOf(server, projection.PlaylistId).OrderBy(id => id).ToArray());
    }

    /// <summary>
    /// It is opened to everybody, and it is opened ONCE. The second pass asks whether it
    /// is already open and writes nothing, which is what makes a scheduled run over a
    /// correct server free.
    /// </summary>
    [Fact]
    public async Task ThePlaylistIsOpenedToEveryoneAndOnlyOnce()
    {
        var store = AStore();
        MakeTheList(store);
        AddShared(store, AFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server, On());

        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.ReadShared().Document!.Projection!.PlaylistId;

        Assert.True(server.IsOpenToEveryone(playlistId, TheOwner));
        Assert.Equal(1, server.Calls.Count(call => call.StartsWith("open ", StringComparison.Ordinal)));

        var afterTheFirst = server.Writes;
        var second = await pass.RunAsync(null, CancellationToken.None);

        Assert.Equal(1, server.Calls.Count(call => call.StartsWith("open ", StringComparison.Ordinal)));
        Assert.Equal(afterTheFirst, server.Writes);
        Assert.Equal(0, second.Writes);
    }

    /// <summary>
    /// A private playlist is never opened. The two lists are different targets and the
    /// pass acts on what each says about itself rather than deciding it, so a user's own
    /// list cannot be caught by the rule the shared one carries.
    /// </summary>
    [Fact]
    public async Task APrivatePlaylistIsNeverOpenedToEveryone()
    {
        var store = AStore();
        AddPrivate(store, AUser, AFilm);

        var server = new APlaylistServerOf();
        await Pass(store, server, On()).RunAsync(null, CancellationToken.None);

        var playlistId = store.Read(AUser).Document!.Projection!.PlaylistId;

        Assert.False(server.IsOpenToEveryone(playlistId, AUser));
        Assert.DoesNotContain(server.Calls, call => call.StartsWith("open ", StringComparison.Ordinal));
    }

    /// <summary>
    /// THE OWNER'S EYES DECIDE. An item the administrator whose list it is cannot resolve
    /// is left off the playlist, and that is the same gate the private list goes through
    /// asked about a different person.
    /// </summary>
    [Fact]
    public async Task AnItemTheOwnerCannotSeeIsLeftOffTheSharedPlaylist()
    {
        var store = AStore();
        MakeTheList(store);
        AddShared(store, AFilm);
        AddShared(store, AnotherFilm);

        var server = new APlaylistServerOf();

        // The library answers for this administrator about one of the two.
        await Pass(store, server, On(), new ADescriberOf((AFilm, TheOwner, WatchlistItemKind.Movie)))
            .RunAsync(null, CancellationToken.None);

        var playlistId = store.ReadShared().Document!.Projection!.PlaylistId;

        Assert.Equal(new[] { AFilm }, RowsOf(server, playlistId));
        Assert.Equal(2, store.ReadShared().Document!.Entries.Count);
    }

    /// <summary>
    /// And whether some OTHER user can see it changes nothing, which is the decision this
    /// target is built on rather than an accident of the fake. A playlist holds the same
    /// rows for everybody who can see it; a user who cannot open one is refused by the
    /// server when they play it.
    /// </summary>
    [Fact]
    public async Task WhatOtherUsersCanSeeDoesNotChangeWhatIsOnIt()
    {
        var store = AStore();
        MakeTheList(store);
        AddShared(store, AFilm);

        var server = new APlaylistServerOf();

        // Nothing resolves for this other user at all, and the row is there anyway.
        var describer = new ADescriberOf((AFilm, TheOwner, WatchlistItemKind.Movie));

        await Pass(store, server, On(), describer).RunAsync(null, CancellationToken.None);

        var playlistId = store.ReadShared().Document!.Projection!.PlaylistId;

        Assert.Equal(new[] { AFilm }, RowsOf(server, playlistId));
        Assert.Null(describer.Describe(AFilm, AUser));
    }

    /// <summary>
    /// The series rule is the same one, unchanged: a show on the shared list is one
    /// episode and not every episode of it.
    /// </summary>
    [Fact]
    public async Task AShowOnTheSharedListIsOneEpisode()
    {
        var store = AStore();
        MakeTheList(store);
        AddShared(store, AShow, WatchlistItemKind.Series);

        var server = new APlaylistServerOf();
        var library = new ASeriesLibraryOf().Holding(AShow, TheOwner, new SeriesEpisode
        {
            ItemId = FirstEpisode,
            SeasonNumber = 1,
            EpisodeNumber = 1,
            IsPlayed = false,
        });

        await Pass(store, server, On(), library: library).RunAsync(null, CancellationToken.None);

        var playlistId = store.ReadShared().Document!.Projection!.PlaylistId;

        Assert.Equal(new[] { FirstEpisode }, RowsOf(server, playlistId));
    }

    /// <summary>
    /// A row somebody put in the shared playlist is taken onto the list, attributed to
    /// the owner. That is a reading of who could have made the edit: the plugin gives
    /// nobody permission to edit this playlist, so the only person who can add a row to
    /// it is the user it belongs to.
    /// </summary>
    [Fact]
    public async Task ARowAddedToTheSharedPlaylistIsAdoptedAndAttributedToTheOwner()
    {
        var store = AStore();
        MakeTheList(store);
        AddShared(store, AFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server, On());
        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.ReadShared().Document!.Projection!.PlaylistId;
        server.Rows(playlistId, AFilm, AThirdFilm);

        await pass.RunAsync(null, CancellationToken.None);

        var adopted = store.ReadShared().Document!.Entries.Single(entry => entry.ItemId == AThirdFilm);

        Assert.Equal(WatchlistEntrySource.PlaylistEdit, adopted.Source);
        Assert.Equal(TheOwner, adopted.AddedBy);
    }

    /// <summary>
    /// A row of a kind a list does not hold is left in the playlist and never becomes an
    /// entry, exactly as on a private list.
    /// </summary>
    [Fact]
    public async Task ARowOfAKindTheSharedListDoesNotHoldIsNotAdopted()
    {
        var store = AStore();
        MakeTheList(store);
        AddShared(store, AFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server, On());
        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.ReadShared().Document!.Projection!.PlaylistId;
        server.Rows(playlistId, AFilm, ASong);

        await pass.RunAsync(null, CancellationToken.None);

        Assert.Equal(new[] { AFilm }, SharedEntriesOf(store));
    }

    /// <summary>
    /// A row the plugin wrote for an entry the OWNER added, taken out of the playlist,
    /// takes that entry off the list.
    /// </summary>
    [Fact]
    public async Task TakingTheOwnersOwnRowOutOfThePlaylistRemovesTheEntry()
    {
        var store = AStore();
        MakeTheList(store);
        AddShared(store, AFilm);
        AddShared(store, AnotherFilm);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server, On());
        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.ReadShared().Document!.Projection!.PlaylistId;
        server.Rows(playlistId, AnotherFilm);

        await pass.RunAsync(null, CancellationToken.None);

        Assert.Equal(new[] { AnotherFilm }, SharedEntriesOf(store));
    }

    /// <summary>
    /// SOMEBODY ELSE'S ENTRY IS NOT TAKEN OFF BY A PLAYLIST EDIT, AND THAT IS NARROWER
    /// THAN THE DECISION ALLOWS. Answer 7 on #1 would let an administrator remove any
    /// entry; this route does not use that, because a playlist is not an authorisation
    /// surface and the server answers no question about who edited it. The endpoints are
    /// where an administrator removes somebody else's entry.
    /// </summary>
    /// <remarks>
    /// The visible cost is asserted rather than only written down: the row comes back on
    /// the next pass, because the entry is still on the list.
    /// </remarks>
    [Fact]
    public async Task TakingSomebodyElsesRowOutOfThePlaylistDoesNotRemoveTheirEntry()
    {
        var store = AStore();
        MakeTheList(store);
        AddShared(store, AFilm, WatchlistItemKind.Movie, addedBy: AUser);

        var server = new APlaylistServerOf();
        var pass = Pass(store, server, On());
        await pass.RunAsync(null, CancellationToken.None);

        var playlistId = store.ReadShared().Document!.Projection!.PlaylistId;
        server.Rows(playlistId);

        await pass.RunAsync(null, CancellationToken.None);

        Assert.Equal(new[] { AFilm }, SharedEntriesOf(store));
        Assert.Equal(new[] { AFilm }, RowsOf(server, playlistId));
    }

    /// <summary>
    /// A shared record this build cannot read is a target whose record is unavailable
    /// rather than a server with no list: nothing is created, and the run counts it as
    /// stepped over.
    /// </summary>
    [Fact]
    public async Task ASharedRecordThisBuildCannotReadMakesNoPlaylist()
    {
        var store = AStore();
        MakeTheList(store);
        AddShared(store, AFilm);
        FromTheFuture(store);

        var server = new APlaylistServerOf();
        var run = await Pass(store, server, On()).RunAsync(null, CancellationToken.None);

        Assert.Empty(server.Calls);
        Assert.Equal(1, run.Skipped);
    }

    /// <summary>
    /// A server with no shared list at all costs no step-over either, which is what
    /// separates it from one whose record cannot be read. The two look identical to a
    /// caller asking whether the read was available, and only one of them is a server
    /// with nothing to do.
    /// </summary>
    [Fact]
    public async Task AServerWithNoSharedListIsNotEvenSteppedOver()
    {
        var run = await Pass(AStore(), new APlaylistServerOf(), On()).RunAsync(null, CancellationToken.None);

        Assert.Equal(0, run.Skipped);
        Assert.Equal(0, run.Created);
    }

    /// <summary>
    /// An entry of a kind a list does not hold makes no row on the shared playlist
    /// either. Nothing the endpoints accept can put one on the list, so what this covers
    /// is a record written by something that is not this plugin.
    /// </summary>
    [Fact]
    public async Task AnEntryOfAKindTheListDoesNotHoldIsNoRowOnTheSharedPlaylist()
    {
        var store = AStore();
        MakeTheList(store);
        AddShared(store, ASong, WatchlistItemKind.Other);
        AddShared(store, AFilm);

        var server = new APlaylistServerOf();
        await Pass(store, server, On()).RunAsync(null, CancellationToken.None);

        var playlistId = store.ReadShared().Document!.Projection!.PlaylistId;

        Assert.Equal(new[] { AFilm }, RowsOf(server, playlistId));
    }

    /// <summary>
    /// Recording a projection against a record that is not there is refused rather than
    /// creating one. A server with no shared list would otherwise gain one the first time
    /// a pass tried to remember a playlist for it.
    /// </summary>
    [Fact]
    public void RememberingAPlaylistForAListThatIsNotThereIsRefused()
    {
        var store = AStore();

        Assert.False(store.SetSharedProjection(new WatchlistProjectionState
        {
            PlaylistId = Guid.Parse("dddddddd-0000-0000-0000-000000000001"),
            LastNameWritten = "Shared Watchlist (plugin)",
            ProjectedItemIds = [],
            WrittenAt = null,
        }));

        Assert.Null(store.ReadShared().Document);
        Assert.False(store.ReadShared().Exists);
    }

    /// <summary>
    /// Both lists in one run: the shared one and a user's own each get their own
    /// playlist, and the two hold their own entries.
    /// </summary>
    [Fact]
    public async Task OneRunProjectsTheSharedListAndAUsersOwnListSeparately()
    {
        var store = AStore();
        MakeTheList(store);
        AddShared(store, AFilm);
        AddPrivate(store, AUser, AnotherFilm);

        var server = new APlaylistServerOf();
        var run = await Pass(store, server, On()).RunAsync(null, CancellationToken.None);

        var shared = store.ReadShared().Document!.Projection!.PlaylistId;
        var mine = store.Read(AUser).Document!.Projection!.PlaylistId;

        Assert.NotEqual(shared, mine);
        Assert.Equal(2, run.Created);
        Assert.Equal(new[] { AFilm }, RowsOf(server, shared));
        Assert.Equal(new[] { AnotherFilm }, RowsOf(server, mine));
    }

    private static PluginConfiguration On() => new() { SharedListEnabled = true };

    private static PluginConfiguration Off() => new() { SharedListEnabled = false };

    private static Guid[] RowsOf(APlaylistServerOf server, Guid playlistId) =>
        server.EntriesOf(playlistId, TheOwner).Select(row => row.ItemId).ToArray();

    private static Guid[] SharedEntriesOf(WatchlistDocumentStore store) =>
        store.ReadShared().Document!.Entries.Select(entry => entry.ItemId).OrderBy(id => id).ToArray();

    private static void MakeTheList(WatchlistDocumentStore store) =>
        Assert.True(store.CreateShared(TheListId, TheOwner));

    private static void AddShared(
        WatchlistDocumentStore store,
        Guid itemId,
        WatchlistItemKind kind = WatchlistItemKind.Movie,
        Guid? addedBy = null)
    {
        var result = store.AddShared(
            new WatchlistEntry
            {
                ItemId = itemId,
                Kind = kind,
                AddedAt = WhenItWasAdded,
                Source = WatchlistEntrySource.Api,
                AddedBy = addedBy ?? TheOwner,
            },
            PluginConfiguration.DefaultMaxEntriesInSharedList);

        Assert.Equal(WatchlistAddOutcome.Added, result.Outcome);
    }

    private static void AddPrivate(WatchlistDocumentStore store, Guid userId, Guid itemId)
    {
        var result = store.Add(
            userId,
            new WatchlistEntry
            {
                ItemId = itemId,
                Kind = WatchlistItemKind.Movie,
                AddedAt = WhenItWasAdded,
                Source = WatchlistEntrySource.Api,
            },
            PluginConfiguration.DefaultMaxEntriesPerUser);

        Assert.Equal(WatchlistAddOutcome.Added, result.Outcome);
    }

    /// <summary>
    /// Puts the shared record out of this build's reach by declaring a version from the
    /// future in it, which is the one way a record becomes unavailable without the file
    /// system being interfered with.
    /// </summary>
    /// <param name="store">The store.</param>
    private static void FromTheFuture(WatchlistDocumentStore store)
    {
        var text = File.ReadAllText(store.SharedListPath);

        File.WriteAllText(
            store.SharedListPath,
            text.Replace(
                "\"SchemaVersion\": " + SharedWatchlistDocument.CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "\"SchemaVersion\": 9999",
                StringComparison.Ordinal));
    }

    private WatchlistDocumentStore AStore() => new(DataFolder, new RecordingLogger());

    private WatchlistProjectionPass Pass(
        WatchlistDocumentStore store,
        APlaylistServerOf server,
        PluginConfiguration configuration,
        ADescriberOf? describer = null,
        ASeriesLibraryOf? library = null) => new(
            store,
            new WatchlistProjector(server, new RecordingProjectorLogger()),
            new WatchlistReconciler(server, new RecordingReconcilerLogger()),
            server,
            describer ?? new ADescriberOf(
                (AFilm, TheOwner, WatchlistItemKind.Movie),
                (AnotherFilm, TheOwner, WatchlistItemKind.Movie),
                (AThirdFilm, TheOwner, WatchlistItemKind.Movie),
                (ASong, TheOwner, WatchlistItemKind.Other),
                (AShow, TheOwner, WatchlistItemKind.Series),
                (FirstEpisode, TheOwner, WatchlistItemKind.Episode),
                (AFilm, AUser, WatchlistItemKind.Movie),
                (AnotherFilm, AUser, WatchlistItemKind.Movie)),
            library ?? new ASeriesLibraryOf(),
            new StoppedClock(WhenItWasAdded),
            () => configuration,
            new RecordingPassLogger());
}
