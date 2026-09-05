## extract-interface

Description:
  Extract the public members of a class/struct/interface into a new interface, in a new file

Usage:
  RoslynRefactor extract-interface [options]

Options:
  --project <project> (REQUIRED)  Path to a .sln or .csproj file
  --file <file> (REQUIRED)        Path to the file containing the type
  --line <line> (REQUIRED)        1-based line of the type
  --column <column> (REQUIRED)    1-based column of the type
  --name <name>                   Name of the extracted interface (default: "I" + the type's name)
  -?, -h, --help                  Show help and usage information
