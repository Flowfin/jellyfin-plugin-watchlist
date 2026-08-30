# Releasing

A release is published by pushing a tag. Nothing is created by hand.

## The tag

The tag has the form `X.Y.Z-stable` or `X.Y.Z.W-stable`, for example `1.4.0-stable`
or `0.1.0.0-stable`. The numeric part is the plugin version that Jellyfin installs,
and it must be exactly the `version` in `build.yaml`, written the same way, with the
same number of parts. The `-stable` suffix lives only in the tag and in the release
name.

## Cutting a release

1. Update `version` in `build.yaml` on the release branch and merge it.
2. Check that the commit you want to release is on that branch.
3. Push the tag for that commit:

    ```
    git tag 1.4.0-stable <commit>
    git push origin 1.4.0-stable
    ```

The `Publish Release` workflow takes it from there.

Push one tag at a time and wait for its run to finish. GitHub keeps at most one
queued run per concurrency group, and although the group here is keyed on the tag,
serialising them by hand is what keeps the release order readable.

## What the run produces

The workflow builds the plugin from the tagged commit, creates the GitHub release
for the tag, and attaches eight files:

- the plugin archive
- the packaging metadata written beside it, `<archive>.zip.meta.json`
- one `.md5` file, the checksum of the archive
- one `.sha256` file for the same archive
- the signed build provenance bundle, `<archive without .zip>.sigstore.json`
- one `.sha256` file for that bundle
- the component inventory, `<archive without .zip>.cdx.json`
- one `.sha256` file for that inventory

The `.md5` is the value a Jellyfin catalog serves as the plugin checksum. There is
exactly one per release, and the step that writes it counts the `.md5` files in the
directory afterwards and fails on a second one, so a tool added to this route later
cannot leave a catalog to choose between two. Every other asset carries a `.sha256`
instead. The archive and the metadata are checked for existence by name before the
release job runs, the bundle is checked by the same job that names it, and the
release job counts the inventories the same way it counts the bundles, so a release
missing one of the eight is not a state this route can reach.

The release notes the package carries are taken from `CHANGELOG.md`, from the
section for the version in `build.yaml`, and written into the manifest the packaging
step reads. Nothing is typed into a release by hand at that point: the section is
what ships, and the `changelog` block in `build.yaml` is a pointer at that file which
the run overwrites on the way. The notes are written in one file, and the suite
refuses a second copy of them appearing in the manifest.

The run also signs a build provenance statement for the archive, in a separate job
that downloads the archive and runs no build tooling. A downloaded archive can be
checked against it:

```
gh attestation verify <archive>.zip --repo <owner>/<repository>
```

That command reads the statement out of this repository's attestation store. The
same statement is attached to the release as `<archive without .zip>.sigstore.json`,
so a reader who has the release page has the archive and its proof in one place and
can hand the file to the same command:

```
gh attestation verify <archive>.zip --repo <owner>/<repository> --bundle <archive>.sigstore.json
```

Whether that second form reaches a verdict with no network at all has not been run
here, so nothing above claims it does.

The run also writes a component inventory for the archive, in CycloneDX, generated
from the dependency graph the release build restored in locked mode. It carries
every package that graph resolved, the version and the licence each package
declares, and the files the archive actually ships with a digest per file. The two
are separate sets and the document says which is which: both package references in
this plugin exclude their runtime assets, so the graph is what the plugin was
compiled against and `scope` on each component says whether the archive carries it.
[README.md](../README.md), under `## Checking a release`, has the commands.

The step that writes it refuses on more than an absent file. It compares the
assemblies inside the archive against the packages in the document, and a `.dll`
that no package accounts for stops the run. Such a file is not missing from the
document: it is in it, as a name and a digest with no version and no licence
beside them, which is the shape a release would publish while looking exactly like
one that says what it ships.

Nothing here writes a plugin catalog. A GitHub release is the whole output. If this
repository previously published through the Jellyfin meta plugins workflow, that path
is gone and no catalog is fed until a manifest generator is added.

## The package this route builds, and the one the gate builds

