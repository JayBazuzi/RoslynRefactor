## make-method-static

Description:
  Convert an instance method into a static method by adding a parameter for its receiver, and update all call sites

Usage:
  RoslynRefactor make-method-static [options]

Options:
  --project <project> (REQUIRED)     Path to a .sln or .csproj file
  --file <file> (REQUIRED)           Path to the file containing the method
  --line <line> (REQUIRED)           1-based line of the method
  --column <column> (REQUIRED)       1-based column of the method
  --parameter-name <parameter-name>  Name for the new receiver parameter (default: derived from the type name, falling back to self/self2/... on collision)
  -?, -h, --help                     Show help and usage information
