using System.CommandLine;

namespace RoslynRefactor;

sealed class ConvertToLinqQueryFormCommand : ConvertToLinqCommandBase, ICommand
{
    internal static readonly string Name = "convert-to-linq-query-form";


    public static Command Build() => Build(
        Name,
        "Convert a foreach loop into a LINQ expression using query syntax (from/where/select)",
        "Convert_to_linq");
}
