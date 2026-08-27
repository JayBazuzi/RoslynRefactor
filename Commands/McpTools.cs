using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynRefactor;

// Exposes every RoslynRefactor CLI command as an MCP tool, driven entirely by the System.CommandLine
// Command/Option definitions those commands already declare. Adding a new ICommand to
// RoslynRefactorRootCommand automatically gets it an MCP tool with no changes needed here.
static class McpTools
{
    public static IEnumerable<McpServerTool> CreateAll() =>
        new RoslynRefactorRootCommand().Subcommands
            .Where(command => command.Name != McpCommand.Name)
            .Select(command => new CommandLineTool(command));

    sealed class CommandLineTool : McpServerTool
    {
        readonly Command command;

        public CommandLineTool(Command command)
        {
            this.command = command;
            ProtocolTool = new Tool
            {
                Name = command.Name,
                Description = command.Description,
                InputSchema = BuildInputSchema(command).Deserialize<JsonElement>(),
            };
        }

        public override Tool ProtocolTool { get; }
        public override IReadOnlyList<object> Metadata => [];

        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
        {
            var arguments = request.Params?.Arguments ?? new Dictionary<string, JsonElement>();
            var args = new List<string> { command.Name };
            foreach (var option in command.Options)
            {
                if (!arguments.TryGetValue(OptionKey(option), out var value))
                {
                    continue;
                }

                args.Add(option.Name);
                args.Add(value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString());
            }

            var (exitCode, output) = await InvokeCliAsync(args.ToArray(), cancellationToken);

            return new CallToolResult
            {
                IsError = exitCode != 0,
                Content = [new TextContentBlock { Text = output }],
            };
        }

        static async Task<(int ExitCode, string Output)> InvokeCliAsync(string[] args, CancellationToken cancellationToken)
        {
            var writer = new StringWriter();
            var previousOut = Console.Out;
            var previousError = Console.Error;
            Console.SetOut(writer);
            Console.SetError(writer);
            try
            {
                var exitCode = await new RoslynRefactorRootCommand().Parse(args).InvokeAsync(configuration: null, cancellationToken);
                return (exitCode, writer.ToString());
            }
            finally
            {
                Console.SetOut(previousOut);
                Console.SetError(previousError);
            }
        }

        static JsonNode BuildInputSchema(Command command)
        {
            var properties = new JsonObject();
            var required = new JsonArray();

            foreach (var option in command.Options)
            {
                properties[OptionKey(option)] = new JsonObject
                {
                    ["type"] = option.ValueType == typeof(int) ? "integer" : "string",
                    ["description"] = option.Description,
                };

                if (option.Required)
                {
                    required.Add(OptionKey(option));
                }
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required,
            };
        }

        static string OptionKey(Option option) => option.Name.TrimStart('-');
    }
}
