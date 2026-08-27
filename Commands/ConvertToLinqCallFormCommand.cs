using System.CommandLine;

namespace RoslynRefactor;

sealed class ConvertToLinqCallFormCommand : ConvertToLinqCommandBase, ICommand
{
    internal static readonly string Name = "convert-to-linq-call-form";

    public static Command Build() => Build(
        Name,
        "Convert a foreach loop into a LINQ expression using fluent method calls (Where/Select)",
        "Convert_to_linq_call_form");
}
