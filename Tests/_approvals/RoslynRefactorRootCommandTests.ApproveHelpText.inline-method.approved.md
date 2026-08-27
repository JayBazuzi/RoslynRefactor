## inline-method

Description:
  Inline a called method's (or local function's) body at the call site

Usage:
  RoslynRefactor inline-method [options]

Options:
  --project <project> (REQUIRED)            Path to a .sln or .csproj file
  --file <file> (REQUIRED)                  Path to the file containing the call site
  --start-line <start-line> (REQUIRED)      1-based start line of the selection
  --start-column <start-column> (REQUIRED)  1-based start column of the selection
  --end-line <end-line> (REQUIRED)          1-based end line of the selection
  --end-column <end-column> (REQUIRED)      1-based end column of the selection
  -?, -h, --help                            Show help and usage information
