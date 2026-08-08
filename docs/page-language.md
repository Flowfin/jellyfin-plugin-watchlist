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
translated.

## What the web client does with the page

The web client is a second reader of the same page, and it was not measured when
this file first landed. It has been now, and the answer is a narrow route rather
than an absence.

The readings below are taken in a `jellyfin-web` checkout, at the tag of the
current stable line and at the newest candidate of the next one, because this
plugin supports both.

The client routes a plugin's configuration page through a component that fetches
the page from the server and translates it before it is shown:

    git show v10.11.11:src/apps/dashboard/routes/routes.tsx | grep -n 'configurationpage'
    15:    PluginConfig: 'configurationpage'
    49:                        element: <ServerContentPage view='/web/configurationpage' />

    git show v10.11.11:src/components/ServerContentPage.tsx | sed -n '37,39p'
                        // Fetch the view html from the server and translate it
                        const viewHtml = await apiClient?.get(apiClient.getUrl(view + location.search))
                            .then((html: string) => globalize.translateHtml(html));

The next line does the same thing at the same lines:

    git show v12.0-rc4:src/components/ServerContentPage.tsx | sed -n '37,39p'
                        // Fetch the view html from the server and translate it
                        const viewHtml = await apiClient?.get(apiClient.getUrl(view + location.search))
                            .then((html: string) => globalize.translateHtml(html));

What the translation does is replace every `${Key}` in the page with a value
looked up in one dictionary:

    git show v10.11.11:src/lib/globalize/index.js | sed -n '246,251p'
    export function translateHtml(html, module) {
        html = html.default || html;

        if (!module) {
            module = defaultModule();
        }

    git show v10.11.11:src/lib/globalize/index.js | sed -n '267,271p'
        const key = html.substring(startIndex, endIndex);
        const val = translateKeyFromModule(key, module);

        html = html.replace('${' + key + '}', val);
        return translateHtml(html, module);

Which dictionary that is was decided before any page loaded, and it is the
client's own:

    git show v10.11.11:src/lib/globalize/loader.ts | grep -n 'defaultModule'
    5:    globalize.defaultModule('core');
    git show v12.0-rc4:src/lib/globalize/loader.ts | grep -n 'defaultModule'
    5:    globalize.defaultModule('core');

A key that dictionary does not hold comes back as the key itself, so a page
asking for a phrase the client was not already written against renders the key
text at the reader:

    git show v10.11.11:src/lib/globalize/index.js | sed -n '229,235p'
        if (!dictionary || isEmpty(dictionary)) {
            console.warn('Translation dictionary is empty.');
        } else {
            console.error(`Translation key is missing from dictionary: ${key}`);
        }

        return key;

Nothing lets a plugin installed into the server add a dictionary of its own.
Strings are registered in one function, and it is reached from two places:

    git grep -n 'loadStrings(' v10.11.11 -- src | grep -v 'globalize/index.js'
    v10.11.11:src/components/pluginManager.js:24:    #loadStrings(plugin) {
    v10.11.11:src/components/pluginManager.js:26:        return globalize.loadStrings({
    v10.11.11:src/components/pluginManager.js:39:            return this.#loadStrings(plugin);
    v10.11.11:src/lib/globalize/loader.ts:6:    return globalize.loadStrings({

`loader.ts` is the client loading its own strings. `pluginManager` is the client
side plugin system, which is a different thing from a server plugin. The list it
works from is served with the web client rather than by the server:

    git show v10.11.11:src/index.jsx | sed -n '145p;161p'
        let list = await getPlugins();
            await Promise.all(list.map(plugin => pluginManager.loadPlugin(plugin)));

    git show v10.11.11:src/scripts/settings/webSettings.js | grep -n "fetchLocal('config.json'"
    9:        const response = await fetchLocal('config.json', {

and what it holds are the players and screensavers the client itself ships:

    git show v10.11.11:src/config.json | tr ',' '\n' | grep 'plugin' | tr -d ' "'
    plugins:[
    playAccessValidation/plugin
    experimentalWarnings/plugin
    htmlAudioPlayer/plugin
    htmlVideoPlayer/plugin
    photoPlayer/plugin
    comicsPlayer/plugin
    bookPlayer/plugin
    youtubePlayer/plugin
    backdropScreensaver/plugin
    pdfPlayer/plugin
    logoScreensaver/plugin
    sessionPlayer/plugin
    chromecastPlayer/plugin
    syncPlay/plugin

So the route is: a configuration page may use `${Key}` and will get the reader's
language for any key the web client already carries, and nothing else. This
plugin has no way to put a phrase of its own into that lookup, on either
supported line.

The reading is of the client's sources and not of a running browser. What a page
does when the dictionary has not finished loading, and what the two lines do
differently at runtime, are not measured here.

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
gaining a route a plugin page can hand strings of its own to. Either is a reason
to reopen this. The narrow route the section above measures is not that second
thing. It carries keys the client already holds, so a page built on it says only
what the client's own screens already say, and the sentences this plugin needs
are not among them.

The work sits on #123, in the milestone after 1.0. Its first job was to measure
what the web client offers rather than to build against a guess, and that
measurement is the section above.

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
