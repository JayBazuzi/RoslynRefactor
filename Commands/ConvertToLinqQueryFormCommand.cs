using System.CommandLine;

namespace RoslynRefactor;

sealed class ConvertToLinqQueryFormCommand : ConvertToLinqCommandBase, ICommand
{
    public static Command Build() => Build(
        "convert-to-linq-query-form",
        "Convert a foreach loop into a LINQ expression using query syntax (from/where/select)",
        "Convert_to_linq");
}
