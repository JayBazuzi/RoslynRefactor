# RoslynRefactor

A command-line tool for running Roslyn-powered refactorings (extract method, introduce
variable, rename, convert to LINQ) against a C# solution from scripts or CI.

## Example

<!-- snippet: sample-refactoring.cmd -->
<a id='snippet-sample-refactoring.cmd'></a>
```cmd
dotnet RoslynRefactor.dll extract-method --project Sample.sln --file Sample/Program.cs --start-line 21 --start-column 9 --end-line 27 --end-column 10
```
<sup><a href='/sample-refactoring.cmd#L1-L1' title='Snippet source file'>snippet source</a> | <a href='#snippet-sample-refactoring.cmd' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
