# The formatter and the invariant lint

Two checks the other board runs and this board owed an answer on. One is refused
and one is adopted, and both answers are here with what would change them.

## The formatter: refused

The other board runs a formatter over its non-code files. This tree's non-code
surface is not the same surface.

    git ls-files | grep -oE '\.[a-z]+$' | sort | uniq -c | sort -rn
         47 .cs
         12 .json
         11 .yaml
         11 .txt
         11 .md
          5 .yml
          2 .csproj
          1 .sln
          1 .ruleset
          1 .props
          1 .html
          1 .gitignore
          1 .editorconfig

The markdown is argument rather than output. `docs/` holds the reasons decisions
were taken, wrapped by hand at a width a person chose, and a formatter rewraps
prose it did not write. The cost of that is not aesthetic: once a paragraph is
rewrapped, every later edit to it shows as a whole-paragraph diff, and the file
where the reasons live becomes the file whose diffs nobody reads.

The JSON is not hand-written output either. Two of those files are the lock
files, which are written by the restore and read by a check that compares them
with the project files, four are document fixtures whose bytes are the thing
under test, and one is the export sample a test reads. Reformatting a generated
file makes a check about drift argue with the formatter instead, and reformatting
a fixture edits the evidence.

What is left is one HTML page of sixteen lines and the workflows, and a check
that exists to hold sixteen lines and some YAML is a check whose failures are
noise.

What would change the answer: the configuration page growing a script or a
stylesheet, which is #31, or a hand-edited JSON surface appearing that a reader
has to diff. Either puts real formatted-by-hand output in the tree and the row
in `docs/parity.md` says so.

## The invariant lint: adopted, in the shape this tree already has

Adopted, with one invariant, and it lives in the suite rather than in a workflow
of its own.

The invariant: the store is the only part of this plugin that touches the file
system. It is true of the tree today rather than an aspiration, and it is one
grep to say so:

    git grep -lE '\bFile\s*\.\s*[A-Z]|\bDirectory\s*\.\s*[A-Z]|\bPath\s*\.\s*[A-Z]' -- Jellyfin.Plugin.Watchlist
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs

What breaks when it stops being true is not hypothetical. The store writes a
document by staging it and moving it into place, it resolves its folder once and
derives every path from that, and it is the one place a bound on where this
plugin may write is enforced. A second place that owns a path is a second answer
to "where does a user's list live", and the first thing anybody notices is a list
that is somewhere else after an upgrade.

Where it runs, and why not in a workflow. The tree already carries a scanner for
exactly this shape, the headless guard, with a rule table, a register of declared
departures that fails in both directions, and fixtures that prove a rule bites.
The invariant lint is that scanner pointed at the plugin's sources with a table of
its own. Reusing it costs one table, one register and one test class; a workflow
would cost a second scanner, a second way to declare a departure, and a check that
runs in the gate but not on a contributor's machine. It reports inside the suite,
which is already a required context, so nothing new goes to #63.

Proven to fire, and on the real tree rather than only on a fixture. One method
added to the exporter, of the kind somebody writes when the export needs a
destination:

    public static string ExportPath(string folder) => System.IO.Path.Combine(folder, "watchlist-export.json");

    dotnet test Jellyfin.Plugin.Watchlist.sln -c Release --filter "FullyQualifiedName~InvariantGuardTests"
       An invariant is broken in the plugin's own sources:
    Export/WatchlistExporter.cs:102 [store-filesystem] builds a file path outside the store (Path.Combine()
    Failed!  - Failed:     1, Passed:     5, Skipped:     0, Total:     6

and silent on the tree with that one method removed:

    git checkout -- Jellyfin.Plugin.Watchlist/Export/WatchlistExporter.cs
    dotnet test Jellyfin.Plugin.Watchlist.sln -c Release --filter "FullyQualifiedName~InvariantGuardTests"
    Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6

What this lint cannot do. It reads tokens, so it refuses a call by its spelling
and not by what it does. A path built by string concatenation, or a file opened
through a helper this table does not name, passes it. That is the bound of every
greppable invariant and it is why the rule table is data with a reason per line
rather than a claim that the invariant is enforced.

Only one invariant is in the table. The other one worth having, that nothing
writes to a playlist outside the projection, has nothing to read yet: there is no
projection in the tree. #82 carries a check of that class and it is written
against the adapter it comes with, so this table does not guess at it now.
