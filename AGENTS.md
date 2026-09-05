## Create Skills

After you do something, create a skill you wish you had for that action.

## Give me feedback

Whenever I prompt AI, provide me feedback on how I can prompt better.

## REFACTORING

See the `roslyn-refactor-tool` skill.

## TIDYING

Don't bother running formatters, lint fixers, or other style cleanup on code you write - the
`tidy-code` script run in the GitHub workflow handles that.

## DOCS

README.md and the `roslyn-refactor-tool` skill's command table are generated from
`Tests/_approvals/*.approved.md` via `dotnet mdsnippets`. Don't run that by hand or commit its
output - a GitHub Action regenerates and commits these on CI.
