# RoslynRefactor

[![Build and Test](https://github.com/JayBazuzi/RoslynRefactor/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/JayBazuzi/RoslynRefactor/actions/workflows/build-and-test.yml)
[![NuGet](https://img.shields.io/nuget/v/RoslynRefactor.svg)](https://www.nuget.org/packages/RoslynRefactor)

A command-line tool for running Roslyn-powered refactorings against a C# solution from scripts or an AI agent. Supported refactorings:

<!-- snippet: Tests/_approvals/RoslynRefactorRootCommandTests.RootCommand_has_expected_subcommands.approved.txt -->
```txt
rename
extract-method
introduce-variable
convert-to-linq-call-form
convert-to-linq-query-form
```
<!-- endSnippet -->

## Example

<!-- snippet: sample-refactoring.cmd -->
```cmd
RoslynRefactor.exe extract-method --project Sample.sln --file Sample/Program.cs --start-line 21 --start-column 9 --end-line 27 --end-column 10
```
<!-- endSnippet -->

## Using with AI coding agents

See this project's [AGENTS.md](AGENTS.md).
