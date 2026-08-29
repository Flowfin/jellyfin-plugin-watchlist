using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Projection;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What a changed list name does to a playlist that already exists, and the one case
/// where this plugin stops writing the name at all.
/// </summary>
/// <remarks>
/// The rule under test is one comparison: a playlist is renamed only where its label
/// is still the one this plugin last wrote. It is a comparison rather than an
/// intention because nothing here can ask a server whether a person typed a name.
/// </remarks>
public sealed class ProjectedListNameTests : IDisposable
{
    private const string TheOldName = "Watchlist (plugin)";

    private const string TheNewName = "Films to watch";

    private static readonly Guid AUser = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static readonly Guid TheFixtureUser = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly TemporaryDirectory _sandbox = new("watchlist-list-name");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The setting moves and the existing playlist is renamed. Nothing new is created,
    /// which is the failure this rule exists against: a second list beside the first,
    /// with the user's entries in the one they can no longer find.
    /// </summary>
    [Fact]
    public async Task AChangedNameRenamesTheExistingPlaylistAndCreatesNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var made = await projector.EnsurePlaylistAsync(TargetFor(store, TheOldName), CancellationToken.None);
        var renamed = await projector.EnsurePlaylistAsync(TargetFor(store, TheNewName), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Renamed, renamed.Outcome);
        Assert.Equal(made.Projection!.PlaylistId, renamed.Projection!.PlaylistId);
        Assert.Equal(1, server.Creations);
        Assert.Single(server.PlaylistsOf(AUser));
        Assert.Equal(TheNewName, server.PlaylistsOf(AUser)[0].Name);
    }

    /// <summary>
    /// And the record moves with it, so the next setting change compares against the
    /// name this plugin actually wrote rather than against the first one it ever wrote.
    /// </summary>
    [Fact]
    public async Task TheRecordCarriesTheNameThisPluginLastWrote()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var projector = new WatchlistProjector(new APlaylistServerOf(), new RecordingProjectorLogger());

        await projector.EnsurePlaylistAsync(TargetFor(store, TheOldName), CancellationToken.None);
        await projector.EnsurePlaylistAsync(TargetFor(store, TheNewName), CancellationToken.None);

        Assert.Equal(TheNewName, store.Read(AUser).Document!.Projection!.LastNameWritten);
    }

    /// <summary>
    /// A pass with the name unchanged renames nothing and writes nothing, which is what
    /// every pass on every server does once a name has settled.
    /// </summary>
    [Fact]
    public async Task AnUnchangedNameRenamesNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        await projector.EnsurePlaylistAsync(TargetFor(store, TheOldName), CancellationToken.None);
        var before = File.ReadAllBytes(store.PathFor(AUser));

        var again = await projector.EnsurePlaylistAsync(TargetFor(store, TheOldName), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.AlreadyProjected, again.Outcome);
        Assert.DoesNotContain(server.Calls, call => call.StartsWith("rename", StringComparison.Ordinal));
        Assert.Equal(before, File.ReadAllBytes(store.PathFor(AUser)));
    }

    /// <summary>
    /// A user who renamed their playlist by hand keeps their name. This is the bullet an
    /// implementation drops, because setting the label to the configured value on every
    /// pass is the straightforward reconciliation and it is a plugin overwriting a label
    /// a person chose.
    /// </summary>
    [Fact]
    public async Task AUserWhoRenamedTheirPlaylistKeepsTheirName()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var made = await projector.EnsurePlaylistAsync(TargetFor(store, TheOldName), CancellationToken.None);
        await server.RenameAsync(made.Projection!.PlaylistId, AUser, "Saturday", CancellationToken.None);

        var pass = await projector.EnsurePlaylistAsync(TargetFor(store, TheNewName), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.AlreadyProjected, pass.Outcome);
        Assert.Equal("Saturday", server.PlaylistsOf(AUser)[0].Name);
        Assert.Equal(TheOldName, store.Read(AUser).Document!.Projection!.LastNameWritten);
    }

    /// <summary>
    /// And it stays theirs. A later setting change does not take the label back, because
    /// the comparison is against the last name this plugin wrote and that name is still
    /// not the one on the playlist.
    /// </summary>
    [Fact]
    public async Task AndALaterChangeDoesNotTakeTheNameBack()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var made = await projector.EnsurePlaylistAsync(TargetFor(store, TheOldName), CancellationToken.None);
        await server.RenameAsync(made.Projection!.PlaylistId, AUser, "Saturday", CancellationToken.None);

        await projector.EnsurePlaylistAsync(TargetFor(store, TheNewName), CancellationToken.None);
        await projector.EnsurePlaylistAsync(TargetFor(store, "Something else again"), CancellationToken.None);

        Assert.Equal("Saturday", server.PlaylistsOf(AUser)[0].Name);
        Assert.DoesNotContain(server.Calls, call => call.StartsWith("rename " + made.Projection.PlaylistId, StringComparison.Ordinal) && call.EndsWith("Something else again", StringComparison.Ordinal));
    }

    /// <summary>
    /// The divergence is said once rather than on every pass. A line per user per pass
    /// is a log nobody reads, and what is reported is a standing state of the server
    /// rather than something that happened.
    /// </summary>
    [Fact]
    public async Task TheUsersOwnNameIsReportedOnceRatherThanOnEveryPass()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var log = new RecordingProjectorLogger();
        var projector = new WatchlistProjector(server, log);

        var made = await projector.EnsurePlaylistAsync(TargetFor(store, TheOldName), CancellationToken.None);
        await server.RenameAsync(made.Projection!.PlaylistId, AUser, "Saturday", CancellationToken.None);

        await projector.EnsurePlaylistAsync(TargetFor(store, TheNewName), CancellationToken.None);
        await projector.EnsurePlaylistAsync(TargetFor(store, TheNewName), CancellationToken.None);
        await projector.EnsurePlaylistAsync(TargetFor(store, TheNewName), CancellationToken.None);

        var about = Lines(log, "no longer carries the name this plugin wrote");
        Assert.Equal(1, about);
    }

    /// <summary>
    /// A user who renames their playlist to exactly what the setting later becomes is
    /// indistinguishable from one who never renamed it, so this plugin manages the name
    /// again from then on. The rule gets this case wrong in the harmless direction and
    /// the record is brought onto the label rather than left disagreeing with it.
    /// </summary>
    [Fact]
    public async Task ALabelThatAlreadyReadsAsTheSettingIsTakenOverRatherThanFought()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var made = await projector.EnsurePlaylistAsync(TargetFor(store, TheOldName), CancellationToken.None);
        await server.RenameAsync(made.Projection!.PlaylistId, AUser, TheNewName, CancellationToken.None);

        var pass = await projector.EnsurePlaylistAsync(TargetFor(store, TheNewName), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.AlreadyProjected, pass.Outcome);
        Assert.Equal(TheNewName, store.Read(AUser).Document!.Projection!.LastNameWritten);

        var third = await projector.EnsurePlaylistAsync(TargetFor(store, "A third name"), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Renamed, third.Outcome);
        Assert.Equal("A third name", server.PlaylistsOf(AUser)[0].Name);
    }

    /// <summary>
    /// A second playlist of that user carrying the configured name does not produce a
    /// duplicate projection. The identifier decides, and the collision is said once.
    /// </summary>
    [Fact]
    public async Task ACollidingNameOnAnotherPlaylistIsSaidOnceAndChangesNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var log = new RecordingProjectorLogger();
        var projector = new WatchlistProjector(server, log);

        var made = await projector.EnsurePlaylistAsync(TargetFor(store, TheOldName), CancellationToken.None);
        server.AlreadyHolds(AUser, Guid.Parse("dddddddd-0000-0000-0000-000000000001"), TheOldName);

        await projector.EnsurePlaylistAsync(TargetFor(store, TheOldName), CancellationToken.None);
        await projector.EnsurePlaylistAsync(TargetFor(store, TheOldName), CancellationToken.None);

        Assert.Equal(1, Lines(log, "carries the configured list name and is not the projected list"));
        Assert.Equal(1, server.Creations);
        Assert.Equal(made.Projection!.PlaylistId, store.Read(AUser).Document!.Projection!.PlaylistId);
    }

    /// <summary>
    /// A rename over a document this build refuses to write leaves the server untouched.
    /// The record is written first for exactly this reason: both orders leave a label and
    /// a record disagreeing if the second half fails, and this one fails having changed
    /// nothing a user can see.
    /// </summary>
    [Fact]
    public async Task ARenameThatCannotBeRecordedDoesNotTouchThePlaylist()
    {
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());
        var playlistId = Guid.Parse("dddddddd-0000-0000-0000-000000000002");
        server.AlreadyHolds(AUser, playlistId, TheOldName);

        var target = new ATargetThatWillNotRemember(AUser, TheNewName, new WatchlistProjectionState
        {
            PlaylistId = playlistId,
            LastNameWritten = TheOldName,
        });

        var result = await projector.EnsurePlaylistAsync(target, CancellationToken.None);

        Assert.Equal(ProjectionOutcome.RefusedRecordUnavailable, result.Outcome);
        Assert.Equal(TheOldName, server.PlaylistsOf(AUser)[0].Name);
        Assert.DoesNotContain(server.Calls, call => call.StartsWith("rename", StringComparison.Ordinal));
    }

    /// <summary>
    /// The same for the write that only moves the record onto a label the user had
    /// already set to the configured name.
    /// </summary>
    [Fact]
    public async Task ATakeOverThatCannotBeRecordedIsRefused()
    {
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());
        var playlistId = Guid.Parse("dddddddd-0000-0000-0000-000000000003");
        server.AlreadyHolds(AUser, playlistId, TheNewName);

        var target = new ATargetThatWillNotRemember(AUser, TheNewName, new WatchlistProjectionState
        {
            PlaylistId = playlistId,
            LastNameWritten = TheOldName,
        });

        var result = await projector.EnsurePlaylistAsync(target, CancellationToken.None);

        Assert.Equal(ProjectionOutcome.RefusedRecordUnavailable, result.Outcome);
        Assert.Equal(TheNewName, server.PlaylistsOf(AUser)[0].Name);
    }

    /// <summary>
    /// A rename with no projection at all makes one under the configured name, rather
    /// than renaming something that is not there. A user who joined the server after the
    /// setting moved is in exactly this state.
    /// </summary>
    [Fact]
    public async Task AChangedNameWithNoProjectionCreatesOneUnderTheNewName()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var result = await projector.EnsurePlaylistAsync(TargetFor(store, TheNewName), CancellationToken.None);

        Assert.Equal(ProjectionOutcome.Created, result.Outcome);
        Assert.Equal(TheNewName, server.PlaylistsOf(AUser)[0].Name);
        Assert.DoesNotContain(server.Calls, call => call.StartsWith("rename", StringComparison.Ordinal));
    }

    /// <summary>
    /// And a document this build refuses renames nothing, for the same reason it creates
    /// nothing: the record cannot be moved, so the label and the record would disagree
    /// from then on.
    /// </summary>
    [Fact]
    public async Task ADocumentThisBuildWillNotReadRenamesNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        Place(store.PathFor(TheFixtureUser), "watchlist-document-from-the-future.json");
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());

        var result = await projector.EnsurePlaylistAsync(
            TargetFor(store, TheNewName, TheFixtureUser),
            CancellationToken.None);

        Assert.Equal(ProjectionOutcome.RefusedRecordUnavailable, result.Outcome);
        Assert.Empty(server.Calls);
    }

    /// <summary>
    /// The default the configuration ships is the name a first projection carries, so
    /// the setting and the projection are one value rather than two that agree today.
    /// </summary>
    [Fact]
    public async Task TheConfiguredNameIsTheSettingAndNotAConstantOfTheProjection()
    {
        var store = new WatchlistDocumentStore(DataFolder, new RecordingLogger());
        var server = new APlaylistServerOf();
        var projector = new WatchlistProjector(server, new RecordingProjectorLogger());
        var configuration = new PluginConfiguration();

        await projector.EnsurePlaylistAsync(
            TargetFor(store, configuration.ProjectedListName),
            CancellationToken.None);

        Assert.Equal(PluginConfiguration.DefaultProjectedListName, server.PlaylistsOf(AUser)[0].Name);
    }

    private static UserProjectionTarget TargetFor(WatchlistDocumentStore store, string configuredName) =>
        TargetFor(store, configuredName, AUser);

    private static UserProjectionTarget TargetFor(WatchlistDocumentStore store, string configuredName, Guid userId) =>
        UserProjectionTarget.For(
            store,
            new PluginConfiguration { ProjectedListName = configuredName },
            new ADescriberOf(),
            new StoppedClock(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
            userId);

    private static int Lines(RecordingProjectorLogger log, string fragment)
    {
        var count = 0;

        foreach (var line in log.Lines)
        {
            if (line.Contains(fragment, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static void Place(string path, string fixture)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var assembly = Assembly.GetExecutingAssembly();
        var resource = "fixture/" + fixture;
        using var stream = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException(resource + " is not embedded in the test assembly.");

        using var reader = new StreamReader(stream);
        File.WriteAllText(path, reader.ReadToEnd());
    }

    /// <summary>
    /// A target that already has a playlist and whose record cannot be written, which is
    /// the narrow window between a readable document and an unwritable one.
    /// </summary>
    private sealed class ATargetThatWillNotRemember : IProjectionTarget
    {
        public ATargetThatWillNotRemember(Guid ownerUserId, string configuredName, WatchlistProjectionState remembered)
        {
            OwnerUserId = ownerUserId;
            ConfiguredName = configuredName;
            Remembered = remembered;
        }

        public Guid OwnerUserId { get; }

        public string ConfiguredName { get; }

        public bool IsRecordAvailable => true;

        public WatchlistProjectionState? Remembered { get; }

        public bool Remember(WatchlistProjectionState projection) => false;

        public int Adopt(IReadOnlyList<Guid> itemIds) => 0;
    }
}
