## REFACTORING

Strongly prefer to use the refactoring tool rather than editing files. You can find it at `dotnet run --project . -- {COMMAND} {OPTIONS}`.

<!-- snippet: Tests\_approvals\RoslynRefactorRootCommandTests.ApproveAllHelpText.approved.md -->
<a id='snippet-Tests\_approvals\RoslynRefactorRootCommandTests.ApproveAllHelpText.approved.md'></a>
```md
# RoslynRefactor CLI help

## RoslynRefactor

Description:
  RoslynRefactor - Roslyn-powered C# refactoring CLI

Usage:
  testhost [command] [options]

Options:
  -?, -h, --help  Show help and usage information
  --version       Show version information

Commands:
  rename                      Rename a symbol across a solution/project
  extract-method              Extract selected statements into a new method
  introduce-variable          Introduce a local variable for a selected expression
  convert-to-linq-call-form   Convert a foreach loop into a LINQ expression using fluent method calls (Where/Select)
  convert-to-linq-query-form  Convert a foreach loop into a LINQ expression using query syntax (from/where/select)

## RoslynRefactor convert-to-linq-call-form

Description:
  Convert a foreach loop into a LINQ expression using fluent method calls (Where/Select)

Usage:
  testhost convert-to-linq-call-form [options]

Options:
  --project <project> (REQUIRED)            Path to a .sln or .csproj file
  --file <file> (REQUIRED)                  Path to the file containing the selection
  --start-line <start-line> (REQUIRED)      1-based start line of the selection
  --start-column <start-column> (REQUIRED)  1-based start column of the selection
  --end-line <end-line> (REQUIRED)          1-based end line of the selection
  --end-column <end-column> (REQUIRED)      1-based end column of the selection
  -?, -h, --help                            Show help and usage information

## RoslynRefactor convert-to-linq-query-form

Description:
  Convert a foreach loop into a LINQ expression using query syntax (from/where/select)

Usage:
  testhost convert-to-linq-query-form [options]

Options:
  --project <project> (REQUIRED)            Path to a .sln or .csproj file
  --file <file> (REQUIRED)                  Path to the file containing the selection
  --start-line <start-line> (REQUIRED)      1-based start line of the selection
  --start-column <start-column> (REQUIRED)  1-based start column of the selection
  --end-line <end-line> (REQUIRED)          1-based end line of the selection
  --end-column <end-column> (REQUIRED)      1-based end column of the selection
  -?, -h, --help                            Show help and usage information

## RoslynRefactor extract-method

Description:
  Extract selected statements into a new method

Usage:
  testhost extract-method [options]

Options:
  --project <project> (REQUIRED)            Path to a .sln or .csproj file
  --file <file> (REQUIRED)                  Path to the file containing the selection
  --start-line <start-line> (REQUIRED)      1-based start line of the selection
  --start-column <start-column> (REQUIRED)  1-based start column of the selection
  --end-line <end-line> (REQUIRED)          1-based end line of the selection
  --end-column <end-column> (REQUIRED)      1-based end column of the selection
  -?, -h, --help                            Show help and usage information

## RoslynRefactor introduce-variable

Description:
  Introduce a local variable for a selected expression

Usage:
  testhost introduce-variable [options]

Options:
  --project <project> (REQUIRED)            Path to a .sln or .csproj file
  --file <file> (REQUIRED)                  Path to the file containing the selection
  --start-line <start-line> (REQUIRED)      1-based start line of the selection
  --start-column <start-column> (REQUIRED)  1-based start column of the selection
  --end-line <end-line> (REQUIRED)          1-based end line of the selection
  --end-column <end-column> (REQUIRED)      1-based end column of the selection
  -?, -h, --help                            Show help and usage information

## RoslynRefactor rename

Description:
  Rename a symbol across a solution/project

Usage:
  testhost rename [options]

Options:
  --project <project> (REQUIRED)  Path to a .sln or .csproj file
  --file <file> (REQUIRED)        Path to the file containing the symbol
  --line <line> (REQUIRED)        1-based line of the symbol
  --column <column> (REQUIRED)    1-based column of the symbol
  --to <to> (REQUIRED)            The new name for the symbol
  -?, -h, --help                  Show help and usage information
```
<sup><a href='#snippet-Tests\_approvals\RoslynRefactorRootCommandTests.ApproveAllHelpText.approved.md' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->
