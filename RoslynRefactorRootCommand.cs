using System.CommandLine;

namespace RoslynRefactor;

sealed class RoslynRefactorRootCommand : RootCommand
{
    public RoslynRefactorRootCommand() : base("RoslynRefactor - Roslyn-powered C# refactoring CLI")
    {
        RefactorSubCommands.Values.ToList().ForEach(Add);
        Add(McpCommand.Build());
    }

    public static readonly Dictionary<string, Command> RefactorSubCommands = new Dictionary<string, Command>
    {
        [RenameCommand.Name] = RenameCommand.Build(),
        [ExtractMethodCommand.Name] = ExtractMethodCommand.Build(),
        [IntroduceVariableCommand.Name] = IntroduceVariableCommand.Build(),
        [InlineTemporaryVariableCommand.Name] = InlineTemporaryVariableCommand.Build(),
        [InlineMethodCommand.Name] = InlineMethodCommand.Build(),
        [ConvertToLinqCallFormCommand.Name] = ConvertToLinqCallFormCommand.Build(),
        [ConvertToLinqQueryFormCommand.Name] = ConvertToLinqQueryFormCommand.Build(),
        [McpCommand.Name] = McpCommand.Build(),
    };
}
