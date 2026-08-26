using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The record the one shared list is kept in, and the store members that read and
/// write it.
/// </summary>
/// <remarks>
/// The shared list is not a user's document under another name, and these are the
/// four differences that make it one record rather than a second copy of the other:
/// it is keyed by nothing, it carries an owner as a value, an entry on it says who
/// put it there, and a server can have none of it at all. Everything else - the
/// staged write, the version rule, the folder - is deliberately the same, and the
/// tests below assert that sameness rather than describing it.
/// </remarks>
public sealed class SharedWatchlistDocumentTests : IDisposable
{
    private static readonly Guid TheList = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid TheOwner = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly Guid AUser = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid AnotherUser = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("shared-watchlist-document");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The name of the shared list's file cannot be the name of a user's document,
    /// and that is a property of the two naming rules rather than of the identifiers
    /// anybody happened to try. Every user document is named by an identifier in its
    /// hexadecimal form; this name does not parse as one, so no identifier a server
    /// can mint produces it.
    /// </summary>
    [Fact]
    public void TheSharedListCannotBeNamedByAnyUserIdentifier()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        var sharedName = Path.GetFileNameWithoutExtension(store.SharedListPath);

        Assert.False(
            Guid.TryParseExact(sharedName, "N", out _),
            "The shared list's file name parses as an identifier, so some user could own a document with that name.");

