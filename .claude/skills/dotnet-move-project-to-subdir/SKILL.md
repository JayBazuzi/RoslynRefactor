---
name: dotnet-move-project-to-subdir
description: Move a .NET project's files into a subdirectory and/or add a .sln/.slnx, without breaking relative path references. Use whenever restructuring a .NET repo's directory layout — pulling a flat-root project into its own folder, adding a solution file to a repo that only has loose .csproj files, or splitting a single-project repo into a multi-project layout.
---

Moving a `.csproj` breaks anything that used a path relative to its old location. The
move itself (`git mv`) is the easy part; finding every relative reference is the part
that bites.

## Procedure

1. **Inventory before moving.** `git ls-files` (or `find . -name '*.csproj'`) to see
   every project and every loose file at the level you're about to disturb. Don't
   just move `*.cs` — check for project-specific assets (README referenced by
   `PackageReadmeFile`, icons, config files) that live alongside it.

2. **Move with `git mv`**, not Write/Read or plain `mv` — preserves rename history
   and stages the move atomically. Move the whole project's file set into
   `<ProjectName>/`, mirroring the pattern of any sibling project that's already in
   its own subdir (e.g. a `Tests/` folder).

3. **Fix relative paths inside the moved `.csproj`.** Grep it for `Include=` and
   `Path=` attributes with relative paths — anything not covered by the SDK's
   implicit globs:
   - `<None Include="README.md" .../>` → needs `..\README.md` (or wherever it now
     lives relative to the new csproj location).
   - `<Compile Remove="Tests\**" />` / similar excludes written for the *old* layout
     — often become unnecessary (sibling dirs are no longer nested under this
     project) and should be deleted, not just re-pathed.
   - Anything under `PackagePath`, `ItemGroup` `Content`/`None` includes that name a
     sibling file explicitly.

4. **Fix `ProjectReference` paths in every *other* project** that pointed at the
   moved csproj (e.g. `Tests/Foo.Tests.csproj`'s `<ProjectReference
   Include="..\Foo.csproj" />` → `..\Foo\Foo.csproj`).

5. **Grep the whole repo for the old path**, not just csproj files — this is the
   step that's easy to skip:
   ```
   grep -rn 'dotnet run --project \.\|dotnet (build|test|run) .*\.csproj\|<old-root-filename>' \
     --include='*.md' --include='*.yml' --include='*.cmd' --include='*' .
   ```
   Common hiding spots: CI workflows, a `build-and-test` script, `AGENTS.md`/
   `CLAUDE.md` fallback commands, README usage examples, `dotnet-tools.json`. Not
   everything needs changing — a script that already pointed at
   `Tests/Foo.Tests.csproj` doesn't break if `Tests/` didn't move.

6. **Check for a hardcoded path inside source**, e.g. a test host that locates the
   built exe by walking up from `AppContext.BaseDirectory` to a folder name, or by
   `typeof(SomeType).Assembly.Location` — these are usually move-proof by design,
   but confirm rather than assume.

7. **Create the solution file** with `dotnet new sln -n <Name>` then
   `dotnet sln <file> add <path/to/each>.csproj`. On newer SDKs (dotnet 10+) `dotnet
   new sln` emits `<Name>.slnx`, not `.sln` — don't assume the old extension; check
   what was actually created before referencing it elsewhere.

8. **Verify by building and testing for real**, not just visual inspection:
   `dotnet build <sln>` then run the repo's actual test entrypoint (its
   `build-and-test` script, if one exists, rather than a hand-rolled `dotnet test`
   invocation) — using the existing script surfaces any path assumptions you missed.

9. **Delete stale `bin/`/`obj/`** left behind at the old root location (they're
   gitignored but stick around on disk and can mask a broken reference behind cached
   output).

10. **`git status` / `git diff --stat` at the end** and confirm every change is
    either a clean rename (`R`) or a small, explainable edit — a plain `M`/`A`/`D`
    on a file you didn't mean to touch is a sign something else picked up a stale
    path.
