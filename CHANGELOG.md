# Changelog

What changed for somebody using this plugin, one section per version, newest
first. It is written for a reader who installs the thing, not as a list of
commit subjects: a person reading this wants to know what is different and what
they have to do about it.

The version in `build.yaml` is the version this file has to carry a section for.
The suite refuses a declared version with no section here, and the publish route
takes the packaged release notes from that section, so this file is the only place
the notes are written. The `changelog` block in `build.yaml` is a pointer at this
file rather than a copy of it, and the suite refuses a copy written there.

## 0.1.1.0

Installs on every 10.11 server the manifest promises, which 0.1.0.0 did not.

That release declares `targetAbi 10.11.0.0`, the floor of the 10.11 line, and its assembly binds the server libraries at `10.11.11.0`, because the build compiled against the newest package on the line instead of the one the floor names. A server between the two offers the plugin on the strength of the declared floor and then refuses every type in it, so the plugin appears in the catalogue, installs, and shows as `NotSupported` with
`Could not load file or assembly 'MediaBrowser.Common, Version=10.11.11.0'` in the server log.

Read on two containers before and after: 10.11.0 answered `NotSupported` for 0.1.0.0 and `Active` for this build, and 10.11.11 answered `Active` for both.

Nothing about the plugin's behaviour changed. If you are on 10.11.11 or newer there is nothing here for you; if your server is older than 10.11.11, this is the first version you can use.

## 0.1.0.0

The first version anybody can install. Published on 2026-09-03 as `0.1.0.0-stable`, from `28d6a70`. There is nothing to upgrade from and nothing a user has to do.

THE NOTES THAT RELEASE CARRIES SAY SOMETHING ELSE, AND THIS PARAGRAPH IS WHY. On the day the tag was pushed this section read "Not released. Nothing has been published from this repository yet, so there is nothing to install, nothing to upgrade from, and nothing a user has to do." The publish route packages this section as the release notes, and a published release is not rewritten, so that is the sentence a catalogue shows for this version. The section is corrected here for a reader of this file; the shipped copy stands.
