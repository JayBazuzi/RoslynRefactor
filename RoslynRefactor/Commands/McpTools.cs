using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace RoslynRefactor;

// Exposes every RoslynRefactor CommandDescriptor as an MCP tool. Adding a new ICommand to
// RoslynRefactorRootCommand.RefactorDescriptors automatically gets it an MCP tool with no changes needed here.
static class McpTools
{
    public static IEnumerable<McpServerTool> CreateAll() =>
        RoslynRefactorRootCommand.RefactorDescriptors.Select(DescriptorTool.Create);

    sealed class DescriptorTool : McpServerTool
    {
        readonly CommandDescriptor descriptor;

        public static DescriptorTool Create(CommandDescriptor descriptor) => new(descriptor);

        public DescriptorTool(CommandDescriptor descriptor)
        {
            this.descriptor = descriptor;
            ProtocolTool = new Tool
            {
                Name = descriptor.Name,
                Description = descriptor.Description,
                InputSchema = BuildInputSchema(descriptor).Deserialize<JsonElement>(),
            };
        }

        public override Tool ProtocolTool { get; }
        public override IReadOnlyList<object> Metadata => [];

        public override async ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
        {
            var arguments = request.Params?.Arguments ?? new Dictionary<string, JsonElement>();
            var stringArguments = descriptor.Parameters
                .Where(p => arguments.ContainsKey(p.Name))
                .ToDictionary(p => p.Name, p => AsString(arguments[p.Name]));

            var writer = new StringWriter();
            int exitCode;
            try
            {
                exitCode = await descriptor.ExecuteAsync(stringArguments, writer, cancellationToken);
            }
            catch (Exception ex)
            {
                writer.WriteLine($"error: {ex.Message}");
                exitCode = 1;
            }

            return new CallToolResult
            {
                IsError = exitCode != 0,
                Content = [new TextContentBlock { Text = writer.ToString() }],
            };
        }

        static string AsString(JsonElement value) =>
            value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ToString();

        static JsonNode BuildInputSchema(CommandDescriptor descriptor)
        {
            var properties = new JsonObject();
            var required = new JsonArray();

            foreach (var parameter in descriptor.Parameters)
            {
                properties[parameter.Name] = new JsonObject
                {
                    ["type"] = parameter.ValueType == typeof(int) ? "integer" : "string",
                    ["description"] = parameter.Description,
                };

                if (parameter.Required)
                {
                    required.Add(parameter.Name);
                }
            }

            return new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = required,
            };
        }
    }
}
