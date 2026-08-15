namespace RoslynRefactor;

static class CommandLine
{
    record CommandInfo(string Name, string Description, Func<string[], Task<int>> RunAsync);

    static readonly IReadOnlyList<CommandInfo> Commands =
    [
        Describe<RenameCommand>(),
        Describe<ExtractMethodCommand>(),
        Describe<IntroduceVariableCommand>(),
    ];

    static CommandInfo Describe<T>() where T : ICommand => new(T.Name, T.Description, T.RunAsync);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var commandName = args[0];
        var rest = args[1..];

        if (commandName is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var command = Commands.FirstOrDefault(c => c.Name == commandName);
        if (command is null)
        {
            Console.Error.WriteLine($"error: unknown command '{commandName}'");
            PrintUsage();
            return 1;
        }

        try
        {
            return await command.RunAsync(rest);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    static void PrintUsage()
    {
        var width = Commands.Count == 0 ? 0 : Commands.Max(c => c.Name.Length);

        Console.WriteLine("""
            RoslynRefactor - Roslyn-powered C# refactoring CLI

            Usage:
              RoslynRefactor <command> [options]

            Commands:
            """);

        foreach (var command in Commands)
        {
            Console.WriteLine($"  {command.Name.PadRight(width)}  {command.Description}");
        }

        Console.WriteLine();
        Console.WriteLine("Run 'RoslynRefactor <command> --help' for command-specific options.");
    }
}
