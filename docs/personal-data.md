# What this plugin stores about a user, and who can read it

A watchlist says what somebody intends to watch. That is personal data held on a
server other people may administer, so it is written down here rather than left to be
worked out from the source: what is kept, where it is kept, how long it stays, who can
read it, and what leaves the machine.

Every reading below was taken on `8edffa9`, except the four that moved when the
shared list got a record of its own. Those four were re-taken on `6930c4a` and say so
where they stand; every other command in this file was re-run at that commit and still
prints what is pasted under it.

## The two things this file does not describe

**The shared list.** The plugin is planned with a second list, one the whole server
can see. That list has a different answer to every question below, and the one that
decides its character is whether an entry records who put it there: a list of titles
and a record of what named people wanted to watch are two different statements to make
to a reader.

THIS PARAGRAPH SAID THAT QUESTION WAS OPEN AND THAT NO SHARED RECORD WAS IN THE TREE.
Both halves have moved and they moved in opposite directions for a reader, so neither
is dropped in silence. The question is answered on #1 - one shared list per server, and
an entry that records who put it there, visible to everyone who can see the list - and
the record now exists. Re-taken on `6930c4a`, where the store is sixteen files and two
of them are it:

    git ls-files 'Jellyfin.Plugin.Watchlist/Store/*'
    Jellyfin.Plugin.Watchlist/Store/IWatchlistItemResolver.cs
    Jellyfin.Plugin.Watchlist/Store/SharedWatchlistDocument.cs
    Jellyfin.Plugin.Watchlist/Store/SharedWatchlistReadResult.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistAddOutcome.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistAddResult.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocument.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentFormat.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentUpgrades.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistEntry.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistEntrySource.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistItemKind.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistReadResult.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistRemoveResult.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistUserPreferences.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistVisibility.cs

WHAT NO SERVER HOLDS IS THE POINT TO READ CAREFULLY, and this is a negative statement
rather than a softened positive one. A record existing in the source and a list
existing on somebody's machine are different things. Nothing in this plugin creates a
shared list, writes to one, or reads one on behalf of a caller: there is no endpoint
for it, no setting that makes one, and no scheduled pass that would touch it. The
two members that read and write it are named in the store that declares them and
nowhere else in the plugin, and the only caller of either is the suite:

    git grep -lE 'WriteShared|ReadShared' -- Jellyfin.Plugin.Watchlist
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentFormat.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs

    git grep -lE 'WriteShared|ReadShared' -- Jellyfin.Plugin.Watchlist.Tests
    Jellyfin.Plugin.Watchlist.Tests/SharedWatchlistDocumentTests.cs

The first two are the declaration and the reader beside it, so nothing in the shipped
assembly calls either from outside the store. On a running server there is therefore no
shared list, nothing about any user sits in one, and everything the rest of this page
says about who can read what is unaffected by the record having arrived.

The section this page still owes is the one that describes that list to a reader: who
sees it, what they learn about other people from it, and that an entry names the person
who added it. It is #69, it is written before the list ships rather than after, and it
is not written here in advance of the surface it would describe, because a published
statement about who can see what has already been read by the time it is corrected.

The export format already carries the kind, so a reader of an exported file can tell
the two apart, and the shared half of it is fed values by a caller rather than read
from the record. Re-taken on `6930c4a`; the three lines said the record was not built
yet, and the change that built it corrected them where they stand:

    grep -n 'The pieces are passed in rather than read from a shared record' -A 2 \
      Jellyfin.Plugin.Watchlist/Export/WatchlistExporter.cs
    52:    /// The pieces are passed in rather than read from a shared record. That said the
    53-    /// record was not built yet, and it is: what has not been written is the caller
    54-    /// that maps one onto this call, and the format does not move when it is.

Those two trailing lines carry a dash rather than a colon, which is what `grep` prints
for a line it pulled in as context rather than one that matched. Only the line the
pattern names is a match, and the three were once arranged by hand instead of taken
from the command, which is what an earlier reading of this file found by running every
command pasted in it and comparing it with what stands under it.

**The projected playlists.** The way a list is meant to become visible on a client is
a playlist owned by that user. Nothing projects one yet:

    git grep -n 'IPlaylistManager' -- Jellyfin.Plugin.Watchlist ; echo "exit=$?"
    exit=1

