# RoslynRefactor

[![Build and Test](https://github.com/JayBazuzi/RoslynRefactor/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/JayBazuzi/RoslynRefactor/actions/workflows/build-and-test.yml)
[![NuGet](https://img.shields.io/nuget/v/RoslynRefactor.svg)](https://www.nuget.org/packages/RoslynRefactor)

A command-line tool for running Roslyn-powered refactorings against a C# solution from scripts or an AI agent. Supported refactorings:

<!-- include: Tests/_approvals/RoslynRefactorRootCommandTests.ApproveIndexOfAvailableCommands.approved.md -->
| Command | Description |
| --- | --- |
| [convert-to-linq-call-form](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.convert-to-linq-call-form.approved.md) | Convert a foreach loop into a LINQ expression using fluent method calls (Where/Select) |
| [convert-to-linq-query-form](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.convert-to-linq-query-form.approved.md) | Convert a foreach loop into a LINQ expression using query syntax (from/where/select) |
| [extract-method](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.extract-method.approved.md) | Extract selected statements into a new method |
| [introduce-variable](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.introduce-variable.approved.md) | Introduce a local variable for a selected expression |
| [rename](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.rename.approved.md) | Rename a symbol across a solution/project |
<!-- endInclude -->

## Example

<!-- snippet: sample-refactoring.cmd -->
```cmd
RoslynRefactor extract-method --project Sample.sln --file Sample/Program.cs --start-line 21 --start-column 9 --end-line 27 --end-column 10
```
<!-- endSnippet -->

## Using with AI coding agents

See this project's [AGENTS.md](https://github.com/JayBazuzi/RoslynRefactor/blob/main/AGENTS.md).
