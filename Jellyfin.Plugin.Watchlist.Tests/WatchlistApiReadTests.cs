using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using Jellyfin.Plugin.Watchlist.Api;
using Jellyfin.Plugin.Watchlist.Configuration;
using Jellyfin.Plugin.Watchlist.Store;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.Watchlist.Tests;

/// <summary>
/// The read endpoint, driven by calling it. No web host is started and no server is
/// present: the controller is constructed with a store over a directory this test
/// owns and a describer that answers from a table, which is the whole reason the
/// describer is an interface.
/// </summary>
public sealed class WatchlistApiReadTests : IDisposable
{
    private static readonly Guid AUser = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnotherUser = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly DateTimeOffset WhenItWasAdded =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    private readonly TemporaryDirectory _sandbox = new("watchlist-api-read");

    private string DataFolder => Path.Combine(_sandbox.FullPath, "plugin-data");

    /// <inheritdoc />
    public void Dispose()
    {
        _sandbox.Dispose();
    }

    /// <summary>
    /// A user who never added anything. An empty list rather than a refusal, because
    /// nothing is wrong: this is what a new account looks like and it needs no
    /// provisioning to look like it.
    /// </summary>
    [Fact]
    public void AUserWhoNeverAddedAnythingGetsAnEmptyList()
    {
        var controller = ControllerOver(new DescriberFor());

        Assert.Empty(EntriesOf(controller.ItemsFor(AUser)));
    }

    /// <summary>
    /// The list, and enough about each item to draw a row without asking again per
    /// item. What the entry recorded and what the library says are both on it.
    /// </summary>
    [Fact]
    public void TheListCarriesWhatTheStoreRecordedAndWhatTheLibrarySays()
    {
        Store(AUser, Entry(1, WatchlistItemKind.Movie), Entry(2, WatchlistItemKind.Episode));

        var describer = new DescriberFor(
            (Item(1), AUser, new WatchlistItemDescription { Name = "A film", ProductionYear = 1998 }),
            (Item(2), AUser, new WatchlistItemDescription
            {
                Name = "An episode",
                SeriesName = "A series",
                SeasonNumber = 2,
                EpisodeNumber = 7,
            }));

        var entries = EntriesOf(ControllerOver(describer).ItemsFor(AUser));

        Assert.Equal([Item(1), Item(2)], entries.Select(e => e.ItemId));
        Assert.Equal([WatchlistItemKind.Movie, WatchlistItemKind.Episode], entries.Select(e => e.Kind));
        Assert.Equal([WhenItWasAdded, WhenItWasAdded], entries.Select(e => e.AddedAt));

        var film = entries[0];
        Assert.Equal("A film", film.Name);
        Assert.Equal(1998, film.ProductionYear);
        Assert.Null(film.SeriesName);

        var episode = entries[1];
        Assert.Equal("An episode", episode.Name);
        Assert.Equal("A series", episode.SeriesName);
        Assert.Equal(2, episode.SeasonNumber);
        Assert.Equal(7, episode.EpisodeNumber);
    }