So everything below is about a document on the server and this plugin's own endpoints.
A playlist is a second surface with its own answer to who can see it, and that answer
belongs here when there is a playlist to describe.

Both absences are stated so this file is not read as covering them.

## What is stored

Four values per entry, and nothing copied out of the library. Re-taken on `6930c4a`,
where the line numbers moved by one and the four members did not:

    grep -n 'public required' Jellyfin.Plugin.Watchlist/Store/WatchlistEntry.cs
    19:    public required Guid ItemId { get; init; }
    24:    public required WatchlistItemKind Kind { get; init; }
    29:    public required DateTimeOffset AddedAt { get; init; }
    34:    public required WatchlistEntrySource Source { get; init; }

An identifier for the library item, what kind of item it was recorded as, the instant
the entry was added in UTC, and how it arrived. No title, no image, no file path. That
started as a correctness rule, because a copied title is wrong the moment the media is
renamed, and it is the reason a stored list says less about a person than the same list
written out in words.

There is a fifth member on the type and it is not on a user's entry. It records who put
an entry on the shared list, and a user's own list has one writer, so nothing sets it
on one:

    grep -n 'public Guid? AddedBy' Jellyfin.Plugin.Watchlist/Store/WatchlistEntry.cs
    53:    public Guid? AddedBy { get; init; }

Unset, it is left out of the document rather than written as an empty value, so a
user's document holds the same four values it held before the member existed. That is a
property of the bytes and the suite refuses the other spelling: written as an empty
value instead, three tests red, two of them the ones that hold the private document's
shape against the committed sample.

Three values around those entries, per user:

    grep -n 'public required' Jellyfin.Plugin.Watchlist/Store/WatchlistDocument.cs
    21:    public required int SchemaVersion { get; init; }
    28:    public required Guid UserId { get; init; }
    33:    public required IReadOnlyList<WatchlistEntry> Entries { get; init; }

And one more that is there only for a user who asked for it. A user may answer two
settings for themselves, and their answer is kept in their own document:

    grep -n 'public bool?' Jellyfin.Plugin.Watchlist/Store/WatchlistUserPreferences.cs
    36:    public bool? ProjectionEnabled { get; init; }
    43:    public bool? RemoveWhenWatched { get; init; }

Two booleans and nothing else. A user who has answered neither has no such block in
their document at all, which is a property of the bytes rather than of a reader:

    grep -n 'NoBlockOnDisk' Jellyfin.Plugin.Watchlist.Tests/PerUserSettingTests.cs
    143:    public void AUserWhoNeverSetAnythingHasNoBlockOnDisk()
    186:    public void WithdrawingTheLastAnswerLeavesNoBlockOnDisk(bool withAnEmptyBlock)

What that block tells a reader about the person is what they chose for those two
settings, and it is readable by whoever can read the file, exactly as the entries
are. Nothing in it is derived from the library or from what they watched.

