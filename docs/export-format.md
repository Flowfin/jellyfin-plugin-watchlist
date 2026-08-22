# The watchlist export format

This is the file this plugin writes when a list leaves it. It is written for a
reader who is not this plugin: another watchlist, a script somebody wrote, a
server this plugin has never run on.

Everything below is a promise. The shape of the plugin's own stored document is
not, and the two are separate files in the source for that reason. The stored
document is this plugin talking to itself and changes whenever the plugin does.
Where the store came from and why it looks the way it does is in
[storage-decision.md](storage-decision.md).

The sample is [samples/watchlist-export.sample.json](samples/watchlist-export.sample.json).
It is written by the test suite from a store the suite fills, and compared to the
committed file on every run, so it is what this plugin actually produces rather
than a picture of it. Reading the sample and this page should be enough to write
a reader. Nobody should have to read the plugin's source to do it.

## The whole file

One JSON object. Two members.

    {
      "FormatVersion": 1,
      "Lists": [ ... ]
    }

`FormatVersion` is the version of this format. It is not the plugin's version and
not the server's. A reader that finds a number it does not know stops and says so,
rather than reading the file as the version it does know.

`Lists` holds every list in the export, in no particular order. An export with an
empty `Lists` is a valid export of nothing, which is what a server with no lists
produces. That is not an error and a reader should not treat it as one.

## A list

    {
      "Kind": "Private",
      "OwnerUserId": "2f4a1e4c-0f9a-4a3f-8b21-000000000001",
      "ListId": null,
      "Name": null,
      "Entries": [ ... ]
    }

`Kind` is `Private` or `Shared`, written as a name and never as a number. A
private list is one user's own and is seen by that user alone. A shared list is
one the server offers to more than one user. A reader that handles only one kind
skips the other on this field, which is the whole reason it is written on every
list rather than implied by where the list sits in the file.

A reader that meets a `Kind` it does not know skips that list. It does not fall
back to either of the two it knows, because both are a claim about who may see
the list and guessing wrong on that is the one mistake this file must not invite.

`OwnerUserId` is the user the list belongs to, or `null`. A private list always
names its user. A shared list names one where the server recorded one. `null`
means the export carried no owner, not that the list has none.

`ListId` is the identifier of the list itself, or `null`. A private list is
identified by its user and carries `null`. A shared list carries whatever
identifier the server holds for it, where it holds one.

`Name` is the label the list was shown under, or `null` for a list with no name
of its own. It is for a reader to display and it is not an identity. Two exports
from two servers can carry the same name for different lists.

## An entry

    {
      "ItemId": "2f4a1e4c-0f9a-4a3f-8b21-000000000010",
      "Kind": "Movie",
      "AddedAt": "2026-03-01T09:15:00+00:00",
      "ProviderIds": {
        "Imdb": "tt0111161",
        "Tmdb": "278"
      }
    }

`ItemId` is the identifier the server this export came from used. It is included
because it is the only way to line an entry back up against the server it left,
and it is worth nothing anywhere else: the library assigns it and it does not
survive a rebuild of that library, let alone a move to another server. A reader
on another server will not resolve it and must not fail when it cannot.

`ProviderIds` is how an entry is found on a server that has never seen this one.
The keys are the provider names the server uses, for example `Imdb`, `Tmdb` and
`Tvdb`, and the values are that provider's identifier as strings. A reader should
treat the key set as open: a provider it does not know is skipped, not refused.

`ProviderIds` can be empty, and an empty one is not a malformed entry. It is what
an entry gets when the plugin could not read the item at the moment the export ran,
which is what happens once the media has been deleted from the library. The entry
still leaves. Dropping it would make the export quietly shorter than the list it
came from, and a reader comparing counts would have no way to tell.

`Kind` on an entry is what the plugin recorded when the entry was made: `Movie`,
`Series`, `Episode`, `Other`, or `Unknown` for an entry whose kind was never
written. It is a name, never a number. A reader that meets a kind it does not know
keeps the entry and treats it as `Other`, because the identifiers are what matter
and the kind is a hint about how to show it.

`AddedAt` is when the entry went onto the list, as an ISO 8601 instant with an
offset. The plugin writes UTC.

The two paragraphs above are advice to somebody writing a reader, and this
plugin's own reader does not follow them. It refuses a `Kind` it does not know,
on a list and on an entry alike, because the only files it reads are ones it
wrote and a name it does not know there is a corrupted file rather than a newer
one. A reader elsewhere is in the opposite position and should be forgiving.

## What the promise covers

A reader may rely on all of this within a `FormatVersion` it knows:

- Every member described above exists on every object it belongs to. A member
  whose value is absent is written as `null` rather than left out.
- `Kind` on a list and on an entry is a string.
- `FormatVersion` is an integer.
- No member is ever repurposed. A name that meant one thing never comes to mean
  another. If the meaning has to change, the member is left alone and a new one is
  added beside it.
- No member described above is removed inside a version.

A later version may add members, to any object here. A reader must skip a member
it does not know rather than refuse the file, and this plugin's own reader does
exactly that, which is what
`WatchlistExportFormat` sets `UnmappedMemberHandling` to `Skip` for. The suite
reads a sample with a member added to prove it.

`FormatVersion` goes up only when something a reader relied on stops being true.
Additions do not raise it, so a reader written against version 1 keeps working
against a file that carries more than version 1 described.

## What is not here, and why

There is no field saying who put an entry on a shared list. Whether that is
recorded at all, and whether it is ever shown, is not settled, and a format is
the wrong place to settle it. If it is added later it arrives as a new member
under the rule above.

There is no title, no image and no path. All three are the server's, all three go
stale the moment media is renamed or moved, and a reader that has the provider
identifiers can look up better ones than this plugin could copy.

There is no import side written against anybody's API. This is a file, and nothing
here waits on another product agreeing to read it.

This repository does hold a reader for it, which it did not when this page was
written. `WatchlistImporter` takes the entries as this format carries them and says
what each one matches on the server it is being read against:

    git log --oneline --format='%h %ad %s' --date=short origin/master -- Jellyfin.Plugin.Watchlist/Export/WatchlistImporter.cs
    517315e 2026-08-08 Match an imported entry by provider identifier before the server's own [#40]

    git grep -n 'IReadOnlyList<ExportedEntry> entries' origin/master -- Jellyfin.Plugin.Watchlist/Export/WatchlistImporter.cs
    origin/master:Jellyfin.Plugin.Watchlist/Export/WatchlistImporter.cs:41:        IReadOnlyList<ExportedEntry> entries,

That changes nothing above it. The promises on this page are made to a reader who is
not this plugin, and a reader in this repository is held to them like any other
rather than allowed to rely on what the exporter happens to write.