        Assert.Equal(".json", Path.GetExtension(store.SharedListPath), StringComparer.Ordinal);
    }

    /// <summary>
    /// The same statement from the other end, over the naming rule rather than over
    /// the one name: a user document name is thirty-two hexadecimal digits, and the
    /// shared one is not of that shape.
    /// </summary>
    [Fact]
    public void EveryUserDocumentIsNamedByThirtyTwoHexadecimalDigitsAndTheSharedListIsNot()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        var identifiers = new[]
        {
            AUser, AnotherUser, TheList, TheOwner, Guid.Empty,
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
        };

        foreach (var identifier in identifiers)
        {
            var name = Path.GetFileNameWithoutExtension(store.PathFor(identifier));

            Assert.Equal(32, name.Length);
            Assert.All(name, character => Assert.True(
                "0123456789abcdef".Contains(character, StringComparison.Ordinal),
                "A user document name carries a character outside the hexadecimal set."));
            Assert.NotEqual(store.SharedListPath, store.PathFor(identifier), StringComparer.Ordinal);
        }

        var sharedName = Path.GetFileNameWithoutExtension(store.SharedListPath);

        Assert.NotEqual(32, sharedName.Length);
    }

    /// <summary>
    /// It lives under the folder the store was given, like everything else the store
    /// builds a path for.
    /// </summary>
    [Fact]
    public void TheSharedListLivesUnderTheFolderTheStoreWasGiven()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        Assert.StartsWith(
            store.DataFolderPath + Path.DirectorySeparatorChar,
            Path.GetFullPath(store.SharedListPath),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A server on which nobody has made a shared list says so. It is not the same
    /// answer as a list that exists and holds nothing, and the difference is the one
    /// a caller acts on: making a list is somebody's decision, so a read must not be
    /// the thing that looks like one having been taken.
    /// </summary>
    [Fact]
    public void AServerWithNoSharedListSaysSoRatherThanAnsweringWithAnEmptyOne()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        var read = store.ReadShared();

        Assert.False(read.Exists);
        Assert.False(read.IsAvailable);
        Assert.Null(read.Document);
        Assert.Null(read.StoredSchemaVersion);
        Assert.False(File.Exists(store.SharedListPath));
    }

    /// <summary>
    /// A list that was made and holds nothing is the other answer, and it says the
    /// list exists.
    /// </summary>
    [Fact]
    public void AListThatWasMadeAndHoldsNothingIsNotAServerWithoutOne()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.WriteShared(WatchlistDocumentStore.EmptyShared(TheList, TheOwner));

        var read = store.ReadShared();

        Assert.True(read.Exists);
        Assert.True(read.IsAvailable);
        Assert.Empty(read.Document!.Entries);
        Assert.Equal(SharedWatchlistDocument.CurrentSchemaVersion, read.Document!.SchemaVersion);
    }

    /// <summary>
    /// The record carries its identity, its owner and its entries, and reading it
    /// back gives all three.
    /// </summary>
    [Fact]
    public void TheRecordCarriesItsIdentityItsOwnerAndItsEntries()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.WriteShared(SharedList(EntryAddedBy(1, AUser), EntryAddedBy(2, AnotherUser)));

        var document = store.ReadShared().Document!;

        Assert.Equal(TheList, document.ListId);
        Assert.Equal(TheOwner, document.OwnerUserId);
        Assert.Equal([Item(1), Item(2)], document.Entries.Select(entry => entry.ItemId));
    }

    /// <summary>
    /// The owner is a value in the record rather than an implied administrator, so a
    /// server whose administrator account is replaced still has the list and still
    /// knows whose it is. Read back from disk rather than from the object that was
    /// written, because the failure this is against is an owner that lives only in
    /// memory.
    /// </summary>
    [Fact]
    public void TheOwnerIsStoredRatherThanImplied()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.WriteShared(SharedList());

        using var stored = JsonDocument.Parse(File.ReadAllText(store.SharedListPath));

        Assert.Equal(TheOwner, stored.RootElement.GetProperty("OwnerUserId").GetGuid());
    }

    /// <summary>
    /// The identity is not the displayed name, because the record holds no displayed
    /// name at all. Asserted over the members on disk rather than over the type, so
    /// a name added later is caught here whatever it is called.
    /// </summary>
    [Fact]
    public void TheRecordHoldsNoDisplayedName()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.WriteShared(SharedList(EntryAddedBy(1, AUser)));

        using var stored = JsonDocument.Parse(File.ReadAllText(store.SharedListPath));

        Assert.Equal(
            new[] { "AddedAt", "AddedBy", "Entries", "ItemId", "Kind", "ListId", "OwnerUserId", "SchemaVersion", "Source" },
            MemberNames(stored.RootElement)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>
    /// An entry on the shared list says who put it there, and every reader of the
    /// list gets that.
    /// </summary>
    [Fact]
    public void AnEntryOnTheSharedListRecordsWhoPutItThere()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.WriteShared(SharedList(EntryAddedBy(1, AUser), EntryAddedBy(2, AnotherUser)));

        var entries = store.ReadShared().Document!.Entries;

        Assert.Equal(AUser, entries[0].AddedBy);
        Assert.Equal(AnotherUser, entries[1].AddedBy);
    }

    /// <summary>
    /// And a private list does not, on disk or after a read. One writer, and the
    /// answer would be the user whose document it is, which the document already
    /// declares. The member is suppressed rather than written as null, so a private
    /// document is the same bytes it was before this record existed.
    /// </summary>
    [Fact]
    public void AnEntryOnAUsersOwnListCarriesNoSuchMember()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Add(AUser, Entry(1), maxEntriesPerUser: 10);

        var text = File.ReadAllText(store.PathFor(AUser));

        Assert.DoesNotContain("AddedBy", text, StringComparison.Ordinal);
        Assert.Null(store.Read(AUser).Document!.Entries[0].AddedBy);
    }

    /// <summary>
    /// The entry type is one type. A title moves from a user's list to the shared one
    /// by being put there, with no converter and no second shape, and it arrives
    /// carrying what it carried plus the one member the shared list adds.
    /// </summary>
    [Fact]
    public void AnEntryMovesBetweenTheTwoListsWithoutAConverter()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Add(AUser, Entry(1), maxEntriesPerUser: 10);

        var itsOwn = store.Read(AUser).Document!.Entries[0];

        store.WriteShared(SharedList(itsOwn with { AddedBy = AUser }));

        var shared = store.ReadShared().Document!.Entries[0];

        Assert.Equal(itsOwn.ItemId, shared.ItemId);
        Assert.Equal(itsOwn.Kind, shared.Kind);
        Assert.Equal(itsOwn.AddedAt, shared.AddedAt);
        Assert.Equal(itsOwn.Source, shared.Source);
        Assert.Equal(AUser, shared.AddedBy);
        Assert.Equal(itsOwn, shared with { AddedBy = null });
    }

    /// <summary>
    /// The version rule, in the direction a downgrade takes. A list written by a newer
    /// plugin is refused rather than guessed at, and the file is left exactly as it
    /// was.
    /// </summary>
    [Fact]
    public void AListFromANewerPluginIsRefusedAndTheFileIsLeftAlone()
    {
        var logger = new RecordingLogger();
        var store = new WatchlistDocumentStore(DataFolder, logger);

        var fromTheFuture = StoredAtVersion(SharedWatchlistDocument.CurrentSchemaVersion + 1);
        WriteSharedTextDirectly(store, fromTheFuture);

        var read = store.ReadShared();

        Assert.True(read.Exists);
        Assert.False(read.IsAvailable);
        Assert.Equal(SharedWatchlistDocument.CurrentSchemaVersion + 1, read.StoredSchemaVersion);
        Assert.Equal(fromTheFuture, File.ReadAllText(store.SharedListPath), StringComparer.Ordinal);
        Assert.Contains(
            logger.Lines,
            line => line.Contains("understands version", StringComparison.Ordinal));

        // The two refusals answer a caller identically, so the line is the only place
        // that says which of them fired. A test that asked only whether the read was
        // refused would pass with this refusal deleted, because the version is then
        // above the chain as well and the other one catches it.
        Assert.DoesNotContain(
            logger.Lines,
            line => line.Contains("no upgrade step", StringComparison.Ordinal));
    }

    /// <summary>
    /// The same refusal in the other direction. A version below the first one this
    /// record ever had is a version no step reaches, so it is refused rather than
    /// relabelled as current.
    /// </summary>
    [Fact]
    public void AListFromAVersionNoStepReachesIsRefusedAndTheFileIsLeftAlone()
    {
        var logger = new RecordingLogger();
        var store = new WatchlistDocumentStore(DataFolder, logger);

        var fromBelow = StoredAtVersion(SharedWatchlistDocument.CurrentSchemaVersion - 1);
        WriteSharedTextDirectly(store, fromBelow);

        var read = store.ReadShared();

        Assert.True(read.Exists);
        Assert.False(read.IsAvailable);
        Assert.Equal(SharedWatchlistDocument.CurrentSchemaVersion - 1, read.StoredSchemaVersion);
        Assert.Equal(fromBelow, File.ReadAllText(store.SharedListPath), StringComparer.Ordinal);
        Assert.Contains(
            logger.Lines,
            line => line.Contains("no upgrade step", StringComparison.Ordinal));

        Assert.DoesNotContain(
            logger.Lines,
            line => line.Contains("understands version", StringComparison.Ordinal));
    }

    /// <summary>
    /// The chain the version rule asks, read directly. It reaches the version this
    /// plugin writes and nothing below it, and it refuses a version above.
    /// </summary>
    [Fact]
    public void TheSharedChainReachesTheCurrentVersionAndNothingElse()
    {
        Assert.True(WatchlistDocumentUpgrades.CanBringSharedForward(SharedWatchlistDocument.CurrentSchemaVersion));
        Assert.False(WatchlistDocumentUpgrades.CanBringSharedForward(SharedWatchlistDocument.CurrentSchemaVersion - 1));
        Assert.False(WatchlistDocumentUpgrades.CanBringSharedForward(SharedWatchlistDocument.CurrentSchemaVersion + 1));
        Assert.Empty(WatchlistDocumentUpgrades.SharedSteps);
    }

    /// <summary>
    /// Bringing a document forward from the version this plugin writes changes
    /// nothing, and asking for a version no step reaches throws rather than returning
    /// a relabelled document.
    /// </summary>
    [Fact]
    public void BringingASharedListForwardIsAskedBeforeItIsDone()
    {
        var stored = System.Text.Json.Nodes.JsonNode
            .Parse(StoredAtVersion(SharedWatchlistDocument.CurrentSchemaVersion))!
            .AsObject();

        var unchanged = WatchlistDocumentUpgrades.BringSharedForward(
            stored,
            SharedWatchlistDocument.CurrentSchemaVersion);

        Assert.Equal(
            SharedWatchlistDocument.CurrentSchemaVersion,
            unchanged["SchemaVersion"]!.GetValue<int>());

        Assert.Throws<InvalidOperationException>(() => WatchlistDocumentUpgrades.BringSharedForward(
            stored,
            SharedWatchlistDocument.CurrentSchemaVersion - 1));
    }

    /// <summary>
    /// The interruption the staged write exists for, over the shared list. The staged
    /// file is on disk and the move has not happened, which is where a crash leaves
    /// the tree, and a reader still gets the whole of the previous list.
    /// </summary>
    [Fact]
    public void AWriteInterruptedBeforeTheMoveLeavesThePreviousSharedListReadable()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.WriteShared(SharedList(EntryAddedBy(1, AUser)));

        var staged = store.Stage(SharedList(EntryAddedBy(1, AUser), EntryAddedBy(2, AUser)));

        Assert.True(File.Exists(staged.StagedPath), "The staged file should be on disk before the move.");
        Assert.EndsWith(WatchlistDocumentStore.PendingSuffix, staged.StagedPath, StringComparison.Ordinal);
        Assert.Single(store.ReadShared().Document!.Entries);
    }

    /// <summary>
    /// And the other half: the move puts the new list in place in one step and leaves
    /// no staged file beside it.
    /// </summary>
    [Fact]
    public void CommittingTheStagedWriteReplacesTheSharedListInOneStep()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.WriteShared(SharedList(EntryAddedBy(1, AUser)));

        var staged = store.Stage(SharedList(EntryAddedBy(1, AUser), EntryAddedBy(2, AUser)));
        WatchlistDocumentStore.Commit(staged);

        Assert.False(File.Exists(staged.StagedPath));
        Assert.Equal(2, store.ReadShared().Document!.Entries.Count);
    }

    /// <summary>
    /// A staged write that was never committed is never the list, and on a server
    /// that had none it does not make one.
    /// </summary>
    [Fact]
    public void AStagedWriteIsNeverTheSharedList()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Stage(SharedList(EntryAddedBy(1, AUser)));

        Assert.False(File.Exists(store.SharedListPath));
        Assert.False(store.ReadShared().Exists);
    }

    /// <summary>
    /// The shared list has a gate of its own. Two stores over one folder share it, so
    /// it is the file that is guarded, and it is not any user's gate, so writing the
    /// shared list never waits for a user's list.
    /// </summary>
    [Fact]
    public void TheSharedListIsGuardedByItsOwnGate()
    {
        var one = new WatchlistDocumentStore(DataFolder);
        var another = new WatchlistDocumentStore(DataFolder);

        Assert.Same(one.SharedGate(), another.SharedGate());
        Assert.NotSame(one.SharedGate(), one.GateFor(AUser));
        Assert.NotSame(one.SharedGate(), one.GateFor(TheList));
    }

    /// <summary>
    /// Writing one list leaves the other alone in both directions, which is what the
    /// separate names and the separate gates are for.
    /// </summary>
    [Fact]
    public void TheTwoKindsOfListDoNotWriteOverEachOther()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        store.Add(AUser, Entry(1), maxEntriesPerUser: 10);
        var usersBytes = File.ReadAllBytes(store.PathFor(AUser));

        store.WriteShared(SharedList(EntryAddedBy(2, AnotherUser)));
        Assert.Equal(usersBytes, File.ReadAllBytes(store.PathFor(AUser)));

        var sharedBytes = File.ReadAllBytes(store.SharedListPath);
        store.Add(AUser, Entry(3), maxEntriesPerUser: 10);

        Assert.Equal(sharedBytes, File.ReadAllBytes(store.SharedListPath));
        Assert.Equal(2, store.Read(AUser).Document!.Entries.Count);
        Assert.Single(store.ReadShared().Document!.Entries);
    }

    /// <summary>
    /// An empty shared list declares the version this plugin writes and holds nothing.
    /// </summary>
    [Fact]
    public void AnEmptySharedListDeclaresTheCurrentVersionAndHoldsNothing()
    {
        var empty = WatchlistDocumentStore.EmptyShared(TheList, TheOwner);

        Assert.Equal(SharedWatchlistDocument.CurrentSchemaVersion, empty.SchemaVersion);
        Assert.Equal(TheList, empty.ListId);
        Assert.Equal(TheOwner, empty.OwnerUserId);
        Assert.Empty(empty.Entries);
    }

    /// <summary>
    /// The JSON literal null is valid JSON and is not a document, on this record for
    /// the same reason as on a user's.
    /// </summary>
    [Fact]
    public void TheJsonLiteralNullIsRefusedRatherThanReturned()
    {
        Assert.Throws<JsonException>(() => WatchlistDocumentFormat.ReadShared("null"));
    }

    /// <summary>
    /// A member renamed and every other byte the same is refused rather than handed
    /// back with a defaulted field, which is the format's rule and is asserted here on
    /// the record that has just started using it.
    /// </summary>
    /// <param name="member">The member name as it is written.</param>
    /// <param name="renamed">What to rename it to.</param>
    [Theory]
    [InlineData("SchemaVersion", "SchemaVersionNumber")]
    [InlineData("ListId", "ListGuid")]
    [InlineData("OwnerUserId", "Owner")]
    [InlineData("Entries", "Items")]
    [InlineData("AddedBy", "AddedByUser")]
    public void ARenamedMemberIsRefusedRatherThanDefaulted(string member, string renamed)
    {
        var written = WatchlistDocumentFormat.Write(SharedList(EntryAddedBy(1, AUser)));
        var mutated = written.Replace(
            "\"" + member + "\":",
            "\"" + renamed + "\":",
            StringComparison.Ordinal);

        Assert.NotEqual(written, mutated);
        Assert.Throws<JsonException>(() => WatchlistDocumentFormat.ReadShared(mutated));
    }

    /// <summary>
    /// A null reference is not a document either, and the writer says so before a file
    /// is staged for it.
    /// </summary>
    [Fact]
    public void NothingIsWrittenForADocumentThatIsNotThere()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        Assert.Throws<ArgumentNullException>(() => store.WriteShared(null!));
        Assert.Throws<ArgumentNullException>(() => store.Stage((SharedWatchlistDocument)null!));
        Assert.Throws<ArgumentNullException>(() => SharedWatchlistReadResult.Available(null!));
        Assert.False(File.Exists(store.SharedListPath));
    }

    private static Guid Item(int n) => Guid.Parse(
        string.Format(CultureInfo.InvariantCulture, "00000000-0000-0000-0000-{0:D12}", n));

    private static WatchlistEntry Entry(int n) => new()
    {
        ItemId = Item(n),
        Kind = WatchlistItemKind.Movie,
        AddedAt = WhenItWasAdded,
        Source = WatchlistEntrySource.Api,
    };

    private static WatchlistEntry EntryAddedBy(int n, Guid whoAddedIt) => Entry(n) with { AddedBy = whoAddedIt };

    private static SharedWatchlistDocument SharedList(params WatchlistEntry[] entries) => new()
    {
        SchemaVersion = SharedWatchlistDocument.CurrentSchemaVersion,
        ListId = TheList,
        OwnerUserId = TheOwner,
        Entries = entries,
    };

    private static string StoredAtVersion(int version) => WatchlistDocumentFormat
        .Write(SharedList(EntryAddedBy(1, AUser)))
        .Replace(
            "\"SchemaVersion\": " + SharedWatchlistDocument.CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture),
            "\"SchemaVersion\": " + version.ToString(CultureInfo.InvariantCulture),
            StringComparison.Ordinal);

    /// <summary>
    /// Puts text on disk where the shared list lives, without going through the store,
    /// so a test can produce a document this plugin would never write.
    /// </summary>
    /// <param name="store">The store whose folder is written into.</param>
    /// <param name="text">The document text.</param>
    private static void WriteSharedTextDirectly(WatchlistDocumentStore store, string text)
    {
        Directory.CreateDirectory(store.DataFolderPath);
        File.WriteAllText(store.SharedListPath, text);
    }

    private static System.Collections.Generic.IEnumerable<string> MemberNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in element.EnumerateObject())
                {
                    yield return member.Name;

                    foreach (var nested in MemberNames(member.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var nested in MemberNames(item))
                    {
                        yield return nested;
                    }
                }

                break;

            default:
                break;
        }
    }
}
