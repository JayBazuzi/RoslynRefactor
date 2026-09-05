using System.CommandLine;

namespace RoslynRefactor;

sealed class RoslynRefactorRootCommand : RootCommand
{
    public RoslynRefactorRootCommand() : base("RoslynRefactor - Roslyn-powered C# refactoring CLI")
    {
        RefactorSubCommands.Values.ToList().ForEach(Add);
        Add(McpCommand.Build());
    }

    public static readonly IReadOnlyList<CommandDescriptor> RefactorDescriptors =
    [
        RenameCommand.Descriptor,
        MoveStaticMemberCommand.Descriptor,
        ExtractMethodCommand.Descriptor,
        ExtractInterfaceCommand.Descriptor,
        ChangeSignatureCommand.Descriptor,
        IntroduceVariableCommand.Descriptor,
        InlineTemporaryVariableCommand.Descriptor,
        InlineMethodCommand.Descriptor,
        ConvertToLinqCallFormCommand.Descriptor,
        ConvertToLinqQueryFormCommand.Descriptor,
    ];

    public static readonly Dictionary<string, Command> RefactorSubCommands =
        RefactorDescriptors.ToDictionary(d => d.Name, CliCommandAdapter.Build);
}
