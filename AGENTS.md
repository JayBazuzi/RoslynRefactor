## Create Skills

After you do something, create a skill you wish you had for that action.

## Give me feedback

Whenever I prompt AI, provide me feedback on how I can prompt better.

## REFACTORING

See the `roslyn-refactor-tool` skill.

## TIDYING

Don't bother running formatters, lint fixers, or other style cleanup on code you write - the
`tidy-code` script run in the GitHub workflow handles that.

## COMMITS

Don't run `git commit` directly. Use the `racn` MCP server's `commit` tool instead (stage with
`git add` first - the server does not stage for you). Commits are classified with Arlo's
Risk-Aware Commit Notation (RACN); see the `arlo-commit-notation` skill and the racn MCP's
`notation_reference` tool for valid risk levels/intentions.

Split work into one commit per logical, independently-verifiable step, rather than batching
everything into one commit at the end. For a multi-step refactor (e.g. extract a helper, then
rewire N call sites), that's N+1 commits, each building and passing tests on its own.

Use risk `.` (proven_safe) for changes that are safe without relying on the test suite - either
because they're safe by inspection alone (renaming a private symbol, deleting dead code) or because
they were produced by a well-vetted automated tool (an autoformatter, a trusted mechanical
refactoring command). Use `^` (validated) when safety is only known because tests exercise the
change - the normal case for hand-written, behavior-preserving refactors in a codebase with a test
suite. Default to `^` for refactoring commits unless the change is so mechanical, or so reliably
produced by tooling, that tests add nothing.

When a run of small related commits forms one coherent unit of work, close it out with a real
two-parent RACN `. m` (merge) commit tying the movement together, parented on both the tip and the
last commit before the movement started - not just a trailing single-parent marker. The racn MCP
tool can't create empty or multi-parent commits, so build it manually:

```
tree=$(git rev-parse <tip>^{tree})
new=$(git commit-tree "$tree" -p <tip-of-movement> -p <last-commit-before-movement> -m ". m <summary>")
git reset --hard "$new"   # only safe if <tip> hasn't been pushed/shared yet
```

## DOCS

README.md and the `roslyn-refactor-tool` skill's command table are generated from
`Tests/_approvals/*.approved.md` via `dotnet mdsnippets`. Don't run that by hand or commit its
output - a GitHub Action regenerates and commits these on CI.
