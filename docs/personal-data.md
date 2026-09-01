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
    docs/personal-data.md:43

Thirty-three were checked. The thirty-fourth is that line counting them, which counts
itself and was written after the pass rather than checked by it, and saying so is
cheaper than a count that quietly excludes the one command a reader can see. A second
counting line arrived later, under the same rule and with the same admission.

Two of the thirty-three arrived after that pass rather than in it, with the playlist
seam on #82, and were run at the commit that landed that instead. They are the pair
under `## The one thing this file does not describe`, and they replace a single paste
whose answer the seam had stopped agreeing with.

The thirty-fifth arrived with the projector on #17, later than all of them, and names
the two values a projection writes into a user's document. It is under
`## What is stored` and it was run at the commit that landed it.

Five more arrived with `## The projected playlists`, later still, and were run at the
commit that landed that section.

One more arrived with the shared list's transfer routes on #40, later than all of
them, and names the two endpoints that carry that list off this server and back onto
one. It is under `### Who can read it` and it was run at the commit that landed it.

One more arrived with the removal of the shared list's playlist on #301, later than all
of them, and names the three files that spell that removal. It is under
`### The plugin never shares a private one` and it was run at the commit that landed it.

Most of the checking is a run rather than a reading now. `DocumentPasteTests` re-runs
every `grep` and `git grep` paste in this file and reds the suite where one has
stopped agreeing, which is forty-one of the forty-three:

    git grep -c '^    \(git grep\|grep\) ' docs/personal-data.md
    docs/personal-data.md:41

Both numbers include that line and the one at the top of this file, each of which
counts itself, which is the same admission the paragraph above makes and is why the
two are stated together rather than one being quietly adjusted.

The two commands it does not run are the `sed` paste under `## Where it is stored` and the
`git show` of a jellyfin checkout under `## The projected playlists`. Both were run by
hand, and the second is the weaker of the two by some way: it reads a tree this
repository does not hold at all, so no run here could ever judge it and its output is
a reading somebody took on a machine that had that checkout. What
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

