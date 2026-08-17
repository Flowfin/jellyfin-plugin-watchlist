# Coexisting with a server that grows its own watchlist

## The position

This plugin keeps its own store and its own projection on every server line it
supports, including a line whose server has a watchlist of its own. It never
writes into the server's own list, it does not refuse to load when one is there,
and it carries no migration written against an interface that has not shipped.

Nothing in that position waits on the upstream work landing. If a server release
arrives with a native watchlist, this plugin behaves on that server exactly as it
does on one without.

## What the upstream work is, as measured

The feature has been asked for and attempted more than once, and the current
attempt is open rather than released:

    gh pr view 17504 --repo jellyfin/jellyfin --json state,title --jq '"\(.state) | \(.title)"'
    OPEN | Add watchlists feature

It is not modelled as a watchlist by itself. It adds a general list of items with
a type, and a watchlist is one of those types:

    gh pr view 17504 --repo jellyfin/jellyfin --json files \
      --jq '[.files[].path] | map(select(test("ItemList";"i"))) | length'
    17
    gh pr view 17504 --repo jellyfin/jellyfin --json files \
      --jq '[.files[].path] | map(select(test("ItemList";"i"))) | .[0:4] | .[]'
    Jellyfin.Server.Implementations/Events/Consumers/Users/ItemListProvisioner.cs
    Jellyfin.Server.Implementations/Library/ItemListManager.cs
    Jellyfin.Server/Migrations/Routines/20260801120000_CreateDefaultItemLists.cs
    MediaBrowser.Controller/Library/IItemListManager.cs

Both readings are of a pull request that is still open, so they are the shape on
the day they were taken and not a description of anything released.

That pull request's own body says the client work is a separate change that comes
afterwards. So the first server release carrying a native watchlist still shows
nothing on a client until each client ships support for it, which is the gap this
plugin covers and the reason coexistence costs nothing today.

## How a user tells the two apart

This plugin's list appears as a playlist owned by the user, under the name the
configuration carries, in the place every stock client already renders playlists.
That is the whole client surface it has. Anything this plugin's settings change
is that list and only that list.

A native watchlist has no client surface yet, per the paragraph above, so today
there is nothing on a client for it to be confused with. What it will be called
where a user meets it cannot be measured now, so no name is given for it. When a
client ships support, the answer to write here is what each one is called and
where each one appears.

## The name of the projected playlist

The rule: the projected playlist takes a name a server's own list would not take,
and the default says which plugin made it rather than claiming the generic word.
Two lists both called "Watchlist" in one client is the failure this rule exists
against, and it is a failure a user cannot resolve by looking, because a playlist
does not say what created it.

The rule belongs next to the setting that carries the name, so that somebody
changing the name meets the reason at the moment they change it. That setting is
not in the tree yet. It is one of the server-wide settings, and it arrives with
the configuration surface. Until it does, this file is the only place the rule
exists, and a person who lands that setting without carrying the rule to it
breaks nothing that a machine would notice.

That gap is stated rather than closed here, and #42 stays open on it.

## The route out

Somebody who later prefers a native list is not held here. The export format is
`docs/export-format.md`, and it names items by provider identifiers as well as by
the server's own, so it can be read by something that is not this plugin and on a
server that is not the one it came from. Getting out does not require this plugin
to have written the migration, which is what lets the position at the top of this
file be honest.
