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

The work sat on #123, in the milestone after 1.0. Its first job was to measure
what the web client offers rather than to build against a guess, and that
measurement is the section above. What the measurement found is that there is no
route this plugin's own text can travel, on either supported line, so #123 is
closed as not planned rather than left open as a wish. The narrow route is not a
partial version of the thing it asked for: a page built on it can say only what
the client's own screens already say, and none of the sentences this page needs is
among them. Nothing about that is a promise it will never exist. The condition
that reopens it is written two paragraphs above and it is a change in the web
client, not a change here.

## The log lines, which are answered separately

A page is read by a user and a log line is read by an operator, so the two are
answered separately here rather than covered by one sentence at the top of this
file. They happen to give the same answer today and they give it for different
reasons, and the reasons are what decide whether either one moves later.

The page is English because there is no route its sentences can travel and nobody
to translate them. If the web client grows a way for a plugin to hand it strings,
that answer is worth taking up again.

The log lines are English because a log line is a thing somebody searches for.
An operator meets one in a file, and more often pastes it into a question
somebody else has to answer from a different machine in a different language. A
translated log line breaks that: the person helping cannot search for the text
they are shown, and the same failure reads as two unrelated failures depending on
where each reader's server was configured. That reason does not depend on a route
existing, so the answer for log lines does not move if the client gains one. It
would move only if the plugin ever wrote a line meant for a user rather than for
an operator, and no line it writes today is.

That is the whole of the difference. The page's answer carries a condition that
would reopen it. The log lines' answer does not.

## What this costs the code now

A later translation must not begin with a rewrite of every string, so no text a
user sees is built by joining fragments together. That is true of the tree today
rather than a promise about it, and one part of it is now refused by the suite
rather than held by care.

Every reading below was taken at `b877b47`.

This section said until now that the page is one static sentence carrying no
script. It is not, and it has not been since #31:

    git grep -c '' b877b47 -- Jellyfin.Plugin.Watchlist/Configuration/configPage.html
    b877b47:Jellyfin.Plugin.Watchlist/Configuration/configPage.html:104
    git grep -n '<script' b877b47 -- Jellyfin.Plugin.Watchlist/Configuration/configPage.html
    b877b47:Jellyfin.Plugin.Watchlist/Configuration/configPage.html:66:        <script type="text/javascript">

Sixteen lines and no script were true when they were pasted here and stayed on
the page after the file they describe had grown. A count of a file moves whenever
the file does, so it is a poor thing to hang a property on. What the count was
standing in for is that no sentence a user reads is assembled from pieces, and
that is still true with the script there. The script reads one number out of the
configuration and writes one back; it builds no text at all:

    git grep -n "innerHTML\|textContent\|+ '" b877b47 -- Jellyfin.Plugin.Watchlist/Configuration/configPage.html ; echo "exit=$?"
    exit=1

The page also carries nothing in the one form the web client rewrites, and that
half no longer depends on anybody re-reading it.
`ThePageIsWrittenInNoFormTheWebClientRewrites`, in
`Jellyfin.Plugin.Watchlist.Tests/ConfigurationPageTests.cs`, refuses a `${...}`
anywhere in the page as it ships. It carries a near miss for each spelling
somebody would actually write: one inside the script, where a template literal is
substituted away before the script that would have filled it ever runs, and one
in the markup, where a label written as a key renders as the bare key in every
language including English. It carries the other direction too, so a dollar sign
or a brace on its own is not refused.

Every log line the plugin writes is a message template with named placeholders,
which is one whole string per message with the values filled in by the logger
rather than by string concatenation:

    git grep -cE '_logger\.Log[A-Za-z]+\(' b877b47 -- Jellyfin.Plugin.Watchlist
    b877b47:Jellyfin.Plugin.Watchlist/Api/WatchlistController.cs:4
    b877b47:Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs:3

Seven call sites at that commit, where this section said three until now. That
number was pasted when the store was the only part of the plugin that logged, and
the endpoints of M4 have arrived since. Nothing refuses an eighth written by
joining pieces: this is a convention held by a reader and not by a check, and the
line above is a reading of one commit rather than a bound on the tree. It is
quoted here because the reason for it is worth stating, not because the number
means anything after the next endpoint lands.
