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
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();

        // rename batches get special handling: every step's line/column is resolved against the
        // original solution up front, so earlier renames in the batch can't shift the positions
        // that later renames were specified against. See RenameCommand.RunBatchAsync.
        if (sharedArgs.Length > 0 && sharedArgs[0] == RenameCommand.Descriptor.Name)
        {
            var sharedArguments = ParseArguments(sharedArgs.Skip(1));
            var perRenameArguments = lines
                .Select(line => MergeArguments(sharedArguments, ParseArguments(SplitArguments(line))))
                .ToList();
            return await RenameCommand.RunBatchAsync(perRenameArguments, Console.Out, CancellationToken.None);
        }

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

    // Minimal "--name value" pair parser, used only for the rename-batch fast path above (which
    // bypasses System.CommandLine so it can build one Dictionary<string,string> of arguments per
    // rename without invoking the command for each one).
    static Dictionary<string, string> ParseArguments(IEnumerable<string> args)
    {
        var result = new Dictionary<string, string>();
        using var enumerator = args.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var name = enumerator.Current;
            if (!name.StartsWith("--", StringComparison.Ordinal) || !enumerator.MoveNext())
            {
                throw new InvalidOperationException($"expected \"--name value\" pairs, got: {name}");
            }
            result[name[2..]] = enumerator.Current;
        }
        return result;
    }

    static Dictionary<string, string> MergeArguments(
        IReadOnlyDictionary<string, string> shared, IReadOnlyDictionary<string, string> specific)
    {
        var merged = new Dictionary<string, string>(shared);
        foreach (var (key, value) in specific)
        {
            merged[key] = value;
        }
        return merged;
    }
}
