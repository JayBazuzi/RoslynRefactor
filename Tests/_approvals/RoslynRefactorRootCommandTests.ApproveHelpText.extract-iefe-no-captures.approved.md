## extract-iefe-no-captures

Description:
  Wrap a capture-free selection (statements or an expression) in an immediately-invoked static lambda

Usage:
  RoslynRefactor extract-iefe-no-captures [options]

Options:
  --project <project> (REQUIRED)            Path to a .sln or .csproj file
  --file <file> (REQUIRED)                  Path to the file containing the selection
  --start-line <start-line> (REQUIRED)      1-based start line of the selection
  --start-column <start-column> (REQUIRED)  1-based start column of the selection
  --end-line <end-line> (REQUIRED)          1-based end line of the selection
  --end-column <end-column> (REQUIRED)      1-based end column of the selection
  -?, -h, --help                            Show help and usage information