Both are made by the same packaging tool at the same pin, on the same runner image,
against the same framework, and neither hands the tool a version - both read it out
of `build.yaml`. They are not the same build, and where they differ is written at
the top of `.github/workflows/build.yaml` rather than here, because a second copy of
that comparison goes stale against the workflow that decides it:

```
grep -n 'What differs is the restore' .github/workflows/build.yaml
```

Read it before treating a green `call / build` as evidence about the archive a user
receives. The short of it is that the gate's leg lets the packager restore for
itself and this route restores in locked mode first, so the release archive is built
from the graph `packages.lock.json` records and the gate's is not.

## What the artifacts are named, and what the name does not say

The packaging tool names the archive, from two values this repository writes and
nothing else: the `name` key in `build.yaml`, lowercased, then an underscore, then
the `version` key. Read off a real run of the gate's packaging leg rather than off
the tool's documentation:

```
gh run download 33315016709 --repo Flowfin/jellyfin-plugin-watchlist -n build-artifact
ls
watchlist_0.1.0.0.zip
```

Everything else a release carries is named after that archive: the packaging
metadata is `<archive>.meta.json`, the checksums are `<archive without .zip>.md5`
and `.sha256`, and the provenance bundle and the inventory take the same stem. So
there is one name in a release and the rest are derived from it, which is what
keeps a catalogue reading the checksum by filename from picking the wrong file.

**The name does not say which server line the archive is built for.** That is
stated here rather than left to be discovered, because the version alone is what
the name carries. The line is inside the archive, in the packaged metadata, and it
is the value `build.yaml` declares:

```
unzip -p watchlist_0.1.0.0.zip meta.json
    "targetAbi": "10.11.0.0",
```

**The rule for when there are two.** A release ships one artifact today, so there is
nothing for a name to distinguish it from and a line marker in it would say nothing
a reader could act on. The second artifact arrives with #4, which is a second
manifest with its own `targetAbi` and its own framework, and the day it does the two
archives cannot both be `watchlist_<version>.zip`. What distinguishes them is
decided there, with the packaging step and the catalogue that reads it in front of
whoever decides, rather than guessed here while one of the two does not exist. That
is the whole remainder, and it is named so that a reader of the paragraph above does
not take one artifact for a naming scheme that was designed for two.

## What is in the archive

Two files, and neither of them is from the build tree:

```
unzip -l watchlist_0.1.0.0.zip
      930  2026-08-30 13:45   meta.json
   130048  2026-08-30 13:45   Jellyfin.Plugin.Watchlist.dll
```

The publish output of that project holds four files - the assembly, a `.pdb`, a
`.deps.json` and an XML documentation file - and the packaging tool takes only the
ones the `artifacts` sequence in `build.yaml` names, which is the assembly. The
suite refuses a sequence naming anything this repository does not build, and the
release route refuses an assembly in the archive that no package in the inventory
accounts for, so a dependency that started shipping stops the run rather than riding
along.

## Building the same tag twice

**The assembly is reproducible for a given checkout path and is not reproducible
across two.** Measured with `sha256sum` over `dotnet publish -c Release -f net9.0`,
rather than argued from the compiler's defaults:

```
two clean builds of one commit at ONE path
  fc4319cef6c11c596dd28277a622c774ff96878212bfdbaec1c141ba308ab9b7 (twice)
the same commit unpacked at two different paths
  45a53ba268f88a68037f1c40233ef36e079300bef361be826acf1615d526215d
  18539d606ae8a2b2959a472ebc9cf652c1c34d49c047e82a2414989e38dd3529
```

What differs is the checkout path, which the compiler writes into the assembly, and
nothing about the source. `-p:PathMap=<root>=/_/` removes the difference, measured
at the same two paths:

```
eee0137a07269d22eddc4ad963a52a95c0eaeb7621e6aa80426026137324ce9c (both)
```

**It is not set, and the reason is measured rather than a preference.** With that
property on the shipped project, the coverage floor stops reporting the module at
all: coverlet prints an empty table and the run fails at 0% against a floor of 100.
Both readings are `dotnet test --configuration Release -p:CollectCoverage=true`, on
the same tree, with and without it:

