using System.CommandLine;

namespace RoslynRefactor;

static class CommandLine
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = new RoslynRefactorRootCommand();

        try
        {
            return await root.Parse(args).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }
}
