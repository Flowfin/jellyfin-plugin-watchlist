# Security policy

This is a Jellyfin plugin. It is loaded into a media server that somebody else
runs, on a machine I have no access to, and it keeps one private watchlist per
user of that server. The question this policy is mostly about is therefore a
narrow one: whether a user of such a server can reach another user's list, or
learn something about a library they were never given.

## Reporting a vulnerability

Report privately, through the advisory form on this repository:

    https://github.com/Flowfin/jellyfin-plugin-watchlist/security/advisories/new

That door opens today, and that is a reading rather than an assumption:

    gh api repos/Flowfin/jellyfin-plugin-watchlist/private-vulnerability-reporting
    {"enabled":true}

Run 2026-08-19. Everything else about this repository is public, the issue
tracker included, so a report describing a way to read somebody else's list
belongs in that form and not in an issue. The form is already private between
the two of us, which is why I publish no key: a key nobody has verified adds a
step without adding a guarantee.

I promise no acknowledgement deadline and no fix deadline. A deadline this
project cannot keep is worse than no deadline at all, because a reporter who was
told to expect an answer within a stated time and does not get one cannot tell a
busy week from a report that never arrived, and removing that guess is the only
thing a deadline was for. Every report is answered, including the ones that turn
out not to be problems, and those get the reason. Credit goes to the reporter
unless they would rather not have it.

## What exists to be attacked

Less than the README describes, and saying so is part of the policy. What is
built is a per-user document store, three HTTP endpoints over it, a
configuration page carrying one setting, and export and import code that no
endpoint reaches. The playlist projection that would make a list visible in a
client is not built, and neither is the shared list. There is no release and no
tag:

    gh api repos/Flowfin/jellyfin-plugin-watchlist/releases --jq length
    0

So nothing from here is running on anybody's server except where somebody built
it themselves, a report is against `master`, and there is no older version to
carry a fix back to.

## Where the surface really is

The reading of who a request is from, in
`Jellyfin.Plugin.Watchlist/Api/CallingUser.cs`. It is one static method reading
one claim, and the private half of this plugin rests entirely on it. No endpoint
takes a user identifier in a route, a query or a body, so there is deliberately
no spelling of any request that names somebody else. Anything that makes a
request answer for a user other than the one it came from is the finding this
repository cares about most: a way to supply or influence that claim, a path
that skips the check, a way through the all-zero identifier the method refuses
precisely because it parses.

The three endpoints in `Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs`,
which are `GET Watchlist/Items`, `POST Watchlist/Items/{itemId}` and
`DELETE Watchlist/Items/{itemId}`. The controller carries the server's default
authorisation and nothing narrower, meaning an authenticated user of that server
and no further permission. A reachable path that answers without an
authenticated user, or a plugin route that shadows one of the server's own, is
in scope.

What a refusal gives away. An item that is not in the library and an item this
caller may not see are answered identically, and no refusal from this plugin
carries a body. If a change makes those two distinguishable, by status, by
timing or by anything else, these endpoints become a way to ask what sits in a
library somebody has no access to, one identifier at a time. A report showing
such a distinction is a report about a disclosure, not about an error message.

The visibility check in `Jellyfin.Plugin.Watchlist/Api/LibraryItemDescriber.cs`,
which is the only place this plugin asks the server whether a user may see an
item. Getting past it matters twice over, because what a list hands back for an
entry carries the item's name, year, series name, season and episode number.

The document paths in
`Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs`. A file name is the
user's identifier in its 32-character hexadecimal form with a `.json` suffix,
and the method that builds it takes a `Guid` and nothing else, so no string a
caller supplies reaches the name and it carries no separator and no parent
reference. Any way to make that store read or write outside the folder handed
to it, or to make one user's write land in another user's document, is a
finding.

`Jellyfin.Plugin.Watchlist/Export/WatchlistImporter.cs`, the only code here that
consumes values a different server wrote. No endpoint feeds it today, so it is
not remotely reachable, but it is the one piece where untrusted input is the
point rather than an accident, and a reading of it now is worth more than one
after the endpoint lands.

The release route, meaning the workflows under `.github/workflows` and the
attestation and component inventory the README tells a reader to verify. A way
to get something into a published archive that the attestation would still cover
is a finding about every future install at once.

## What is not a vulnerability here

Anyone with file access to the server can read every list. The documents are
plain JSON under the plugin's data folder, neither encrypted nor obfuscated, and
`docs/personal-data.md` says so in as many words. An administrator, a backup, a
container snapshot and anyone with a shell on that machine already have it. A
plugin holding a key on the same disk as the file the key protects has protected
nothing and has added a way to lose the list.

User identifiers reach the server log, including the path of a document this
plugin refuses to read, which is a file named after its user. That is
deliberate: the path is what makes the line actionable for whoever has to go
look at the file, and an identifier with no list beside it says nothing about
what anybody wanted to watch. No title is ever logged. One value read out of the
library does reach a line: the refusal of an add names the kind the library
holds the item as, because that kind is the reason for the refusal. A title in
the log, or anything else out of the library beside that kind, would be a real
finding.

The plugin adds no transport security, no rate limiting and no session handling
of its own. It opens no socket at all, and every request it sees has already
passed the server's own authentication. Those layers belong to the server and to
whatever sits in front of it, and a second answer here would only be an answer
to disagree with the first.

A defect in Jellyfin itself is not mine to fix or to embargo. It belongs to
[the Jellyfin project](https://github.com/jellyfin/jellyfin/security/policy). I
will read such a report and point it the right way rather than close it, but the
fix does not live in this repository.

A dependency alert with no path from this plugin's code to the flaw. Dependency
review, CodeQL and Dependabot already run here, so an unreachable advisory is
already in front of me. What I cannot see without you is a reachable one, and
that is worth sending even when the score attached to it is low.

The parts that do not exist. The shared list, the playlist projection and the
export and import endpoints are unbuilt, and a report about what they might one
day do wrong is a design comment rather than a vulnerability. Those are welcome,
in the tracker, in the open.

A stored document corrupted or hand-edited into a shape the reader refuses will
throw out of the store rather than answering with the unavailable list the API
documentation describes. That is a robustness bug and belongs in the tracker,
because producing it needs the file access that already reads every list, so it
hands an attacker nothing they did not have.
