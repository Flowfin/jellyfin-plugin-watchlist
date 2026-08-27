# The document-paste check, and where it stops

Every document on this board carries commands with their output pasted underneath,
and that is how a claim in a document is checked here. Nothing re-ran them, so a
paste went stale the moment the code under it moved and every check stayed green.
Five sites were found by hand on #232 and repaired; the sixth was found by this
check on the day it landed.

`DocumentPasteTests` re-runs the pastes it can and reds the run when what a command
prints is not what stands under it, naming the file, the line and both readings.

This page is the bound. It exists because a green run here is easy to read as
"every paste in the tree agrees", and that is not what it says. What it says is
that every paste in the POPULATION below, minus the ones this page calls unjudged,
agrees. Read the accounting before treating the green as coverage:

    dotnet test --filter FullyQualifiedName~TheRunSaysWhatItDidNotJudge --logger "console;verbosity=detailed"

That prints how many pastes were judged, how many were not, and for each unjudged
one the reason. An unjudged paste is not a passing one.

## What a paste has to look like

To be seen at all:

- It is indented by exactly four spaces, which is the block indent this
  repository's documents use. Less is prose and more is inside another block.
- Its first word is `grep`, or its first two words are `git grep`. A command that
  reaches one of the two further along a pipeline - `git ls-tree ... | grep ...`,
  `curl ... | grep ...` - is not a paste this check has found, because what it
  would have to run is the command in front.
- A line ending in a backslash is joined to the one after it, so a command written
  across several lines is one command.
- The output is the indented lines that follow, up to the first blank line, the
  first line that is not indented, or the first line that begins with a word this
  check knows as a command. That last rule is what keeps two commands in one block
  from being read as one command and its output, and it is a list of what has been
  written rather than a guarantee.
- A paste whose output holds a line that is exactly `...` is a declared elision and
  is reported as unjudged rather than compared.

## What it reads

The POPULATION is `README.md`, `CONTRIBUTING.md`, and every `.md` page under
`docs/`. A markdown file anywhere else - the changelog, the notices, the documents
beside this suite, this page - is not judged.

The TREE the commands read is a declared file set carried into the test assembly as
embedded resources. What is in it is written in the test project file beside the
item groups that put it there. A command naming a path the set does not hold is
reported as unjudged rather than answered from a shorter tree, because an answer
computed over fewer files than the command was given looks exactly like agreement.

The FLAGS it reads are `-n`, `-c`, `-l`, `-E`, `-i`, `-v`, `-w` and `-I`, alone or
clustered, and `--` before a git pathspec. A command using any other flag is
unjudged, named by the flag.

The PIPELINE it reads is a first stage that names files, then any number of further
`grep` stages over what the stage before printed, and `head -N`. One trailing
`; echo "exit=$?"` or `; echo "rc=$?"` is read, and the code it prints is the one
the last stage would have exited with.

The PATTERNS it reads are POSIX basic and extended expressions, translated into
this runtime's dialect: `\|`, `\(`, `\)`, `\{`, `\}`, `\+` and `\?` as operators in
basic mode with the bare forms literal, the same characters as operators in
extended mode, `\<` and `\>` as word boundaries, `\b`, `\B`, `\w`, `\W`, `\s`, `\S`
passed through, and bracket expressions passed through. A construct outside that
set is refused by name rather than translated approximately.

## What it does not reach

**It is an evaluation of the two programs, not the two programs.** Nothing here
runs a shell. The suite may not reach a shell, the network, a container runtime or
a real server - `HEADLESS.md` beside this file is that rule - so what runs is this
tree's reading of `grep` and `git grep` over an embedded file set. Where that
reading and the programs disagree, this check is wrong and says nothing about it.
The agreement measured when it landed is in the pull request that landed it, taken
by running the same pastes through the real programs and comparing; it is a reading
somebody took rather than something a run keeps true, because nothing in this tree
runs the comparison.

**A command naming a commit, a tag or a ref.** It is a reading of that commit and
this check reads the tree at the working copy. `git grep -n 'x' abc1234 -- path`
and `git grep -n 'x' origin/master -- path` are both unjudged; `git grep -n 'x' --
path` and `git grep -n 'x' path` are judged, because a bare operand that reaches a
file in the tree is a pathspec and one that reaches nothing is read as a ref.

**A command this check does not run.** A pasted `gh`, `curl`, `docker`, `dotnet`,
`sed`, `awk`, `python` or `cut` reading is unjudged rather than quietly passed, and
the headless rule is why for the first four: nothing here may reach the network, a
container runtime or a real server. `sort`, `uniq`, `wc` and the rest are unjudged
for the simpler reason that this check does not implement them.

**A shell.** A variable, a substitution, a redirection, a glob, `&&`, `||`, a brace
group, or more than one semicolon. Each of those changes what the command does and
none of them is read here.

**A locale, a machine and an ordering.** Files are read as text with line endings
normalised, so a paste that depends on a carriage return or on a byte order is not
what is compared. `git grep` output is ordered by path here, which is the order git
walks its index; a document pasting some other order disagrees.

**Whether the paste is the RIGHT command.** A command that prints what stands under
it and answers a question the sentence above it is not asking passes. That is a
judgement about meaning and no reading of the tree makes it.

## How a departure is declared

`DOCUMENT-PASTE-EXCEPTIONS.txt` beside this file, one entry per site, with the
document, the line the command starts on and a reason. It fails in both directions:
a mismatch with no entry reds the run, and an entry naming no paste in the
population reds it too, so a departure cannot outlive the paste it covered.

The site moves when the document does, and that is the intended cost. An edit that
shifts a declared paste to another line makes its entry stale and puts the
departure in front of somebody again.
