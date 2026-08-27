using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Store;
using Jellyfin.Plugin.Watchlist.Watched;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What the watched subscription costs a server that is raising the event thousands of
/// times, which is what a library scan or a bulk mark-as-played does.
/// </summary>
/// <remarks>
/// <para>
/// The number these tests hold down is document writes, not events. A handler that
/// wrote once per event would pass every behaviour test in this suite and still put a
/// write on the disk for every item a scan touched, and nothing about the resulting
/// list would show it.
/// </para>
/// <para>
/// The instrument is the document file itself rather than a counter inside the store.
/// A write is a staged file put in place inside <c>Remove</c>, reached only where the
/// entry set actually shrinks, so every write changes the bytes at the path the store
/// gives for that user and no write leaves them alone. The count below is therefore a
/// reading of what reached the disk rather than a restatement of what the handler was
/// asked to do.
/// </para>
/// <para>
/// Every played item here is a movie. A movie names at most its own entry in
/// <see cref="WatchedRemoval.EntriesRetiredBy"/>, so one event retires at most one
/// entry and a sample taken after each event cannot miss a second write made inside
/// the same event. An episode can retire two, and a storm of those is a different
/// measurement rather than this one with another fixture.
/// </para>
/// </remarks>
public sealed class WatchedStormTests : IDisposable
{
    private const int EventsInTheStorm = 500;

    private static readonly Guid TheViewer = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("watchlist-watched-storm");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// Five titles on a list, five hundred plays, five writes. The bound is the length
    /// of the list and not the length of the storm, and the four hundred and ninety
    /// five plays of things nobody listed leave the document byte for byte alone.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AStormOfPlaysWritesOncePerEntryItRetiresRatherThanOncePerEvent()
    {
        var listed = Movies(5, "aaaaaaaa");
        var unlisted = Movies(EventsInTheStorm - listed.Count, "dddddddd");

        var store = new WatchlistDocumentStore(DataFolder);

        foreach (var itemId in listed)
        {
            store.Add(TheViewer, Entry(itemId), maxEntriesPerUser: 10000);
        }

        var userData = new AUserDataManagerOf();
        var subscription = new UserDataWatchedSubscription(userData, HandlerOver(store));
        await subscription.StartAsync(CancellationToken.None);

        var storm = Interleaved(unlisted, listed);

        Assert.Equal(EventsInTheStorm, storm.Count);

        var writes = new WritesTo(store.PathFor(TheViewer));

        foreach (var itemId in storm)
        {
            writes.Around(() => userData.Raise(Played(itemId)));
        }

        Assert.Equal(listed.Count, writes.Count);
        Assert.Equal(EventsInTheStorm - listed.Count, writes.EventsThatWroteNothing);
        Assert.Empty(EntriesOf(store, TheViewer));
    }

    /// <summary>
    /// The same title played five hundred times is one write. The first play takes the
    /// entry off, and every play after it finds nothing of that item to retire, so the
    /// storm that costs the most on a busy server costs the least here.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task PlayingOneListedTitleOverAndOverWritesOnce()
    {
        var theOne = Movies(1, "aaaaaaaa")[0];

        var store = new WatchlistDocumentStore(DataFolder);
        store.Add(TheViewer, Entry(theOne), maxEntriesPerUser: 10000);

        var userData = new AUserDataManagerOf();
        var subscription = new UserDataWatchedSubscription(userData, HandlerOver(store));
        await subscription.StartAsync(CancellationToken.None);

        var writes = new WritesTo(store.PathFor(TheViewer));

        for (var i = 0; i < EventsInTheStorm; i++)
        {
            writes.Around(() => userData.Raise(Played(theOne)));
        }

        Assert.Equal(1, writes.Count);
        Assert.Equal(EventsInTheStorm - 1, writes.EventsThatWroteNothing);
        Assert.Empty(EntriesOf(store, TheViewer));
    }

    /// <summary>
    /// A storm against a user who has never listed anything writes nothing at all and
    /// creates no document either. The absence of the file is the stronger half: a
    /// handler that wrote a list back for every user a scan touched would leave a
    /// document per user on a server where nobody uses this plugin.
    /// </summary>
    /// <returns>The running test.</returns>
    [Fact]
    public async Task AStormAgainstAUserWithNoListWritesNothingAndCreatesNoDocument()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        var userData = new AUserDataManagerOf();
        var subscription = new UserDataWatchedSubscription(userData, HandlerOver(store));
        await subscription.StartAsync(CancellationToken.None);

