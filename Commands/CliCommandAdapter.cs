using System.CommandLine;

namespace RoslynRefactor;

// Builds a System.CommandLine Command from a CommandDescriptor. This is the only place that
// connects the CLI/MCP-agnostic CommandDescriptor to System.CommandLine.
static class CliCommandAdapter
{
    public static Command Build(CommandDescriptor descriptor)
    {
        var command = new Command(descriptor.Name, descriptor.Description);
        var getters = new List<(string Name, Func<ParseResult, string> GetValue)>();

        foreach (var parameter in descriptor.Parameters)
        {
            getters.Add((parameter.Name, AddOption(command, parameter)));
        }

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var arguments = getters.ToDictionary(g => g.Name, g => g.GetValue(parseResult));
            return await descriptor.ExecuteAsync(arguments, cancellationToken);
        });

        return command;
    }

    static Func<ParseResult, string> AddOption(Command command, CommandParameter parameter)
    {
        var name = "--" + parameter.Name;

        if (parameter.ValueType == typeof(int))
        {
            var option = new Option<int>(name) { Required = parameter.Required, Description = parameter.Description };
            command.Add(option);
            return parseResult => parseResult.GetValue(option).ToString();
        }

        var stringOption = new Option<string>(name) { Required = parameter.Required, Description = parameter.Description };
        command.Add(stringOption);
        return parseResult => parseResult.GetValue(stringOption)!;
    }
}
