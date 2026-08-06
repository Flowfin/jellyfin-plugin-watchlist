# The language this plugin's own text ships in

English, and English only, for 1.0. That covers the configuration page, every
log line the plugin writes, and every string it returns to a caller. A reader who
expects other languages to arrive should read this file rather than assume they
are on their way.

## What the server actually offers a plugin

The issue that asked this question said the server has a translation mechanism
for plugin pages. What the server has is a localisation manager, and the
dictionary it reads is its own:

    git show v10.11.11:Emby.Server.Implementations/Localization/LocalizationManager.cs | grep -n 'const string Prefix = "Core";'
    410:            const string Prefix = "Core";
    git show v12.0-rc4:Emby.Server.Implementations/Localization/LocalizationManager.cs | grep -n 'const string Prefix = "Core";'
    410:            const string Prefix = "Core";

The dictionary is built from resources under that manager's own namespace, and a
phrase it does not hold comes back unchanged:

    git show v10.11.11:Emby.Server.Implementations/Localization/LocalizationManager.cs | sed -n '398,403p'
            if (dictionary.TryGetValue(phrase, out var value))
            {
                return value;
            }

            return phrase;

A plugin's configuration page reaches the server through a different surface,
which hands over pages and not phrases:

    git show v10.11.11:MediaBrowser.Model/Plugins/IHasWebPages.cs | grep -n 'IEnumerable<PluginPageInfo>'
    9:        IEnumerable<PluginPageInfo> GetPages();

So on the server side there is no route by which a plugin page's text is
translated. Whether the web client translates anything of its own that ends up
around this page was not measured here, and nothing in this file claims it does
or does not.

## Why English only

There is nobody to translate into. A translation surface with one language in it
is a mechanism that is never exercised, and an unexercised mechanism is the one
that is broken when the second language finally arrives. Shipping one language
and saying so leaves the next person a plain statement instead of a half-built
route they have to reverse-engineer first.

The cost is real and it is not hidden. A reader whose server runs in another
language meets an English page and English log lines, and nothing in the product
tells them that is deliberate. The README carries the sentence for exactly that
reason.

What would change the answer: somebody offering a translation, or the web client
gaining a route a plugin page can hand strings to. Either is a reason to reopen
this. The work sits on #123, in the milestone after 1.0, and its first job is to
measure what the web client offers rather than to build against a guess.

## What this costs the code now

A later translation must not begin with a rewrite of every string, so no text a
user sees is built by joining fragments together. That is true of the tree today
rather than a promise about it.

The page is one static sentence and carries no script at all:

    git grep -c '' -- Jellyfin.Plugin.Watchlist/Configuration/configPage.html
    Jellyfin.Plugin.Watchlist/Configuration/configPage.html:16
    git grep -n '<script' -- Jellyfin.Plugin.Watchlist/Configuration/configPage.html ; echo "exit=$?"
    exit=1

Every log line the plugin writes is a message template with named placeholders,
which is one whole string per message with the values filled in by the logger
rather than by string concatenation:

    git grep -nE '_logger\.Log[A-Za-z]+\(' -- Jellyfin.Plugin.Watchlist
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs:132:            _logger.LogError(
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs:147:            _logger.LogError(
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs:292:            _logger.LogWarning(

Three call sites, three templates. Nothing refuses a fourth one written by
joining pieces: this is a convention held by a reader, not by a check, and the
tree is small enough today that the reading above is the whole surface. It stops
being the whole surface the moment the configuration page grows fields, which is
#31, and the endpoints start returning text, which is M4.
