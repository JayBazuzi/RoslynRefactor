namespace RoslynRefactor;

// A named, typed argument a command accepts. Shared by both the CLI (System.CommandLine options)
// and the MCP server (JSON schema properties) - this type knows about neither.
sealed record CommandParameter(string Name, string Description, bool Required = true, Type? ValueType = null)
{
    public Type ValueType { get; } = ValueType ?? typeof(string);
}

// Everything needed to expose a refactoring as a command, independent of how it's invoked.
// Arguments are always passed as strings, keyed by CommandParameter.Name; ExecuteAsync parses
// them itself (e.g. int.Parse for line/column parameters). Progress/result text goes to the
// supplied TextWriter rather than Console, so the command has no notion of CLI or MCP.
sealed record CommandDescriptor(
    string Name,
    string Description,
    IReadOnlyList<CommandParameter> Parameters,
    Func<IReadOnlyDictionary<string, string>, TextWriter, CancellationToken, Task<int>> ExecuteAsync);

interface ICommand
{
    static abstract CommandDescriptor Descriptor { get; }
}