The whole of it on disk for a user who answered nothing, as the suite's own fixture
holds it:

    cat Jellyfin.Plugin.Watchlist.Tests/Fixtures/watchlist-document-v2.json
    {
      "SchemaVersion": 2,
      "UserId": "11111111-1111-1111-1111-111111111111",
      "Entries": [
        {
          "ItemId": "aaaaaaaa-0000-0000-0000-000000000001",
          "Kind": "Movie",
          "AddedAt": "2026-01-02T03:04:05+00:00",
          "Source": "Api"
        },
    ...

Plain JSON, neither encrypted nor obfuscated. Anybody who can read the file can read
the list, and that is said plainly rather than dressed up: a plugin holding a key on
the same disk as the file the key protects has protected nothing and has added a way to
lose the list.

An exported list carries one value per entry that the store does not, the provider
identifiers of the item, so that an import onto a different server can match a title
rather than an identifier that means nothing there. What that file holds is in
[docs/export-format.md](export-format.md), and no endpoint hands one out or takes one
in today:

    git grep -nE 'Export|Import' -- Jellyfin.Plugin.Watchlist/Api/ ; echo "exit=$?"
    exit=1

## Where it is stored

One document per user, in the plugin's own data folder, named after that user's
identifier and nothing else:

    sed -n '107,109p' Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs
        public string PathFor(Guid userId) => Path.Combine(
            _dataFolderPath,
            string.Format(CultureInfo.InvariantCulture, "{0}.json", userId.ToString("N", CultureInfo.InvariantCulture)));

Re-taken on `6930c4a`. The three lines are the ones this file has always pasted and the
range that prints them moved, because the shared list's own path went in above them:

    grep -n 'public string SharedListPath' Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs
    95:    public string SharedListPath => Path.Combine(_dataFolderPath, SharedListFileName);

That file is under the same folder and cannot be named by any identifier a server can
mint, so it can never be mistaken for somebody's document. No server has one, for the
reason stated at the top of this page.

Which folder that is on a running server was measured against a stock container and is
in [docs/uninstall.md](uninstall.md), beside the two other paths this plugin leaves
behind. It is not repeated here, because a path written in two files goes stale in one
of them.

Nothing is written to a user's media, and nothing of this is written into the server's
own database. The reason it is not in the plugin's configuration document instead is
that the server rewrites that whole document when an administrator saves the settings
page, which is a decision recorded in [docs/storage-decision.md](storage-decision.md).

## How long it stays

Until somebody removes it. There is no expiry, no trimming, no retention period, and
the store has no path at all that deletes a document:

    grep -n 'public void NoPathThroughTheStoreRemovesADocument' \
      Jellyfin.Plugin.Watchlist.Tests/WatchlistDocumentSurvivalTests.cs
    103:    public void NoPathThroughTheStoreRemovesADocument()

Taking the last entry off a list leaves the document in place holding an empty one:

    grep -n 'public void RemovingTheLastEntryLeavesTheDocumentOnDisk' \
      Jellyfin.Plugin.Watchlist.Tests/WatchlistDocumentSurvivalTests.cs
    50:    public void RemovingTheLastEntryLeavesTheDocumentOnDisk()

Reading a list never writes to it, so opening a watchlist leaves no trace in the file
and a document written by an older version is brought forward in memory rather than on
disk:

    grep -n 'public void TheUpgradedFormIsWrittenOnlyWhenSomethingElseChangesTheDocument' \
      Jellyfin.Plugin.Watchlist.Tests/WatchlistDocumentUpgradeTests.cs
    327:    public void TheUpgradedFormIsWrittenOnlyWhenSomethingElseChangesTheDocument()

An uninstall leaves every document where it is; that was measured rather than assumed
and the measurement is in [docs/uninstall.md](uninstall.md), together with what a
person removes by hand to leave nothing behind. What a disable does to the same files
is #38 and is not answered here.

## Who can read it

**The user whose list it is**, through this plugin's endpoints. A request is answered
for the user it came from, and no endpoint takes a user identifier at all, which is
read off every endpoint in the assembly rather than off one signature:

    grep -n 'public void NoEndpointInThisPluginTakesAUserIdentifier' \
      Jellyfin.Plugin.Watchlist.Tests/WatchlistApiIdentityTests.cs
    125:    public void NoEndpointInThisPluginTakesAUserIdentifier()

Who a request is from is read in one place, so there is one answer rather than several
of varying leniency:

    grep -n 'public const string Claim' Jellyfin.Plugin.Watchlist/Api/CallingUser.cs
    29:    public const string Claim = "Jellyfin-UserId";

    grep -n 'public void OnlyOneFileInThePluginNamesTheClaim' \
      Jellyfin.Plugin.Watchlist.Tests/WatchlistApiIdentityTests.cs
    48:    public void OnlyOneFileInThePluginNamesTheClaim()

A request carrying no identity this plugin will use is refused rather than answered
with a default, and the four ways of having none get one answer: no principal, no
claim, a claim that is not an identifier, and the all-zero identifier, which is the
one worth naming because it parses.

**Anybody with file access to the server.** The documents are plain files under the
server's configuration directory, so an administrator, a backup, a snapshot of a
container volume and anyone with a shell on that machine can read every user's list.
No setting changes that, and this plugin is not in a position to.

**Not another user.** There is no spelling of a request here that names somebody else,
and the route set is written down and held to what the assembly actually exposes:

    grep -n 'public void TheRoutesAreTheOnesWrittenDown' \
      Jellyfin.Plugin.Watchlist.Tests/WatchlistApiRouteTests.cs
    41:    public void TheRoutesAreTheOnesWrittenDown()

Read again on `7306873`. It said 55 until then, because the reader that test calls
moved to a file of its own and the change that moved it did not carry this paste.

What each route answers is in [docs/api.md](api.md).

## What a refusal does not give away

An item the caller cannot see is answered the same way as an item that is not in the
library, so these endpoints cannot be used to learn what sits in a library somebody has
no access to. A refusal carries its status code and no body, and that is refused rather
than remembered:

    grep -c '^api-refusal-body' Jellyfin.Plugin.Watchlist.Tests/Invariants.txt
    8

Eight spellings of a refusal that explains itself, each one a red run. The bound on
that is written beside the rules: the scan is line-based, so a call split across lines
is outside it.

## What reaches the server log

Identifiers, counts and versions. Never a title, and never anything read out of the
library. Re-taken on `6930c4a`, where nine places log at all and the count said seven
until the shared list's two refusals were added:

    git grep -cE '_logger\.Log' -- Jellyfin.Plugin.Watchlist/
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:4
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs:5

and what they name is the calling user's identifier, an item identifier, the kind the
library holds an item as, an entry count, a configured maximum, and a schema version.
The pass that drops entries whose item can no longer be resolved reports one line with
a count, and nothing out of the entries themselves:

    grep -n 'public void NoLineNamesAnythingFromTheEntriesThemselves' \
      Jellyfin.Plugin.Watchlist.Tests/WatchlistVisibilityTests.cs
    95:    public void NoLineNamesAnythingFromTheEntriesThemselves()

One of the seven is worth stating rather than leaving to be discovered. A document this
plugin refuses to read is reported with its path, and the file name is the user's
identifier, so a refused read puts that identifier in the server log:

    grep -n 'Refusing to read {Path}' Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs
    152:                "Refusing to read {Path}: it declares watchlist schema version {StoredVersion} and this plugin understands version {UnderstoodVersion}. The list is unavailable for this user and the file is left alone.",
    167:                "Refusing to read {Path}: it declares watchlist schema version {StoredVersion} and this plugin carries no upgrade step from it to version {UnderstoodVersion}. The list is unavailable for this user and the file is left alone.",
    414:                "Refusing to read {Path}: it declares shared watchlist schema version {StoredVersion} and this plugin understands version {UnderstoodVersion}. The shared list is unavailable and the file is left alone.",
    425:                "Refusing to read {Path}: it declares shared watchlist schema version {StoredVersion} and this plugin carries no upgrade step from it to version {UnderstoodVersion}. The shared list is unavailable and the file is left alone.",

Re-taken on `6930c4a`. The paste held two lines until the shared list gained the same
two refusals, and the last two of the four name a path that is the shared list's file
rather than anybody's identifier.

The path is what makes that line actionable, since it names the file somebody has to
look at, and an identifier without the list beside it says nothing about what anyone
wanted to watch.

## What leaves the server

Nothing. No part of this plugin opens a socket, builds an HTTP client or makes a web
request:

    git grep -nE 'HttpClient|WebClient|TcpClient|WebRequest|new Socket' \
      -- Jellyfin.Plugin.Watchlist ; echo "exit=$?"
    exit=1

There is one import from a `System.Net` namespace and it is not network access:

    git grep -n 'using System.Net' -- Jellyfin.Plugin.Watchlist
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:4:using System.Net.Mime;

`System.Net.Mime` supplies the name of the media type the controller declares it
answers in. There is no telemetry, no usage reporting and no update check of this
plugin's own. What updates the plugin is the server reading a manifest, which is the
server's request made on an administrator's instruction rather than something this
plugin does.

That paragraph was a reading of the sources at one commit until #69 put a rule under
it. `plugin-network` in `Jellyfin.Plugin.Watchlist.Tests/Invariants.txt` refuses a
client built by hand, one taken from the server through its own factory, a socket, a
web request, a name resolved against the network, and the imports either of the first
two needs. It is read by the same scan that already refuses a refusal carrying a body,
so the next change adding an outbound call reds the run naming the file, the line and
the rule rather than being found by whoever next reads this page.

The bound on the rule, stated rather than left to be found. It matches text, line by
line, in this plugin's own sources and nowhere else, so it reaches a call written in
those files and not one made on this plugin's behalf by something it hands work to. A
call split across lines is outside it in the same way every rule in that table is, and
a name the rule does not carry is a name it does not refuse. What it makes impossible
is the quiet arrival of the ordinary forms, which is how such a call actually arrives.