**The projected playlists are described now**, in [their own section
below](#the-projected-playlists), and they used to be named here as an absence. A
playlist is a second surface with its own answer to who can see it, so it gets a
section rather than a sentence inside the document's.

WHAT THAT SECTION KEEPS IS THE NEGATIVE, AND THE NEGATIVE IS SMALLER THAN IT WAS
RATHER THAN GONE. This paragraph read as a history because it shrank twice: it first
said nothing in this plugin names a playlist at all, which the seam on #82 ended, and
then said one file names the server's playlist manager and nothing above it decides
anything, which the projector on #17 ended. What is left is the last link, and it is
the sentence that section carries: the code that would make a playlist exists, and
nothing on a running server calls it.

Both absences are stated so this file is not read as covering what it does not.

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
playlist. It records which playlist that is, the name this plugin last wrote for it,
and what this plugin last put in it:

    grep -n 'public required' Jellyfin.Plugin.Watchlist/Store/WatchlistProjectionState.cs
    39:    public required Guid PlaylistId { get; init; }
    44:    public required string LastNameWritten { get; init; }
    66:    public required IReadOnlyList<Guid> ProjectedItemIds { get; init; }
    81:    public required DateTimeOffset? WrittenAt { get; init; }

THE THIRD OF THOSE IS ABOUT THE PERSON AND THE OTHER THREE ARE NOT, and the
difference is worth stating rather than leaving to be worked out. The first is an
identifier the server minted for a playlist. The second is a name an administrator
configured for every user on the server. The fourth is an instant.

The third is a set of library identifiers, and it is a copy of what this user's list
projected as at the last pass. That is the same class of fact as the entries above it,
one file away from being the same bytes, and a reader who can read one can read the
other: it is in the same document, under the same permissions, and there is no reading
of it that a reading of the entries does not already give. It is stored because
without it a row added on a client and a row not yet projected are indistinguishable,
and one of those two readings means delete.

A user who has never had a playlist made has no such block in their document at all,
for the same reason the preferences block is absent for a user who answered nothing.

The whole of it on disk for a user who has answered nothing and had no playlist made,
as the suite's own fixture holds it:

    cat Jellyfin.Plugin.Watchlist.Tests/Fixtures/watchlist-document-v4.json
    {
      "SchemaVersion": 4,
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

**THIS PARAGRAPH SAID NO ENDPOINT HANDS ONE OUT OR TAKES ONE IN, AND FOUR DO.** The
first two landed on #40 and the absence under them was never taken again, so a reader
of this page was told the export never leaves the server through an endpoint while a
route was handing it to whoever asked for their own. The other two landed later on the
same issue and are the shared list's own pair:

    git grep -cE 'Export|Import' -- Jellyfin.Plugin.Watchlist/Api/
    Jellyfin.Plugin.Watchlist/Api/ImportableTo.cs:7
    Jellyfin.Plugin.Watchlist/Api/ImportedFile.cs:27
    Jellyfin.Plugin.Watchlist/Api/LibraryProviderIds.cs:1
    Jellyfin.Plugin.Watchlist/Api/SharedWatchlistTransferController.cs:23
    Jellyfin.Plugin.Watchlist/Api/WatchlistImportEntryReport.cs:4
    Jellyfin.Plugin.Watchlist/Api/WatchlistImportOutcome.cs:1
    Jellyfin.Plugin.Watchlist/Api/WatchlistImportReport.cs:9
    Jellyfin.Plugin.Watchlist/Api/WatchlistTransferController.cs:23

What the four routes do is bounded and it is worth stating exactly, because the
sentence they replace was about who can get the data out. `GET Watchlist/Export`
hands the caller their OWN list, as an export, and takes no user identifier, like
every other route here; `POST Watchlist/Import` writes into the caller's own list
from a body they supplied. `GET Watchlist/Shared/Export` hands out the one list the
whole server shares, unfiltered, and `POST Watchlist/Shared/Import` writes into it;
both are refused to anybody the server's elevation policy does not answer for. None
of the four reaches another user's private list, none of them sends anything
anywhere, and what each answers is in [docs/api.md](api.md).

## Where it is stored

One document per user, in the plugin's own data folder, named after that user's
identifier and nothing else:

    sed -n '107,109p' Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs
        public string PathFor(Guid userId) => Path.Join(
            _dataFolderPath,
            string.Format(CultureInfo.InvariantCulture, "{0}.json", userId.ToString("N", CultureInfo.InvariantCulture)));

Re-taken on the change that closed #265. The three lines are the ones this file has
always pasted and the range that prints them has not moved; the call they name has,
from `Path.Combine` to `Path.Join`. The same substitution moved the line below:

    grep -n 'public string SharedListPath' Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs
    95:    public string SharedListPath => Path.Join(_dataFolderPath, SharedListFileName);

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
    381:    public void TheUpgradedFormIsWrittenOnlyWhenSomethingElseChangesTheDocument()

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
    50:    public void TheRoutesAreTheOnesWrittenDown()

Re-taken on the change that landed the shared list's transfer routes. It has now said
55, then 44, then 46, then 48, and each of the first two moves went unpasted: first
the reader that test calls moved to a file of its own, then the export and the import
were added to the pinned set. The two administrative routes of #87 moved it by two
more and the two transfer routes of #40 by two after that, and both of those readings
were taken with the change rather than found afterwards.
The number is what a reader follows to check the claim rather than the claim itself,
and a number that has gone wrong twice for the same reason is worth naming as a habit
rather than repairing quietly.

What each route answers is in [docs/api.md](api.md).

## The shared list

Everything above this heading is a user's own list. This section is the other list
kind, and it answers the same questions differently because it is a different thing:
one list for the whole server, written by everybody who can reach it.

**A SERVER HAS ONE ONLY IF AN ADMINISTRATOR MADE IT, AND THIS PARAGRAPH SAID NOTHING
COULD MAKE ONE.** That was true until #87 landed the route that does, and the negative
which survives it is the narrower one, stated before anything else because every
sentence after it describes a list a server need not have. Nothing here creates a
shared list on its own. No setting creates one, no scheduled pass creates one, and no
import creates one: the transfer routes write into a list that is already there and
answer that there is none when it is not. So a server whose administrator has never
asked for a shared list has none, and nothing about any user sits in a list nobody
made.

That is checkable rather than asserted. Every route that touches the shared record
reads it or asks the store to change it, and none of them writes a document by hand,
which is what a creation nobody asked for would be:

    git grep -lE 'WriteShared|ReadShared' -- Jellyfin.Plugin.Watchlist
    Jellyfin.Plugin.Watchlist/Api/SharedWatchlistTransferController.cs
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs
    Jellyfin.Plugin.Watchlist/Projection/SharedProjectionTarget.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentFormat.cs
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs

    git grep -c 'WriteShared' -- Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs ; echo "exit=$?"
    exit=1

So what follows is what a reader sees on a server whose administrator has made one. It
was written before that surface shipped rather than after, because a published
statement about who can see what has already been read by the time it is corrected.

### Who can read it

**Every user of the server.** Not a set an administrator picks: the list is one object
and the read endpoint is bound to nothing narrower than being somebody this server
knows. There are seven shared routes and none of them takes a user identifier, exactly
as the private ones do not. Five sit on the controller that carries the item routes:

    git grep -n 'HttpGet("Shared\|HttpPost("Shared\|HttpDelete("Shared' -- Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:231:    [HttpGet("Shared/Items")]
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:265:    [HttpPost("Shared/Items/{itemId}")]
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:304:    [HttpDelete("Shared/Items/{itemId}")]
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:349:    [HttpPost("Shared")]
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:396:    [HttpDelete("Shared")]

and two on the one that carries the list between servers:

    git grep -n 'HttpGet("Shared\|HttpPost("Shared' -- Jellyfin.Plugin.Watchlist/Api/SharedWatchlistTransferController.cs
    Jellyfin.Plugin.Watchlist/Api/SharedWatchlistTransferController.cs:118:    [HttpGet("Shared/Export")]
    Jellyfin.Plugin.Watchlist/Api/SharedWatchlistTransferController.cs:163:    [HttpPost("Shared/Import")]

The first paste said three until #87 added its last two. Those two are the list itself
rather than its contents - making it and taking it away - and neither reads anything:
one writes an empty record and the other removes it. The second paste is the pair from
#40, which carries the list off this server and back onto one. All four are refused to
an ordinary user, and the two pastes are read together rather than the first alone,
because a count over one controller stopped answering for this surface the day a
second one arrived.

FIVE OF THE SEVEN ARE CLOSED WHILE THE SETTING SAYS NO, WHICH IS #277 AND IS NEW. The
five over the list's CONTENTS - the read, the add, the removal of one entry, and the
export and import pair - read `SharedListEnabled` and answer as though this server had
no shared list while it says no. So an administrator who turns the switch off closes the
list to every user of the server rather than only to the page that describes it, and the
sentence at the head of this section is true of a server whose switch says yes. What is
NOT closed is the administrative pair: the creation refuses with its own answer, and the
removal has to stay reachable, because it is how a list made before the switch moved is
taken away. Nothing on disk is touched either way, so turning the switch back on gives
every user the list they had.

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

**The list itself is an administrator's to make and to remove**, which is narrower
than either of the two above and is the only place on this surface where that is so.
Removing it takes the shared record away and nothing else: no private document is read
or written by it, and this plugin projects no shared playlist for it to take with it.

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

**And nothing else.** The record holds five members, and none of them is about a
reader:

    grep -n 'get; init;' Jellyfin.Plugin.Watchlist/Store/SharedWatchlistDocument.cs
    48:    public required int SchemaVersion { get; init; }
    53:    public required Guid ListId { get; init; }
    64:    public required Guid OwnerUserId { get; init; }
    69:    public required IReadOnlyList<WatchlistEntry> Entries { get; init; }
    87:    public WatchlistProjectionState? Projection { get; init; }

THE FIFTH ARRIVED WITH THE PROJECTION AND IS THE SAME KIND OF THING AS THE ONE ON A
USER'S DOCUMENT. It names the playlist the shared list is projected into, the name last
written for it, the items last put in it and when. The items are a copy of what the
entries above them projected as, which is one file away from being the same bytes and
readable by exactly whoever can read those, and it is stored because without it a row
somebody added on a client and a row no pass has projected yet are indistinguishable -
and one of those two readings means delete.

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
library. Forty-five places log at all, over nine files:

    git grep -cE '_logger\.Log' -- Jellyfin.Plugin.Watchlist/
    Jellyfin.Plugin.Watchlist/Api/SharedWatchlistTransferController.cs:7
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:15
    Jellyfin.Plugin.Watchlist/Api/WatchlistTransferController.cs:5
    Jellyfin.Plugin.Watchlist/Projection/WatchlistProjectionPass.cs:2
    Jellyfin.Plugin.Watchlist/Projection/WatchlistProjector.cs:7
    Jellyfin.Plugin.Watchlist/Projection/WatchlistReconciler.cs:1
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs:6
    Jellyfin.Plugin.Watchlist/Users/DeletedUserHandler.cs:1
    Jellyfin.Plugin.Watchlist/Watched/WatchedRemovalHandler.cs:1

THAT PASTE HAD ALREADY STOPPED REPRODUCING BEFORE THIS CHANGE, AND ONLY ONE OF ITS
TWO NEW LINES IS MINE. It showed two files and a count of fifteen. The transfer
controller's five lines arrived with the export and import endpoints and the paste
was not taken again, so a reader running the command met four files under a sentence
naming two. The watched handler's one line is the change that made this section have
to be re-read, and re-reading it is what found the older half.

The count moved again on #87, from twenty-nine to thirty-one, and both new lines are
the controller's. They are the two refusals the administrative endpoints write when
the server's elevation policy does not answer for a caller: the one about making the
shared list names that caller's identifier, and the one about removing it names
nothing at all, because that operation is handed no caller.

It moved to thirty-nine on #19, and the one new line is the reconciler's. It is
written where a playlist has to be emptied and written back because its rows are in an
order no add can reach, and it names the playlist, the user it belongs to and how many
rows were there - no title, and nothing out of the rows themselves.

It moved to forty-one on #24, in a ninth file, and both new lines are the scheduled
pass's. THIS IS THE FIRST LINE THIS PLUGIN WRITES WITHOUT ANYBODY HAVING ASKED IT
ANYTHING, which is why it is worth reading rather than counting. One is the summary a
run owes: four numbers, being how many users it looked at, how many playlists it made,
how many playlist writes it took and how many users it stepped over. It names no user
and no item, and a test asserts the absence rather than the presence. The other says
that a run did nothing because the projection is turned off, and carries nothing at
all.

It moved to thirty-eight on #40, and all seven new lines are the shared transfer
controller's. Five are refusals - two about a caller the elevation policy does not
answer for, one about a body that is not an export, one about a version this plugin
does not know, and two about a stored document it will not read - and two are the
lines a finished import writes. Every one of them names the calling user's identifier
and counts, and none of them names a title or anything else read out of the library.

What they name is the calling user's identifier, an item identifier, the kind the
library holds an item as, an entry count, a configured maximum, and a schema version.
The watched handler's line names a count of entries and two identifiers, the user the
event named and the item that was played, and nothing out of the entries it removed.
The deleted-user handler's line names one identifier, the user the server deleted, and
it is written only where a document was there to remove, so a server deleting users
who never opened a watchlist logs nothing at all.
The projector's seven lines name a user identifier, a playlist identifier and counts of
playlists and of rows, and never the name of a list or of anything in one. Two of the
seven are said once per playlist for the life of the process rather than on every pass,
because what they report is a standing state of the server: a playlist whose label the
user changed, and a second playlist of that user carrying the configured name.
The pass that drops entries whose item can no longer be resolved reports one line with
a count, and nothing out of the entries themselves:

    grep -n 'public void NoLineNamesAnythingFromTheEntriesThemselves' \
      Jellyfin.Plugin.Watchlist.Tests/WatchlistVisibilityTests.cs
    95:    public void NoLineNamesAnythingFromTheEntriesThemselves()

One of the seven is worth stating rather than leaving to be discovered. A document this
plugin refuses to read is reported with its path, and the file name is the user's
identifier, so a refused read puts that identifier in the server log:

    grep -n 'Refusing to read {Path}' Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs
    199:                "Refusing to read {Path}: it declares watchlist schema version {StoredVersion} and this plugin understands version {UnderstoodVersion}. The list is unavailable for this user and the file is left alone.",
    214:                "Refusing to read {Path}: it declares watchlist schema version {StoredVersion} and this plugin carries no upgrade step from it to version {UnderstoodVersion}. The list is unavailable for this user and the file is left alone.",
    577:                "Refusing to read {Path}: it declares shared watchlist schema version {StoredVersion} and this plugin understands version {UnderstoodVersion}. The shared list is unavailable and the file is left alone.",
    588:                "Refusing to read {Path}: it declares shared watchlist schema version {StoredVersion} and this plugin carries no upgrade step from it to version {UnderstoodVersion}. The shared list is unavailable and the file is left alone.",

Re-taken on the change that landed the deleted-user handler, where the last two moved
from 414 and 425 because the deletion path went into the store above them, and again
on the change that landed the projector, which moved them to 499 and 510 for the same
reason: the store gained the writer that records a user's playlist above them. The paste
held two lines until the shared list gained the same two refusals, and the last two of
the four name a path that is the shared list's file rather than anybody's identifier.
All four moved up by two on the change that took a local out of the loop over the
document folder, which sits above every one of them.

The path is what makes that line actionable, since it names the file somebody has to
look at, and an identifier without the list beside it says nothing about what anyone
wanted to watch.

## The projected playlists

This is the surface a list is MEANT to become visible on, and it is where somebody
other than the owner could meet one. Nothing below has happened on any server yet, for
the reason at the end of this section, and it is written now because the code that
would do it is here and a reader deciding whether to install this plugin is deciding
about that code.

### What a projection is

One playlist per user, owned by that user, holding what is on their list. A playlist is
what a stock client already renders, which is the whole reason the projection exists:
the document under `## Where it is stored` is invisible to every client and a playlist
is not.

The plugin remembers which playlist that is and nothing else about it. What it writes
into the user's document is the identifier and the name it last set, which is described
under `## What is stored` and is two values about a playlist rather than anything about
the person.

### The plugin never shares a private one

A playlist the plugin creates is made for one user and shared with nobody. It asks the
server for a playlist by that user's identifier and by a name, and it names no other
user and no public flag:

    grep -n 'CreatePlaylist(new PlaylistCreationRequest' Jellyfin.Plugin.Watchlist/Projection/ServerPlaylistGateway.cs
    114:            .CreatePlaylist(new PlaylistCreationRequest { Name = name, UserId = userId })

What the server does with a request that names neither is a reading of the server
rather than of this tree, taken in a jellyfin checkout at the line this artifact
declares:

    git show v10.11.11:Emby.Server.Implementations/Playlists/PlaylistManager.cs | sed -n '138,145p'
                var playlist = new Playlist
                {
                    Name = name,
                    Path = path,
                    OwnerUserId = request.UserId,
                    Shares = request.Users ?? [],
                    OpenAccess = request.Public ?? false,
                    DateCreated = info.CreationTimeUtc,

So the shares are empty and the list is not open when it is made.

THE SEAM CAN NOW OPEN A PLAYLIST TO EVERYBODY, AND THIS PASSAGE SAID IT COULD NOT. It
said the seam declares nothing that changes who a playlist is shared with. It declares
one such operation, and one read beside it:

    grep -c 'Task\|IReadOnlyList' Jellyfin.Plugin.Watchlist/Projection/IPlaylistGateway.cs
    9

That number counts declarations and their return types together rather than
operations, and it is pasted as what the command prints rather than as the figure the
sentence above wants. The sentence is the list of names, which a reader checks by
opening the file.

THE SEAM CAN ALSO REMOVE A PLAYLIST NOW, which is #301 and is the count moving from
eight to nine. What it is for is the moment the shared list is removed: the playlist
that list was projected into goes with it, so a list an administrator has taken away
stops being visible to every user of the server instead of standing there under the old
name with nothing managing it. It reaches one playlist, asked for as its owner, and the
route that calls it is the administrative removal and nothing else:

    git grep -ln 'DeleteAsync(' -- Jellyfin.Plugin.Watchlist/
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs
    Jellyfin.Plugin.Watchlist/Projection/IPlaylistGateway.cs
    Jellyfin.Plugin.Watchlist/Projection/ServerPlaylistGateway.cs

No scheduled pass deletes anything, and no private playlist is reachable through it: the
only identifier it is ever handed is the one the shared record remembered.

WHAT THAT OPERATION IS FOR IS THE SHARED LIST AND NEVER A PRIVATE ONE, and the reason a
reader can hold on to is that the two lists are different targets: the private one
answers that it is not open to everybody and the shared one answers that it is, and the
pass acts on the answer rather than deciding it. A private playlist is therefore made
private and left private, which is what the paragraph above this one is about.

TWO THINGS THAT OPERATION DOES NOT DO. It grants nobody permission to EDIT a playlist -
the update it sends names no users, so the share list is not touched - and it is never
sent for a playlist that is already open, because the read beside it is asked first.
Who may add to the shared list and who may take an entry off it are the endpoints'
question, answered with the server's own word about the caller, and a playlist share
could not express that pair anyway: edit permission on a playlist is one flag covering
both.

An ADMINISTRATOR of the server can see a playlist like anything else on the server they
administer. That is the same answer this page already gives for the document on disk,
and it is said again here rather than left to be carried across.

### A playlist you already had

A playlist the user owns that already carries the configured name is taken over on the
first projection rather than duplicated, which is the behaviour the README describes
under where the list appears. Two things follow for this page.

Its rows are READ INTO the store, so items somebody put on a hand-made playlist become
entries on their watchlist. Each goes through the same describer the endpoints use, so
what is recorded is what the library says that item is FOR THAT USER, and a row they
may not see is left off. The describer is asked in exactly two places in that file, and
the second is the other direction of the same question - what the playlist should hold
for this user, asked through the gate every read path goes through:

    grep -n '_describer.Describe' Jellyfin.Plugin.Watchlist/Projection/UserProjectionTarget.cs
    257:            var described = _describer.Describe(itemId, OwnerUserId);
    367:        public bool Exists(Guid itemId) => _describer.Describe(itemId, _userId) is not null;

An entry that arrived that way records that it came from a playlist edit rather than
from an endpoint, which is one of the four values this page already says an entry
carries:

    grep -n 'Source = WatchlistEntrySource.PlaylistEdit' Jellyfin.Plugin.Watchlist/Projection/UserProjectionTarget.cs
    271:                    Source = WatchlistEntrySource.PlaylistEdit,

Adoption does not change who can see the playlist. It is the user's own list, made by
them, and the plugin takes over managing it rather than sharing it.

### The shared list has a playlist now, and it is open to everybody

THIS SAID IT HAS NONE. `## The shared list` below describes a record and its endpoints,
and until #84 that was the whole of it: nothing on the shared list reached a client.
There is a playlist now, and what is true of it is the part to read carefully, because
it is the one surface in this plugin that shows one person's addition to everybody.

The playlist is made for the administrator the record names and is then marked as one
every user of the server may SEE. It is not shared with named users, and nobody is given
permission to edit it: who may add to the list and who may take an entry off it are the
endpoints' question, answered with the server's own word about the caller.

WHAT GOES ON IT IS DECIDED BY THE OWNER'S LIBRARY ACCESS AND NOT BY THE READER'S. A
playlist holds the same rows for everybody who can see it, so there is no per-reader
answer for it to carry. An entry is projected when it resolves for the administrator
whose list it is, which means a user can see the NAME of something they may not play,
and the server refuses them when they try. That is what a list the whole server can see
is, and it is why `SharedListEnabled` is off until an administrator turns it on.

The attribution goes with it. `## The shared list` below already says an entry carries
and shows who added it; the playlist row does not carry that, so what a client shows is
the title alone and the endpoints are where the name beside it is read.

### THIS RUNS ON A SERVER NOW, AND THIS SECTION SAID IT RUNS NOWHERE

It said neither the seam nor the projector is registered and that no playlist is
created, renamed, read or adopted on any server running this plugin. Both are
registered, and the scheduled pass that drives them is too:

    git grep -l 'IPlaylistManager' -- Jellyfin.Plugin.Watchlist
    Jellyfin.Plugin.Watchlist/Projection/ServerPlaylistGateway.cs

    git grep -lE 'PlaylistGateway|WatchlistProjector' -- Jellyfin.Plugin.Watchlist/PluginServiceRegistrator.cs ; echo "exit=$?"
    Jellyfin.Plugin.Watchlist/PluginServiceRegistrator.cs
    exit=0

So everything above about what a projection reads and writes is now what a server
actually does, four times a day by default, without anybody asking. What a run of it
puts in the log is the summary under `## What reaches the server log`: four counts and
nothing else. The shared list was the exception in this section and is not any more,
which the section above it sets out.

## What leaves the server

Nothing. No part of this plugin opens a socket, builds an HTTP client or makes a web
request:

    git grep -nE 'HttpClient|WebClient|TcpClient|WebRequest|new Socket' \
      -- Jellyfin.Plugin.Watchlist ; echo "exit=$?"
    exit=1

The imports from a `System.Net` namespace are not network access:

    git grep -n 'using System.Net' -- Jellyfin.Plugin.Watchlist
    Jellyfin.Plugin.Watchlist/Api/SharedWatchlistTransferController.cs:2:using System.Net.Mime;
    Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:4:using System.Net.Mime;
    Jellyfin.Plugin.Watchlist/Api/WatchlistTransferController.cs:3:using System.Net.Mime;

Three imports rather than one, and the sentence under this said one until the transfer
controller's line was taken again, then two until the shared transfer controller
arrived. `System.Net.Mime` supplies the name of the media type each controller
declares it answers in. There is no telemetry, no usage reporting and no update check of this
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
