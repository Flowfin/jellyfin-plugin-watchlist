using System;
using System.IO;
using Jellyfin.Plugin.Watchlist.Store;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// What <see cref="WatchlistDocumentStore.CreateShared"/> ANSWERS, as opposed to what
/// it leaves on disk.
/// </summary>
/// <remarks>
/// <para>
/// The store's own remarks say the second call is the one an administrator makes when
/// they cannot remember whether the first one worked, that an existing list is left
/// exactly as it is, and that the answer says which of the two happened. The first two
/// were covered. The third was not: both of the answers could be inverted and the suite
/// stayed green, which is a mutation run's finding and not a coverage one, because both
/// lines were executed the whole time.
/// </para>
/// <para>
/// It matters because the answer is the only difference between the two outcomes that a
/// caller can see. A list that was already there and a list this call made look
/// identical on disk a moment later, so an inverted answer tells an administrator their
/// list was created when it was not, or refuses them one that was.
/// </para>
/// </remarks>
public sealed class SharedListCreationAnswerTests : IDisposable
{
    private static readonly Guid TheList = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    private static readonly Guid AnAdministrator = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private readonly TemporaryDirectory _sandbox = new("watchlist-shared-creation-answer");

    private string DataFolder => Path.Join(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// The call that makes the list says it made one.
    /// </summary>
    [Fact]
    public void TheCallThatMakesTheListAnswersThatItDid()
    {
        var store = new WatchlistDocumentStore(DataFolder);

        Assert.True(store.CreateShared(TheList, AnAdministrator));
        Assert.True(store.ReadShared().Exists);
    }

    /// <summary>
    /// And the second call says it made none, over a list that is still the first one.
    /// </summary>
    [Fact]
    public void TheSecondCallAnswersThatItMadeNothing()
    {
        var store = new WatchlistDocumentStore(DataFolder);
        store.CreateShared(TheList, AnAdministrator);

        var somebodyElse = Guid.Parse("88888888-8888-8888-8888-888888888888");

        Assert.False(store.CreateShared(Guid.NewGuid(), somebodyElse));

        var read = store.ReadShared();
        Assert.True(read.Exists);
        Assert.Equal(TheList, read.Document!.ListId);
        Assert.Equal(AnAdministrator, read.Document.OwnerUserId);
    }
}
