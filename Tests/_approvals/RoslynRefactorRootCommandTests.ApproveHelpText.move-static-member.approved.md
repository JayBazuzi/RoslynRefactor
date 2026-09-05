## move-static-member

Description:
  Move a static member (method/property/field/event) to another type in the same project

Usage:
  RoslynRefactor move-static-member [options]

Options:
  --project <project> (REQUIRED)  Path to a .sln or .csproj file
  --file <file> (REQUIRED)        Path to the file containing the member
  --line <line> (REQUIRED)        1-based line of the member
  --column <column> (REQUIRED)    1-based column of the member
  --to <to> (REQUIRED)            Fully qualified name of the destination type (must already exist in the same project)
  -?, -h, --help                  Show help and usage information
