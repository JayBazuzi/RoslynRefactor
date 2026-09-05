---
name: arlo-commit-notation
description: Write git commit summary lines using Arlo's (Risk-Aware) Commit Notation — a risk-level symbol plus an intention letter at the start of the summary. Use whenever writing or reviewing a commit message in this repo.
---

Full spec: https://raw.githubusercontent.com/RefactoringCombos/ArlosCommitNotation/refs/heads/main/README.md

## Format

```
<risk> <intention> <summary>
```

Example: `. r Extract method` — a proven-safe refactoring.

## Risk level (first character)

| Symbol | Meaning | Guarantees |
|---|---|---|
| `.` | (Proven) Safe | Intended change + all invariants (known and unknown) preserved |
| `^` | Validated | Intended change + all known invariants preserved (e.g. covered by tests) |
| `!` | Risky | Only the intended change is verified; some known risks unmitigated |
| `@` | (Probably) Broken | No risk attestation — savepoint, WIP, or unverified |

Pick the risk level honestly based on what was actually done to verify the change, not what was hoped for. A refactor done purely via the RoslynRefactor tool with a clean build is `.`; a manual/hand-edited refactor is at best `!`.

## Intention (second character)

Core intentions, from the spec:

| Letter | Name | Meaning |
|---|---|---|
| `F`/`f` | Feature | Change or extend one behavior without altering others |
| `B`/`b` | Bugfix | Repair one undesired behavior without altering others |
| `R`/`r` | Refactoring | Change implementation without changing behavior |
| `D`/`d` | Documentation | Change something communicating to team members; no behavior impact |

Case distinguishes "pay more attention": uppercase = intended/user-visible behavior change, lowercase = no behavior change or purely internal. "User" means a user of this product (the RoslynRefactor tool) — a change to CI, dev tooling, or scripts is internal/lowercase even though it's visible to contributors or affects the build pipeline.

This repo also uses extension letters beyond the core four (e.g. `e` for environment/tooling/CI changes, `t` for test-only changes — see `git log --oneline` for real examples and current usage before inventing a new one). Extension intentions are a per-team addition; check history rather than assuming a fixed global list.

The intention field is always a single letter — never the spelled-out name (`e`, not `environment`; `d`, not `documentation`).

## Writing a commit message

1. Look at `git log --oneline -20` in the current repo to confirm the risk symbols and intention letters actually in use (this repo's convention may extend the core spec).
2. Pick the intention letter — a single character — for what the commit is doing.
3. Pick the risk level based on how the change was verified (tool-driven refactor with clean build → `.`; hand edits with tests → `^` or `!`; unverified/WIP → `@`).
4. Write the summary as a short imperative phrase after the two-character prefix.
