using System.CommandLine;

namespace RoslynRefactor;

sealed class ConvertToLinqCallFormCommand : ConvertToLinqCommandBase, ICommand
{
    public static Command Build() => Build(
        "convert-to-linq-call-form",
        "Convert a foreach loop into a LINQ expression using fluent method calls (Where/Select)",
        "Convert_to_linq_call_form");
}
