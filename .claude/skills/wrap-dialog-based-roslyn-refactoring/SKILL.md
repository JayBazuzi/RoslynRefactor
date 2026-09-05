---
name: wrap-dialog-based-roslyn-refactoring
description: Add a new RoslynRefactor command for a Roslyn refactoring whose built-in CodeAction is designed for a VS dialog (an internal IWorkspaceService that collects options interactively) - e.g. Move Static Members, Pull Members Up, Change Signature, Extract Interface. Use whenever a command's underlying Roslyn provider needs an options service rather than working directly off the selection span.
---

Some Roslyn refactorings (anything using `CodeActionWithOptions`) don't compute their result
directly - they call `GetOptions(cancellationToken)`, which asks an internal
`IWorkspaceService` (e.g. `IMoveStaticMembersOptionsService`) to pop a VS dialog and return the
user's choices. There's no headless entry point, and you can't implement the internal service
interface yourself (it's inaccessible outside the Roslyn assembly, so `class X : IThatInterface`
won't compile). The fix is to skip the dialog layer entirely:

1. **Find the CodeAction class** (not the `CodeRefactoringProvider` - that's just what registers
   the action after checking the options service is non-null, which you're bypassing). Use
   `ilspycmd` against the relevant `Microsoft.CodeAnalysis*.dll` in
   `~/.nuget/packages/.../lib/net10.0/` to read its source. Confirm it derives from the **public**
   `Microsoft.CodeAnalysis.CodeActions.CodeActionWithOptions`.
2. **Read `CodeActionWithOptions` itself** (in `Microsoft.CodeAnalysis.Workspaces.dll`, public).
   It exposes `Task<IEnumerable<CodeActionOperation>?> GetOperationsAsync(object? options, CancellationToken)`
   - a **public** method that calls `ComputeOperationsAsync(options, ...)` directly, without ever
   calling `GetOptions()`. That's the entry point: build the internal `options` object yourself
   and hand it to this method. The options service is never touched.
3. **Build the options object.** It's usually an internal record/struct with a constructor. Get
   its `Type` via `Assembly.Load("Microsoft.CodeAnalysis.Features").GetType("Full.Internal.Name", throwOnError: true)`,
   then `GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)` -
   **use both `Public` and `NonPublic`**, even though the type is internal: primary constructors
   and explicit constructors on an internal type are often still declared `public` (their
   effective accessibility is capped by the containing type, but reflection's `Public` flag
   still finds them - `NonPublic` alone can come back empty). Pick the right overload by
   inspecting `GetParameters()`, then `ctor.Invoke([...])`. Reflection ignores accessibility on
   the *arguments* too, so passing public types (`ISymbol`, `Document`, `ImmutableArray<T>`) into
   an internal constructor's parameter slots needs no further tricks.
4. **Construct the CodeAction the same way** - reflect its (also probably-public-but-on-an-internal-type)
   constructor, passing `null` for the options-service parameter (safe, since step 2's code path
   never dereferences it).
5. **Cast the constructed instance to the public base type** (`CodeActionWithOptions`,
   `CodeRefactoringProvider`, etc.) to call its public members directly, with no further
   reflection - this is legal even though the concrete runtime type is inaccessible from your
   assembly (`RoslynRefactor/Commands/CommandSupport.cs`'s `LoadInternalProvider` already does
   this for providers; `MoveStaticMemberCommand.cs` does it for the CodeAction).
6. **Replicate any internal validation helpers** (e.g. `MemberAndDestinationValidator.IsMemberValid`)
   as plain C# in your command rather than reflecting into them - they're usually a handful of
   public-API checks (symbol kind, `IsStatic`, `TypeKind`) and reflecting a `static` helper adds
   little over just reading its decompiled body once and copying the logic.

See `RoslynRefactor/Commands/MoveStaticMemberCommand.cs` for a full worked example (wraps
`MoveStaticMembersWithDialogCodeAction`).

Once the command and its approval tests are in and passing, stop there - don't run
`dotnet mdsnippets` to regenerate README.md or the `roslyn-refactor-tool` skill's command table.
A GitHub Action does that from `Tests/_approvals/*.approved.md` on CI; a manual run just adds an
extra commit for something CI already does.
