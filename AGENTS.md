## Create skills

After finishing a task, if you had to work around missing tooling, figure out a non-obvious
multi-step process, or would have benefited from a skill that doesn't exist yet, create one so a
future session doesn't have to rediscover it.

## Give feedback on prompts

After each response, briefly tell the user how they could have prompted this request more
effectively. Be specific and concise - reference the actual prompt, not general advice.

## Refactoring

See the `roslyn-refactor-tool` skill.

## Tidying

Don't run formatters, lint fixers, or other style cleanup on code you write. The `tidy-code`
script, run in the GitHub workflow, handles that.

## Commits

Don't run `git commit` directly. Use the `racn` MCP server's `commit` tool instead (stage with
`git add` first - the server does not stage for you). Commits are classified with Arlo's
Risk-Aware Commit Notation (RACN); see the `arlo-commit-notation` skill and the racn MCP's
`notation_reference` tool for valid risk levels/intentions.

Split work into one commit per logical, independently-verifiable step, rather than batching
everything into one commit at the end. For a multi-step refactor (e.g. extract a helper, then
rewire N call sites), that's N+1 commits, each building and passing tests on its own.

Choose the risk level per commit:

- `.` (proven_safe) - the change is safe without relying on the test suite: safe by inspection
  alone (renaming a private symbol, deleting dead code), or produced by a well-vetted automated
  tool (an autoformatter, a trusted mechanical refactoring command).
- `^` (validated) - safety is only known because tests exercise the change. This is the default
  for hand-written, behavior-preserving refactors in a codebase with a test suite. Use `^` unless
  the change is mechanical or tool-produced enough that tests add nothing.

When a run of small related commits forms one coherent unit of work, close it out with a real
two-parent RACN `. m` (merge) commit tying the movement together, parented on both the tip and the
last commit before the movement started - not just a trailing single-parent marker. The racn MCP
tool can't create empty or multi-parent commits, so build it manually:

```
tree=$(git rev-parse <tip>^{tree})
new=$(git commit-tree "$tree" -p <tip-of-movement> -p <last-commit-before-movement> -m ". m <summary>")
git reset --hard "$new"   # only safe if <tip> hasn't been pushed/shared yet
```

## Tooling

Don't reach for Python scripts to accomplish tasks - it isn't always installed in the environments
this repo runs in. Use PowerShell/Bash, .NET, or the available tools instead.

## Docs

README.md and the `roslyn-refactor-tool` skill's command table are generated from
`Tests/_approvals/*.approved.md` via `dotnet mdsnippets`. Don't run that by hand or commit its
output - a GitHub Action regenerates and commits these on CI.
