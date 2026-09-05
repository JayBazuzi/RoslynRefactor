## reorder-parameters

Description:
  Reorder a method/property/indexer/delegate's parameters and update all call sites

Usage:
  RoslynRefactor reorder-parameters [options]

Options:
  --project <project> (REQUIRED)  Path to a .sln or .csproj file
  --file <file> (REQUIRED)        Path to the file containing the member's declaration
  --line <line> (REQUIRED)        1-based line of the member
  --column <column> (REQUIRED)    1-based column of the member
  --order <order> (REQUIRED)      New 1-based parameter order as a comma-separated permutation of the current positions, e.g. "2,1,3"
  -?, -h, --help                  Show help and usage information
