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

## 0.1.0.0

The first version anybody can install. Published on 2026-09-03 as `0.1.0.0-stable`, from `28d6a70`. There is nothing to upgrade from and nothing a user has to do.

THE NOTES THAT RELEASE CARRIES SAY SOMETHING ELSE, AND THIS PARAGRAPH IS WHY. On the day the tag was pushed this section read "Not released. Nothing has been published from this repository yet, so there is nothing to install, nothing to upgrade from, and nothing a user has to do." The publish route packages this section as the release notes, and a published release is not rewritten, so that is the sentence a catalogue shows for this version. The section is corrected here for a reader of this file; the shipped copy stands.
