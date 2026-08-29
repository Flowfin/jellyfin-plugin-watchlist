# What this plugin stores about a user, and who can read it

A watchlist says what somebody intends to watch. That is personal data held on a
server other people may administer, so it is written down here rather than left to be
worked out from the source: what is kept, where it is kept, how long it stays, who can
read it, and what leaves the machine.

Every command in this file was re-run against the change that landed the deleted-user
handler, and each one still prints what is pasted under it. That is the whole file
rather than the sections that change touched, because a path added to the store moves
line numbers in files this page reads:

    git grep -c '^    \(git\|grep\|sed\|awk\|curl\|gh\) ' docs/personal-data.md
    docs/personal-data.md:35

Thirty-three were checked. The thirty-fourth is that line counting them, which counts
itself and was written after the pass rather than checked by it, and saying so is
cheaper than a count that quietly excludes the one command a reader can see.

Two of the thirty-three arrived after that pass rather than in it, with the playlist
seam on #82, and were run at the commit that landed that instead. They are the pair
under `## The one thing this file does not describe`, and they replace a single paste
whose answer the seam had stopped agreeing with.

The thirty-fifth arrived with the projector on #17, later than all of them, and names
the two values a projection writes into a user's document. It is under
`## What is stored` and it was run at the commit that landed it.

Most of the checking is a run rather than a reading now. `DocumentPasteTests` re-runs
every `grep` and `git grep` paste in this file and reds the suite where one has
stopped agreeing, which is thirty-four of the thirty-five. The one it does not judge
is the `sed` paste under `## Where it is stored`, and that one was run by hand. What
the check can and cannot see is in `Jellyfin.Plugin.Watchlist.Tests/DOCUMENT-PASTES.md`
rather than restated here.

Older readings name the commit they were taken on where they stand, and those
sentences are kept. What they say is when a reading last MOVED, not when it was last
checked; the checking is this paragraph and it covers the file.

## The one thing this file does not describe

