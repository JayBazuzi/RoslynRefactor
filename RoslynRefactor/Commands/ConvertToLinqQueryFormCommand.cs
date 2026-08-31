namespace RoslynRefactor;

sealed class ConvertToLinqQueryFormCommand : ConvertToLinqCommandBase, ICommand
{
    public static CommandDescriptor Descriptor { get; } = BuildDescriptor(
        "convert-to-linq-query-form",
        "Convert a foreach loop into a LINQ expression using query syntax (from/where/select)",
        "Convert_to_linq");
}
