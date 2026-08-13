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
what ships, and the copy in `build.yaml` is overwritten before the package is built.

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
