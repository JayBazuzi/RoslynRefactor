# RoslynRefactor

A command-line tool for running Roslyn-powered refactorings against a C# solution from scripts or an AI agent. Supported refactorings:

<!-- snippet: Tests\_approvals\RoslynRefactorRootCommandTests.RootCommand_has_expected_subcommands.approved.txt -->
<a id='snippet-Tests\_approvals\RoslynRefactorRootCommandTests.RootCommand_has_expected_subcommands.approved.txt'></a>
```txt
rename
extract-method
introduce-variable
convert-to-linq-call-form
convert-to-linq-query-form
```
<sup><a href='#snippet-Tests\_approvals\RoslynRefactorRootCommandTests.RootCommand_has_expected_subcommands.approved.txt' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

## Example

<!-- snippet: sample-refactoring.cmd -->
<a id='snippet-sample-refactoring.cmd'></a>
```cmd
RoslynRefactor.exe extract-method --project Sample.sln --file Sample/Program.cs --start-line 21 --start-column 9 --end-line 27 --end-column 10
```
<sup><a href='/sample-refactoring.cmd#L1-L1' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-refactoring.cmd' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
