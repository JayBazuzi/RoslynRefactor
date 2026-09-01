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
| [inline-method](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.inline-method.approved.md) | Inline a called method's (or local function's) body at the call site |
| [inline-temporary-variable](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.inline-temporary-variable.approved.md) | Inline a local variable's initializer into all usages, then remove the declaration |
| [introduce-variable](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.introduce-variable.approved.md) | Introduce a local variable for a selected expression |
| [rename](https://raw.githubusercontent.com/JayBazuzi/RoslynRefactor/refs/heads/main/Tests/_approvals/RoslynRefactorRootCommandTests.ApproveHelpText.rename.approved.md) | Rename a symbol across a solution/project |
<!-- endInclude -->

## Example

<!-- snippet: sample-refactoring.cmd -->
```cmd
RoslynRefactor extract-method --project Sample.sln --file Sample/Program.cs --start-line 21 --start-column 9 --end-line 27 --end-column 10
```
<!-- endSnippet -->

## Batch response files

Put an `@file` token anywhere on the command line to run the same command once per line
of that file, combined with any shared arguments given alongside the `@file` token:

```cmd
RoslynRefactor extract-method --project Sample.sln @inputs.txt
```

`inputs.txt`:
```
--file Sample/Program.cs --start-line 21 --start-column 9 --end-line 27 --end-column 10
--file Sample/Program.cs --start-line 29 --start-column 9 --end-line 35 --end-column 10
```

## Using with AI coding agents

See this project's [AGENTS.md](https://github.com/JayBazuzi/RoslynRefactor/blob/main/AGENTS.md) and [SKILL.md](https://github.com/JayBazuzi/RoslynRefactor/blob/main/.claude/skills/roslyn-refactor-tool/SKILL.md).

## MCP server

`RoslynRefactor mcp` runs an MCP server over stdio that exposes every refactoring command above as an MCP tool, for clients that speak MCP instead of invoking a CLI directly.

[JSON descriptions](https://github.com/JayBazuzi/RoslynRefactor/blob/main/Tests/_approvals/McpToolsTests.ApproveToolDescriptions.approved.md) of the tools exposed by the MCP server.
