using System.CommandLine;

namespace RoslynRefactor;

static class CommandLine
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = new RootCommand("RoslynRefactor - Roslyn-powered C# refactoring CLI")
        {
            RenameCommand.Build(),
            ExtractMethodCommand.Build(),
            IntroduceVariableCommand.Build(),
            ConvertToLinqCallFormCommand.Build(),
            ConvertToLinqQueryFormCommand.Build(),
        };

        try
        {
            return await root.Parse(args).InvokeAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }
}
