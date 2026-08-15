namespace RoslynRefactor;

static class CommandLine
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0];
        var rest = args[1..];

        try
        {
            return command switch
            {
                "rename" => await RenameCommand.RunAsync(rest),
                "extract-method" => await ExtractMethodCommand.RunAsync(rest),
                "-h" or "--help" or "help" => Help(),
                _ => Unknown(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    static int Help()
    {
        PrintUsage();
        return 0;
    }

    static int Unknown(string command)
    {
        Console.Error.WriteLine($"error: unknown command '{command}'");
        PrintUsage();
        return 1;
    }

    static void PrintUsage()
    {
        Console.WriteLine("""
            RoslynRefactor - Roslyn-powered C# refactoring CLI

            Usage:
              RoslynRefactor <command> [options]

            Commands:
              rename            Rename a symbol across a solution/project
              extract-method    Extract selected statements into a new method

            Run 'RoslynRefactor <command> --help' for command-specific options.
            """);
    }
}
