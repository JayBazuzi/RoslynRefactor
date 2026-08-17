using System.CommandLine;

namespace RoslynRefactor;

sealed class RoslynRefactorRootCommand : RootCommand
{
    public RoslynRefactorRootCommand() : base("RoslynRefactor - Roslyn-powered C# refactoring CLI")
    {
        Add(RenameCommand.Build());
        Add(ExtractMethodCommand.Build());
        Add(IntroduceVariableCommand.Build());
        Add(ConvertToLinqCallFormCommand.Build());
        Add(ConvertToLinqQueryFormCommand.Build());
        Add(ConvertLambdaCapturesToParametersCommand.Build());
    }
}
