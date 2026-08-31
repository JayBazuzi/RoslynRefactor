---
name: roslyn-refactor-tool
description: Use the RoslynRefactor commands (extract-method, inline-method, inline-temporary-variable, introduce-variable, rename, convert-to-linq-call-form, convert-to-linq-query-form) instead of hand-editing files. Use whenever performing a refactoring in this repo that matches one of these commands.
---

Strongly prefer to use the refactoring tool rather than editing files by hand. It is
available as MCP tools (one per command below); use those instead of shelling out. If
MCP tools aren't available, fall back to `dotnet run --project RoslynRefactor -- {COMMAND} {OPTIONS}`.

<!-- include: Tests/_approvals/RoslynRefactorRootCommandTests.ApproveIndexOfAvailableCommands.approved.md -->
| Command | Description |
| --- | --- |
| [convert-to-linq-call-form](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.convert-to-linq-call-form.approved.md) | Convert a foreach loop into a LINQ expression using fluent method calls (Where/Select) |
| [convert-to-linq-query-form](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.convert-to-linq-query-form.approved.md) | Convert a foreach loop into a LINQ expression using query syntax (from/where/select) |
| [extract-method](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.extract-method.approved.md) | Extract selected statements into a new method |
| [inline-method](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.inline-method.approved.md) | Inline a called method's (or local function's) body at the call site |
| [inline-temporary-variable](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.inline-temporary-variable.approved.md) | Inline a local variable's initializer into all usages, then remove the declaration |
| [introduce-variable](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.introduce-variable.approved.md) | Introduce a local variable for a selected expression |
| [rename](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.rename.approved.md) | Rename a symbol across a solution/project |
<!-- endInclude -->