```
without           | Jellyfin.Plugin.Watchlist | 100% | 100%   | 100%   |
with              | Module | Line | Branch | Method |   <- no row at all
                  error : The minimum line coverage is below the specified 100
```

`-p:DeterministicReport=true`, which exists for exactly this pairing, moved neither
reading. So the choice is between an artifact reproducible across machines and a
coverage floor that reports, and the floor is a required context while cross-machine
reproducibility is nothing this repository has been asked for.

**The archive can never repeat its bytes, whatever the assembly does**, because the
packaging tool writes the moment of the run into the metadata it puts inside:

```
unzip -p watchlist_0.1.0.0.zip meta.json
    "timestamp": "2026-08-30T13:45:48Z",
```

So two builds of one tag produce the same assembly on the same path and two
different archives, always. A reader comparing releases compares the assembly and
the inventory, not the zip.

## What fails the run

- The tag does not end in `-stable`, or the workflow was started from something
  other than a tag.
- The numeric part of the tag differs from `version` in `build.yaml`.
- `build.yaml` is missing a required field, or `version`, `targetAbi`, `framework`
  or `guid` has the wrong shape.
- `framework` in `build.yaml` names a target the plugin project is not built for.
- A packaging manifest that shadows `build.yaml` is present, such as `jprm.yaml` or
  `meta.yaml`.
- `build.yaml` declares an `image` file that is not in the repository.
- The tagged commit is not contained in a release branch, or the tag was moved after
  the run started.
- There is no `packages.lock.json` next to the plugin project, so the release build
  cannot restore against a reviewed dependency graph. Create one with
  `dotnet restore <project> -p:RestorePackagesWithLockFile=true` and commit it.
- The version stamped into the assembly is not the version in `build.yaml`.
- `CHANGELOG.md` is missing, or carries no section for the version in `build.yaml`.
  The release notes in the package are taken from that section, so a version with
  no section has nothing to ship.
- `build.yaml` has no folded `changelog: >` block for the notes to be written into.
- The build produced no archive, or more than one, or no packaging metadata.
- The attestation step reported no bundle on disk, so there is nothing to attach as
  the archive's proof.
- The inventory generator wrote nothing, wrote something that is not a CycloneDX
  document, or wrote one that does not name the archive it describes.
- The archive ships an assembly no package in the component inventory accounts for.
- The project reports no `AssemblyName`, so the file the archive is built around
  cannot be told apart from a dependency that shipped beside it.
- The release job found no attestation bundle, or more than one, or no component
  inventory, or more than one.
- More than one `.md5` ended up in the directory the release is assembled from.
- A release already exists for the tag.

All of these fail before anything is published.

## What the run notes without failing

The packaging tool warns when `build.yaml` declares neither `image` nor `imageUrl`.
The plugin then shows without a logo in a catalog. That is a warning on every run
until a logo exists, and it is not a reason to hold a release.

## Re-running

A release that exists is not touched again. The release job asks whether a release
exists for the tag before it writes anything and stops if one does, and the upload
step is configured not to replace an asset of the same name. Replacing the bytes of a
version people have already installed is the failure this prevents, and it is worth
more than the convenience of a re-run.

So: if a release went out with the wrong contents, fix the problem, raise the version
in `build.yaml`, and push a new tag.

If a run failed **before** the release was created, the tag is still clean. Fix the
cause and re-run the workflow from the Actions page, or delete and re-push the tag.

If a run failed **after** the release was created but before every asset was attached,
the release is incomplete and a re-run will refuse it. What is possible then depends
on the repository settings below. Without immutable releases you can delete the
incomplete release, delete the tag, and push it again. With immutable releases you
cannot, and the version has to be raised.

## Repository settings this expects

- Default workflow permissions set to read only.
- A rule that restricts who may push `*-stable` tags.
- The `ABI floor build` check required on the release branches.
- Immutable releases, if the repository wants the guarantee that a published release
  can never be edited or deleted at all. The workflow does not depend on it: the
  refusal to touch an existing release is enforced in the release job. Turning it on
  removes the only recovery path for an incomplete release, so try it on one
  repository and cut a release there before turning it on everywhere.
