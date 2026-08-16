## introduce-variable

Description:
  Introduce a local variable for a selected expression

Usage:
  RoslynRefactor introduce-variable [options]

Options:
  --project <project> (REQUIRED)            Path to a .sln or .csproj file
  --file <file> (REQUIRED)                  Path to the file containing the selection
  --start-line <start-line> (REQUIRED)      1-based start line of the selection
  --start-column <start-column> (REQUIRED)  1-based start column of the selection
  --end-line <end-line> (REQUIRED)          1-based end line of the selection
  --end-column <end-column> (REQUIRED)      1-based end column of the selection
  -?, -h, --help                            Show help and usage information
