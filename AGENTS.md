## REFACTORING

Strongly prefer to use the refactoring tool rather than editing files. You can find it at `dotnet run --project . -- {COMMAND} {OPTIONS}`.

<!-- include: Tests/_approvals/RoslynRefactorRootCommandTests.ApproveIndexOfAvailableCommands.approved.md -->
| Command | Description |
| --- | --- |
| [convert-to-linq-call-form](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.convert-to-linq-call-form.approved.md) | Convert a foreach loop into a LINQ expression using fluent method calls (Where/Select) |
| [convert-to-linq-query-form](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.convert-to-linq-query-form.approved.md) | Convert a foreach loop into a LINQ expression using query syntax (from/where/select) |
| [extract-method](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.extract-method.approved.md) | Extract selected statements into a new method |
| [introduce-variable](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.introduce-variable.approved.md) | Introduce a local variable for a selected expression |
| [rename](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.rename.approved.md) | Rename a symbol across a solution/project |
<!-- endInclude -->
