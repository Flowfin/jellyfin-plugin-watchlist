# The formatter and the invariant lint

Two checks the other board runs and this board owed an answer on. One is refused
and one is adopted, and both answers are here with what would change them.

## The formatter: refused

The other board runs a formatter over its non-code files. This tree's non-code
surface is not the same surface.

    git ls-tree -r --name-only a8bf829 | grep -oE '\.[a-z]+$' | sort | uniq -c | sort -rn
         78 .cs
         20 .md
         15 .txt
         12 .json
         11 .yaml
          6 .yml
          2 .png
          2 .csproj
          1 .sln
          1 .ruleset
          1 .props
          1 .html
          1 .gitignore
          1 .editorconfig

Taken on `a8bf829`, which the command now names rather than leaving to this
sentence. It read `git ls-files` before, and that reads whichever tree it is run
in, so the totals a reader got moved away from the ones pasted under it while
every number here stayed right for the commit it was taken at:

    git ls-tree -r --name-only 3f8ce3d | grep -oE '\.[a-z]+$' | sort | uniq -c | sort -rn | head -3
         80 .cs
         20 .md
         17 .txt

The census is unchanged. This paragraph pasted a census with 47 sources, eleven
markdown files and no image in it, and that reading was right on `c24b5b1`, the
commit that wrote this file:

    git ls-tree -r --name-only c24b5b1 | grep -oE '\.[a-z]+$' | sort | uniq -c | sort -rn | head -3
         47 .cs
         12 .json
         11 .yaml

The tree grew under it and nobody took the census again. What the refusal is read
off is the composition rather than the totals, and the composition holds: the
markdown is still argument, the JSON is still generated or fixture bytes, and the
two image files that arrived in the meantime are not text a formatter touches.

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

What is left is one HTML page and the workflows, and a check that exists to hold
a page and some YAML is a check whose failures are noise.

That sentence said sixteen lines, twice, and the page is 104:

    git grep -c '' c24b5b1 -- Jellyfin.Plugin.Watchlist/Configuration/configPage.html
    c24b5b1:Jellyfin.Plugin.Watchlist/Configuration/configPage.html:16
    git grep -c '' a8bf829 -- Jellyfin.Plugin.Watchlist/Configuration/configPage.html
    a8bf829:Jellyfin.Plugin.Watchlist/Configuration/configPage.html:104

What would change the answer: the configuration page growing a script or a
stylesheet, which is #31, or a hand-edited JSON surface appearing that a reader
has to diff. Either puts real formatted-by-hand output in the tree and the row
in `docs/parity.md` says so.

The first of those has happened, and the refusal above is left standing rather
than reversed here. #31 landed and the page carries a script:

    git grep -n '<script' a8bf829 -- Jellyfin.Plugin.Watchlist/Configuration/configPage.html
    a8bf829:Jellyfin.Plugin.Watchlist/Configuration/configPage.html:66:        <script type="text/javascript">

    gh issue view 31 --repo Flowfin/jellyfin-plugin-watchlist --json state --jq .state
    CLOSED

So the surface this section refused a formatter over is not the surface in the
tree, and the sentence naming the condition sat next to a page that had already
met it. Whether the answer changes now is the decision #60 took and #60 is
closed, so nothing here retakes it. What this correction does is stop the section
from reading as settled against a page it stopped describing.

It was found by re-running the census command above and comparing what it prints
with what the paragraph pasted.

## The invariant lint: adopted, in the shape this tree already has

Adopted, and it lives in the suite rather than in a workflow of its own.

What the table holds is read out of it rather than counted here, because a count
written into a document is wrong the day somebody adds a rule and nothing says so:

    grep -v '^#' Jellyfin.Plugin.Watchlist.Tests/Invariants.txt | grep -v '^$' | cut -d' ' -f1 | sort -u
    api-refusal-body
    plugin-network
    store-filesystem

The first invariant, and the one the argument below is written from: the store is
the only part of this plugin that touches the file system. It is true of the tree
today rather than an aspiration, and it is one grep to say so:

    git grep -lE '\bPath\s*\.\s*[A-Z][A-Za-z]*\s*\(|\bFile\s*\.\s*[A-Z][A-Za-z]*\s*\(|\bDirectory\s*\.\s*[A-Z][A-Za-z]*\s*\(|new\s+FileStream\s*\(' -- Jellyfin.Plugin.Watchlist
    Jellyfin.Plugin.Watchlist/Store/WatchlistDocumentStore.cs

That command matched the three names with nothing required after them until
this change, so it had started printing two paths under a sentence naming one.
What the second one was, read at `40d29ad` because the comment block it found
came out of the project file when #4 moved the package set and the forced
transitive pins it explained were no longer needed:

    git grep -nE '\bDirectory\s*\.\s*[A-Z]' 40d29ad -- Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj
    40d29ad:Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj:53:             rated critical, fixed in 4.5.1; the audit switched on in Directory.Build.props
    40d29ad:Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj:56:             Declared here rather than in Directory.Build.props, although it applies to
    40d29ad:Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj:60:             that comparison about Directory.Build.props is a change to a guard and

    git log --oneline -S'the audit switched on in Directory.Build.props' 40d29ad -- Jellyfin.Plugin.Watchlist/Jellyfin.Plugin.Watchlist.csproj
    8a405f1 Watch the dependency graph, do not only record it (#188)

Three lines of comment naming a build file, in a file the guard never reads.
The scan is handed the plugin's `.cs` sources by the wildcard in the test
project and nothing else, and every pattern in the table ends at the call
rather than at the name, which is the shape the command above now has. So the
invariant held throughout and the evidence for it had stopped saying so. It was
found by running the commands in this file and comparing what they print with
what is pasted under them.

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
destination. Both runs below were taken again on `7306873`, and that commit is
named because the totals move whenever a test joins the class the filter selects:

    public static string ExportPath(string folder) => System.IO.Path.Combine(folder, "watchlist-export.json");

    dotnet test Jellyfin.Plugin.Watchlist.sln -c Release --filter "FullyQualifiedName~InvariantGuardTests"
       An invariant is broken in the plugin's own sources:
    Export/WatchlistExporter.cs:102 [store-filesystem] builds a file path outside the store (Path.Combine()
    Failed!  - Failed:     1, Passed:     9, Skipped:     0, Total:    10

and silent on the tree with that one method removed:

    git checkout -- Jellyfin.Plugin.Watchlist/Export/WatchlistExporter.cs
    dotnet test Jellyfin.Plugin.Watchlist.sln -c Release --filter "FullyQualifiedName~InvariantGuardTests"
    Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10

The totals said six until the rule for an outbound call landed beside this one and
brought two tests with it. What the pair is quoted for is the one line naming the
file, the rule and the line it refused, and that line is the same on both readings.

What this lint cannot do. It reads tokens, so it refuses a call by its spelling
and not by what it does. A path built by string concatenation, or a file opened
through a helper this table does not name, passes it. That is the bound of every
greppable invariant and it is why the rule table is data with a reason per line
rather than a claim that the invariant is enforced.

The one worth having that is not in the table, and the reason it is not: nothing
writes to a playlist outside the projection has nothing to read yet, because there
is no projection in the tree. #82 carries a check of that class and it is written
against the adapter it comes with, so this table does not guess at it now.