        var writes = new WritesTo(store.PathFor(TheViewer));

        foreach (var itemId in Movies(EventsInTheStorm, "dddddddd"))
        {
            writes.Around(() => userData.Raise(Played(itemId)));
        }

        Assert.Equal(0, writes.Count);
        Assert.Equal(EventsInTheStorm, writes.EventsThatWroteNothing);
        Assert.False(File.Exists(store.PathFor(TheViewer)));
    }

    private static List<Guid> Movies(int count, string prefix) =>
        Enumerable.Range(1, count)
            .Select(n => Guid.Parse(
                string.Format(CultureInfo.InvariantCulture, "{0}-0000-0000-0000-{1:D12}", prefix, n)))
            .ToList();

    /// <summary>
    /// The listed titles spread through the storm rather than gathered at one end, so
    /// a handler that stopped writing after the first few would not pass by accident.
    /// </summary>
    /// <param name="unlisted">The plays of items nobody listed.</param>
    /// <param name="listed">The plays that retire an entry.</param>
    /// <returns>The order the events are raised in.</returns>
    private static List<Guid> Interleaved(List<Guid> unlisted, List<Guid> listed)
    {
        var apart = unlisted.Count / listed.Count;
        var storm = new List<Guid>(unlisted);

        for (var i = 0; i < listed.Count; i++)
        {
            storm.Insert(Math.Min(((i + 1) * apart) + i, storm.Count), listed[i]);
        }

        return storm;
    }

    private static UserDataSaveEventArgs Played(Guid itemId) => new()
    {
        UserId = TheViewer,
        Item = new Movie { Id = itemId },
        UserData = new UserItemData { Key = "watched", Played = true },
        SaveReason = UserDataSaveReason.TogglePlayed,
    };

    private static WatchedRemovalHandler HandlerOver(WatchlistDocumentStore store) =>
        new(
            store,
            static () => new PluginConfiguration { RemoveWhenWatched = true },
            new AFinishedSeriesSet(),
            new RecordingWatchedLogger());

    private static WatchlistEntry Entry(Guid itemId) => new()
    {
        ItemId = itemId,
        Kind = WatchlistItemKind.Movie,
        AddedAt = WhenItWasAdded,
        Source = WatchlistEntrySource.Api,
    };

    private static IReadOnlyList<Guid> EntriesOf(WatchlistDocumentStore store, Guid userId) =>
        store.Read(userId).Document?.Entries.Select(entry => entry.ItemId).ToArray() ?? [];

    /// <summary>
    /// Counts the writes that reached one document, by stamping the file with a fixed
    /// time before each event and asking afterwards whether the stamp survived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Comparing the contents instead was tried first and it is weaker in the direction
    /// that matters: a handler that wrote the same list back on every event changed no
    /// bytes and was counted as writing nothing, while it had put five hundred writes on
    /// the disk. The stamp does not care what was written.
    /// </para>
    /// <para>
    /// A write here is a staged file moved onto the document, and the moved file carries
    /// its own timestamps, so any write replaces the stamp. The stamp is a constant
    /// rather than a clock read, which is what the headless rule beside this suite asks
    /// for, and it needs no resolution from the machine's clock because the test never
    /// compares two real times with each other.
    /// </para>
    /// </remarks>
    private sealed class WritesTo
    {
        private static readonly DateTime Stamp = new(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        private readonly string _path;

        public WritesTo(string path)
        {
            _path = path;
        }

        /// <summary>
        /// Gets how many events put a write on the disk.
        /// </summary>
        public int Count { get; private set; }

        /// <summary>
        /// Gets how many events left the document exactly as they found it.
        /// </summary>
        public int EventsThatWroteNothing { get; private set; }

        /// <summary>
        /// Stamps the document, raises one event, and reads whether the stamp survived.
        /// </summary>
        /// <param name="raiseOneEvent">The event to raise.</param>
        public void Around(Action raiseOneEvent)
        {
            var thereBefore = File.Exists(_path);

            if (thereBefore)
            {
                File.SetLastWriteTimeUtc(_path, Stamp);
            }

            raiseOneEvent();

            if (Written(thereBefore))
            {
                Count++;

                return;
            }

            EventsThatWroteNothing++;
        }

        private bool Written(bool thereBefore)
        {
            if (!File.Exists(_path))
            {
                return thereBefore;
            }

            return !thereBefore || File.GetLastWriteTimeUtc(_path) != Stamp;
        }
    }
}
