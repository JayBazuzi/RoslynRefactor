using System.CommandLine;
using System.Text.RegularExpressions;

namespace RoslynRefactor;

static class CommandLine
{
    public static async Task<int> RunAsync(string[] args)
    {
        var root = new RoslynRefactorRootCommand();

        try
        {
            var batchFileIndex = Array.FindIndex(args, arg => arg.Length > 1 && arg.StartsWith('@'));
            return batchFileIndex < 0
                ? await InvokeAsync(root, args)
                : await RunBatchAsync(root, args, batchFileIndex);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    // A "@file" token anywhere on the command line names a batch response file: each non-blank,
    // non-comment line supplies the arguments for one invocation of the same command, combined with
    // whatever shared arguments were given alongside the @file token (e.g. --project).
    static async Task<int> RunBatchAsync(RoslynRefactorRootCommand root, string[] args, int batchFileIndex)
    {
        var batchFilePath = args[batchFileIndex][1..];
        var sharedArgs = args.Where((_, index) => index != batchFileIndex).ToArray();

        var lines = (await File.ReadAllLinesAsync(batchFilePath))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'));

        var exitCode = 0;
        foreach (var line in lines)
        {
            var combinedArgs = sharedArgs.Concat(SplitArguments(line)).ToArray();
            var result = await InvokeAsync(root, combinedArgs);
            if (result != 0)
            {
                exitCode = result;
            }
        }

        return exitCode;
    }

    static Task<int> InvokeAsync(RoslynRefactorRootCommand root, string[] args) =>
        root.Parse(args).InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false });

    static IEnumerable<string> SplitArguments(string line) =>
        Regex.Matches(line, "\"[^\"]*\"|\\S+")
            .Select(match => match.Value.Length >= 2 && match.Value[0] == '"'
                ? match.Value[1..^1]
                : match.Value);
}