**The shared list is described now**, in [its own section below](#the-shared-list),
and it used to be named here as an absence. It is a different list with a different
answer to every question on this page, so it gets a section rather than a sentence
inside the private list's.

What that section keeps from the paragraph it replaces is the negative statement,
because that is the part a reader is most likely to lose in a rewrite: no server
running this plugin has a shared list, nothing about any user sits in one, and
everything else on this page is unaffected by the record and its endpoints existing.
The section says it at greater length and with what it is read from.

**The projected playlists.** The way a list is meant to become visible on a client is
a playlist owned by that user. THE ABSENCE HAS GOT SMALLER TWICE AND IT IS STILL AN
ABSENCE, which is why this reads as a history rather than as one sentence. It first
said that nothing in this plugin names a playlist at all; the seam on #82 ended that.
It then said that one file names the server's playlist manager and nothing above it
decides anything; the projector on #17 ended that too. What is left is the last link:
the code that would make a playlist exists, and nothing on a running server calls it.

    git grep -l 'IPlaylistManager' -- Jellyfin.Plugin.Watchlist
    Jellyfin.Plugin.Watchlist/Projection/ServerPlaylistGateway.cs

    git grep -nE 'PlaylistGateway|WatchlistProjector' -- Jellyfin.Plugin.Watchlist/PluginServiceRegistrator.cs ; echo "exit=$?"
    exit=1

Neither the seam nor the projector is registered, so nothing in a running server can
resolve either, and no playlist is created, read or written on any server running this
plugin. Everything below is about a document on the server and this plugin's own
endpoints. A playlist is a second surface with its own answer to who can see it, and
that answer belongs here when there is a playlist to describe.

What the projector already writes into a user's document IS described below, under
`## What is stored`, because that block is bytes on disk whether or not anything ever
calls the code that fills it. A reader who takes this absence for "nothing about a
playlist is stored" would be wrong in the direction that matters.

That absence is stated so this file is not read as covering it.

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

And one more block, there only for a user whose list has been projected into a
playlist. It records which playlist that is and the name this plugin last wrote for
it:

    grep -n 'public required' Jellyfin.Plugin.Watchlist/Store/WatchlistProjectionState.cs
    38:    public required Guid PlaylistId { get; init; }
    43:    public required string LastNameWritten { get; init; }

Two values, and neither is about the person. The first is an identifier the server
minted for a playlist; the second is a name an administrator configured for every
user on the server. What the pair says about this user is that a playlist was made
for them, which is a thing they can already see on any client they log in to. A user
who has never had one has no such block in their document at all, for the same reason
the preferences block is absent for a user who answered nothing.

The whole of it on disk for a user who has answered nothing and had no playlist made,
as the suite's own fixture holds it:

    cat Jellyfin.Plugin.Watchlist.Tests/Fixtures/watchlist-document-v3.json
    {
      "SchemaVersion": 3,
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
[docs/export-format.md](export-format.md).

**THIS PARAGRAPH SAID NO ENDPOINT HANDS ONE OUT OR TAKES ONE IN, AND TWO DO.** They
landed on #40 and the absence under them was never taken again, so a reader of this
page was told the export never leaves the server through an endpoint while a route
was handing it to whoever asked for their own:

    git grep -cE 'Export|Import' -- Jellyfin.Plugin.Watchlist/Api/
    Jellyfin.Plugin.Watchlist/Api/LibraryProviderIds.cs:1
    Jellyfin.Plugin.Watchlist/Api/WatchlistImportEntryReport.cs:4
    Jellyfin.Plugin.Watchlist/Api/WatchlistImportOutcome.cs:1
    Jellyfin.Plugin.Watchlist/Api/WatchlistImportReport.cs:9
    Jellyfin.Plugin.Watchlist/Api/WatchlistTransferController.cs:48

What the two routes do is bounded and it is worth stating exactly, because the
sentence they replace was about who can get the data out. `GET Watchlist/Export`
hands the caller their OWN list, as an export, and takes no user identifier, like
every other route here; `POST Watchlist/Import` writes into the caller's own list
from a body they supplied. Neither reaches another user's list, neither sends
anything anywhere, and what each answers is in [docs/api.md](api.md).

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

Until somebody removes it, or until the server deletes the user it belongs to. There
is no expiry, no trimming and no retention period, and the store has exactly one path
that deletes a document:

    grep -n 'public void NoPathThroughTheStoreExceptADeletedUserRemovesADocument' \
      Jellyfin.Plugin.Watchlist.Tests/WatchlistDocumentSurvivalTests.cs
    113:    public void NoPathThroughTheStoreExceptADeletedUserRemovesADocument()

THIS PAGE SAID THE STORE HAD NO SUCH PATH AT ALL, AND THAT HAS STOPPED BEING TRUE.
What changed is the deletion of a user. The server removes the account, the list was
that person's, and keeping it would be this plugin holding somebody's data after the
server has stopped holding their user. The sentence is narrowed rather than
withdrawn: no path here removes a document because it looks like litter, and the
deletion removes exactly one document, the one belonging to the user the server
named:

    grep -n 'public void TheDeletionOfAUserRemovesThatOneDocumentAndNoOther' \
      Jellyfin.Plugin.Watchlist.Tests/WatchlistDocumentSurvivalTests.cs
    148:    public void TheDeletionOfAUserRemovesThatOneDocumentAndNoOther()

Taking the last entry off a list leaves the document in place holding an empty one:

    grep -n 'public void RemovingTheLastEntryLeavesTheDocumentOnDisk' \
      Jellyfin.Plugin.Watchlist.Tests/WatchlistDocumentSurvivalTests.cs
    60:    public void RemovingTheLastEntryLeavesTheDocumentOnDisk()

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
    46:    public void TheRoutesAreTheOnesWrittenDown()

Read again on `331b2af`. It has now said 55 and then 44, and each time the change
that moved the line did not carry the paste: first the reader that test calls moved
to a file of its own, then the export and the import were added to the pinned set.
The number is what a reader follows to check the claim rather than the claim itself,
and a number that has gone wrong twice for the same reason is worth naming as a habit
rather than repairing quietly.

What each route answers is in [docs/api.md](api.md).

## The shared list

Everything above this heading is a user's own list. This section is the other list
kind, and it answers the same questions differently because it is a different thing:
one list for the whole server, written by everybody who can reach it.

**No server has one.** This is where the negative statement belongs and it is stated
before anything else, because every sentence after it describes a list that does not
exist on anybody's machine yet. The record exists and the endpoints exist; nothing
creates a list. No setting creates one, no scheduled pass creates one, and there is no
route that would. Creating it is #87 and it has not been built, so all three endpoints
answer that there is none, and nothing about any user sits in a shared list today.

That is checkable rather than asserted. The controller reads the shared record and
asks the store to change one, and it never writes a document, which is what a creation
would be:

    git grep -lE 'WriteShared|ReadShared' -- Jellyfin.Plugin.Watchlist
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentFormat.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs

    git grep -c 'WriteShared' -- Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs ; echo "exit=$?"
    exit=1

So what follows is what a reader will be able to see once somebody builds the surface
that makes one, written before that ships rather than after, because a published
statement about who can see what has already been read by the time it is corrected.

### Who can read it

**Every user of the server.** Not a set an administrator picks: the list is one object
and the read endpoint is bound to nothing narrower than being somebody this server
knows. There are three shared routes and none of them takes a user identifier, exactly
as the private ones do not:

    git grep -n 'HttpGet("Shared\|HttpPost("Shared\|HttpDelete("Shared' -- Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:214:    [HttpGet("Shared/Items")]
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:248:    [HttpPost("Shared/Items/{itemId}")]
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:287:    [HttpDelete("Shared/Items/{itemId}")]

**Anybody with file access to the server**, for the same reason a private list is
readable that way. It is one plain file under the same folder, and its name is not one
any user identifier can produce, so it cannot be mistaken for somebody's document:

    grep -n 'SharedListFileName =' Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs
    47:    internal const string SharedListFileName = "shared-list.json";

**Two readers can get different answers.** An entry whose library item a caller cannot
see is left out of that caller's reading, so the shared list cannot be used to learn
what sits in a library they have no access to. The entry stays on the list for
everybody else, and the caller is not told it was left out:

    grep -n 'public void AnEntryTheCallerCannotSeeIsLeftOutAndStaysOnTheList' \
      Jellyfin.Plugin.Watchlist.Tests/SharedWatchlistApiTests.cs
    355:    public void AnEntryTheCallerCannotSeeIsLeftOutAndStaysOnTheList()

### Who can change it

**Anybody who may use the server may add.** There is no curator and no approval step.

**A removal is allowed to two people**: whoever put the entry there, and an
administrator. Whether a caller is an administrator is the server's own answer, asked
through the server's elevation policy rather than decided by a rule kept in this
plugin. A removal by anybody else is refused and changes nothing.

This is the answer to question 7 on #1, taken on 2026-08-24, and the endpoints
implement it rather than restating it. What each request answers is in
[api.md](api.md).

### What a reader learns about other people

**Who put each title on the list.** This is the sentence that decides the privacy
character of this list, so it is stated plainly rather than left to be inferred: every
entry on the shared list carries the identifier of the user who added it, and that
identifier is returned to every reader of the list.

    grep -n 'public Guid? AddedBy' Jellyfin.Plugin.Watchlist/Api/WatchlistEntryView.cs
    79:    public Guid? AddedBy { get; init; }

So a shared list is not a list of titles. It is a record of which named people on this
server wanted to watch which things, readable by everyone else on it. That is the
answer to question 8 on #1, taken deliberately on 2026-08-24 over the alternative of
storing the name and never returning it: the attribution is what makes an entry
somebody's suggestion rather than an anonymous one, and it is also what tells a caller
which entries they may take off again.

A user who does not want that known about them should not add the title. There is no
setting that makes an entry anonymous, and this page does not imply one.

**And nothing else.** The record holds four members, and none of them is about a
reader:

    grep -n 'get; init;' Jellyfin.Plugin.Watchlist/Store/SharedWatchlistDocument.cs
    47:    public required int SchemaVersion { get; init; }
    52:    public required Guid ListId { get; init; }
    63:    public required Guid OwnerUserId { get; init; }
    68:    public required IReadOnlyList<WatchlistEntry> Entries { get; init; }

**The shared list holds no per-user reading state.** Nobody learns from it who has
opened the list, who has looked at an entry, or what anybody has watched. Reading it
writes nothing at all, so the list does not record that a read happened. The one
watched-related value this plugin stores is a per-user preference on that user's own
document and is not on this record:

    grep -n 'public bool? RemoveWhenWatched' Jellyfin.Plugin.Watchlist/Store/WatchlistUserPreferences.cs
    43:    public bool? RemoveWhenWatched { get; init; }

**And a private list is not reachable from here.** No endpoint returns another user's
private list, which is a property of the whole route set rather than of these three,
and the shared routes did not weaken it: they are separate routes rather than a list
identifier added to the private ones, so there is no spelling of a shared request that
names a private list.

### What is stored on it

The same four values a private entry carries, and the fifth that a private entry does
not: who added it. A private entry leaves that member unset, so a user's own document
holds what it held before the shared list existed, and the two are held apart by the
suite from both sides:

    grep -n 'public void TheReadSaysWhoAddedEachEntry\|public void APrivateListSaysNothingAboutWhoAddedAnEntry' \
      Jellyfin.Plugin.Watchlist.Tests/SharedWatchlistApiTests.cs
    104:    public void TheReadSaysWhoAddedEachEntry()
    121:    public void APrivateListSaysNothingAboutWhoAddedAnEntry()

How long an entry stays, and what an uninstall leaves behind, are the same answers the
sections above give for a private list: until somebody removes it, and everything
stays where it is.

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

One refusal does separate two cases rather than collapsing them, and it is worth
naming here rather than being found. Taking an entry off the shared list is refused
with its own code when the entry is somebody else's, instead of being answered as
though the entry were not there. What that discloses is that the entry exists and is
not the caller's, and the caller could have read exactly that off the shared list a
moment earlier, because every entry on it names who added it. It discloses nothing
about a library: an entry the caller may not see is left out of their reading of the
list in the first place.

## What reaches the server log

Identifiers, counts and versions. Never a title, and never anything read out of the
library. Twenty-seven places log at all, over six files:

    git grep -cE '_logger\.Log' -- Jellyfin.Plugin.Watchlist/
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:9
    Jellyfin.Plugin.Watchlist/Api/WatchlistTransferController.cs:5
    Jellyfin.Plugin.Watchlist/Projection/WatchlistProjector.cs:5
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs:6
    Jellyfin.Plugin.Watchlist/Users/DeletedUserHandler.cs:1
    Jellyfin.Plugin.Watchlist/Watched/WatchedRemovalHandler.cs:1

THAT PASTE HAD ALREADY STOPPED REPRODUCING BEFORE THIS CHANGE, AND ONLY ONE OF ITS
TWO NEW LINES IS MINE. It showed two files and a count of fifteen. The transfer
controller's five lines arrived with the export and import endpoints and the paste
was not taken again, so a reader running the command met four files under a sentence
naming two. The watched handler's one line is the change that made this section have
to be re-read, and re-reading it is what found the older half.

What they name is the calling user's identifier, an item identifier, the kind the
library holds an item as, an entry count, a configured maximum, and a schema version.
The watched handler's line names a count of entries and two identifiers, the user the
event named and the item that was played, and nothing out of the entries it removed.
The deleted-user handler's line names one identifier, the user the server deleted, and
it is written only where a document was there to remove, so a server deleting users
who never opened a watchlist logs nothing at all.
The projector's five lines name a user identifier and a playlist identifier and never
the name of either. Two of the five are said once per playlist for the life of the
process rather than on every pass, because what they report is a standing state of the
server: a playlist whose label the user changed, and a second playlist of that user
carrying the configured name.
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
    499:                "Refusing to read {Path}: it declares shared watchlist schema version {StoredVersion} and this plugin understands version {UnderstoodVersion}. The shared list is unavailable and the file is left alone.",
    510:                "Refusing to read {Path}: it declares shared watchlist schema version {StoredVersion} and this plugin carries no upgrade step from it to version {UnderstoodVersion}. The shared list is unavailable and the file is left alone.",

Re-taken on the change that landed the deleted-user handler, where the last two moved
from 414 and 425 because the deletion path went into the store above them, and again
on the change that landed the projector, which moved them to 499 and 510 for the same
reason: the store gained the writer that records a user's playlist above them. The paste
held two lines until the shared list gained the same two refusals, and the last two of
the four name a path that is the shared list's file rather than anybody's identifier.

The path is what makes that line actionable, since it names the file somebody has to
look at, and an identifier without the list beside it says nothing about what anyone
wanted to watch.

## What leaves the server

Nothing. No part of this plugin opens a socket, builds an HTTP client or makes a web
request:

    git grep -nE 'HttpClient|WebClient|TcpClient|WebRequest|new Socket' \
      -- Jellyfin.Plugin.Watchlist ; echo "exit=$?"
    exit=1

The imports from a `System.Net` namespace are not network access:

    git grep -n 'using System.Net' -- Jellyfin.Plugin.Watchlist
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:4:using System.Net.Mime;
    Jellyfin.Plugin.Watchlist/Api/WatchlistTransferController.cs:3:using System.Net.Mime;

Two imports rather than one, and the sentence under this said one until the transfer
controller's line was taken again. `System.Net.Mime` supplies the name of the media
type each controller declares it answers in. There is no telemetry, no usage reporting and no update check of this
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