    /// <summary>
    /// An item this user may not see is not on their list and nothing about it is on
    /// the answer either. The describer answers the same nothing for an item that is
    /// gone and for one that is hidden, so the response cannot be read backwards to
    /// learn which it was.
    /// </summary>
    [Fact]
    public void AnItemTheCallerCannotSeeIsNotOnTheirList()
    {
        Store(AUser, Entry(1, WatchlistItemKind.Movie), Entry(2, WatchlistItemKind.Movie));

        var describer = new DescriberFor(
            (Item(1), AUser, new WatchlistItemDescription { Name = "A film they may see" }),
            (Item(2), AnotherUser, new WatchlistItemDescription { Name = "A film they may not" }));

        var entries = EntriesOf(ControllerOver(describer).ItemsFor(AUser));

        var only = Assert.Single(entries);
        Assert.Equal(Item(1), only.ItemId);
        Assert.DoesNotContain("may not", only.Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// The entry is skipped on the way out and left in the document, which is the
    /// store's rule and not a second copy of it made here. A library that comes back
    /// brings the entry back with it.
    /// </summary>
    [Fact]
    public void AnEntryWhoseItemIsGoneIsSkippedAndStaysInTheDocument()
    {
        Store(AUser, Entry(1, WatchlistItemKind.Movie), Entry(2, WatchlistItemKind.Movie));

        var describer = new DescriberFor(
            (Item(2), AUser, new WatchlistItemDescription { Name = "The one still there" }));

        Assert.Single(EntriesOf(ControllerOver(describer).ItemsFor(AUser)));

        var stored = new WatchlistDocumentStore(DataFolder).Read(AUser).Document;
        Assert.Equal([Item(1), Item(2)], stored!.Entries.Select(e => e.ItemId));
    }

    /// <summary>
    /// A list this plugin refuses to read is unavailable, not empty. Answering with an
    /// empty list is how a refusal turns into an overwrite the next time something
    /// writes, which is the failure the store's read result exists to prevent and this
    /// is the endpoint honouring it. Which code every outcome gets is #29.
    /// </summary>
    [Fact]
    public void AListThisPluginWillNotReadIsUnavailableRatherThanEmpty()
    {
        Directory.CreateDirectory(DataFolder);
        File.WriteAllText(
            new WatchlistDocumentStore(DataFolder).PathFor(AUser),
            "{\"SchemaVersion\":9999,\"UserId\":\"" + AUser + "\",\"Entries\":[]}");

        var result = ControllerOver(new DescriberFor()).ItemsFor(AUser);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
    }

    /// <summary>
    /// The describer is asked once per item however many times the read needs the
    /// answer. Two questions about one item are two chances to get two answers, and
    /// the second one would decide what a row says about an item the first one already
    /// let through.
    /// </summary>
    [Fact]
    public void TheDescriberIsAskedOncePerItem()
    {
        Store(AUser, Entry(1, WatchlistItemKind.Movie), Entry(1, WatchlistItemKind.Movie));

        var describer = new DescriberFor(
            (Item(1), AUser, new WatchlistItemDescription { Name = "Asked about once" }));

        Assert.Equal(2, EntriesOf(ControllerOver(describer).ItemsFor(AUser)).Count);
        Assert.Equal(1, describer.TimesAsked(Item(1)));
    }

    /// <summary>
    /// The route, with an identity on the request. This is the only test that goes
    /// through the endpoint rather than the read behind it, and it is here to prove the
    /// two are connected.
    /// </summary>
    [Fact]
    public void TheEndpointReadsTheListOfTheUserTheRequestNames()
    {
        Store(AUser, Entry(1, WatchlistItemKind.Movie));

        var describer = new DescriberFor(
            (Item(1), AUser, new WatchlistItemDescription { Name = "Theirs" }));

        var controller = ControllerOver(describer);
        controller.ControllerContext = ContextFor(AUser.ToString());

        var only = Assert.Single(EntriesOf(controller.GetWatchlistItems()));
        Assert.Equal("Theirs", only.Name);
    }

    /// <summary>
    /// A request carrying no identity is refused rather than served as somebody. The
    /// hardening of this, one helper every endpoint uses and a test pinning the claim
    /// string, is #27; what is here is that the first endpoint does not fall back to a
    /// default user.
    /// </summary>
    [Fact]
    public void ARequestWithNoIdentityIsRefused()
    {
        var controller = ControllerOver(new DescriberFor());
        controller.ControllerContext = ContextFor(null);

        Assert.IsType<UnauthorizedResult>(controller.GetWatchlistItems().Result);
    }

    /// <summary>
    /// And one whose identity is not an identifier. It is refused for the same reason
    /// and not read as the empty identifier, which is a real user identifier as far as
    /// a file name is concerned.
    /// </summary>
    [Fact]
    public void ARequestWhoseIdentityCannotBeReadIsRefused()
    {
        var controller = ControllerOver(new DescriberFor());
        controller.ControllerContext = ContextFor("not-an-identifier");

        Assert.IsType<UnauthorizedResult>(controller.GetWatchlistItems().Result);
    }

    private static Guid Item(int number) => Guid.Parse(
        "00000000-0000-0000-0000-" + number.ToString("D12", System.Globalization.CultureInfo.InvariantCulture));

    private static WatchlistEntry Entry(int number, WatchlistItemKind kind) => new()
    {
        ItemId = Item(number),
        Kind = kind,
        AddedAt = WhenItWasAdded,
        Source = WatchlistEntrySource.Api,
    };

    private static ControllerContext ContextFor(string? claimValue)
    {
        var identity = new ClaimsIdentity();

        if (claimValue is not null)
        {
            identity.AddClaim(new Claim(CallingUser.Claim, claimValue));
        }

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) },
        };
    }

    private static IReadOnlyList<WatchlistEntryView> EntriesOf(
        ActionResult<IReadOnlyList<WatchlistEntryView>> result) => result.Value!;

    private WatchlistController ControllerOver(IWatchlistItemDescriber describer) => new(
        new WatchlistDocumentStore(DataFolder),
        describer,
        new PluginConfiguration(),
        new StoppedClock(WhenItWasAdded),
        NullLogger<WatchlistController>.Instance);

    private void Store(Guid userId, params WatchlistEntry[] entries)
    {
        new WatchlistDocumentStore(DataFolder).Write(new WatchlistDocument
        {
            SchemaVersion = WatchlistDocument.CurrentSchemaVersion,
            UserId = userId,
            Entries = entries,
        });
    }

    /// <summary>
    /// A describer that answers from a table and counts what it was asked. An entry in
    /// the table is a pair of an item and the one user it answers for, so "this user
    /// may not see it" is written as an entry belonging to somebody else rather than as
    /// a flag, which is how the server would put it.
    /// </summary>
    private sealed class DescriberFor : IWatchlistItemDescriber
    {
        private readonly Dictionary<(Guid ItemId, Guid UserId), WatchlistItemDescription> _known = [];
        private readonly Dictionary<Guid, int> _asked = [];

        public DescriberFor(params (Guid ItemId, Guid UserId, WatchlistItemDescription Description)[] known)
        {
            foreach (var (itemId, userId, description) in known)
            {
                _known[(itemId, userId)] = description;
            }
        }

        public WatchlistItemDescription? Describe(Guid itemId, Guid userId)
        {
            _asked[itemId] = TimesAsked(itemId) + 1;

            return _known.TryGetValue((itemId, userId), out var description) ? description : null;
        }

        public int TimesAsked(Guid itemId) => _asked.TryGetValue(itemId, out var times) ? times : 0;
    }
}
