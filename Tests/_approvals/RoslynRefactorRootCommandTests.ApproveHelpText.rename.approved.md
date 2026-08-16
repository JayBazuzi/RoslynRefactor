## rename

Description:
  Rename a symbol across a solution/project

Usage:
  RoslynRefactor rename [options]

Options:
  --project <project> (REQUIRED)  Path to a .sln or .csproj file
  --file <file> (REQUIRED)        Path to the file containing the symbol
  --line <line> (REQUIRED)        1-based line of the symbol
  --column <column> (REQUIRED)    1-based column of the symbol
  --to <to> (REQUIRED)            The new name for the symbol
  -?, -h, --help                  Show help and usage information
