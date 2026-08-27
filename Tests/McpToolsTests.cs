using System.Text.Json;
using ApprovalUtilities.Utilities;
using ModelContextProtocol.Server;

namespace RoslynRefactor.Tests;

public class McpToolsTests
{
    [Fact]
    public void ApproveToolDescriptions()
    {
        VerifyWithExtension(
            McpTools.CreateAll()
                .OrderBy(t => t.ProtocolTool.Name)
                .Select(FormatToolDescription)
                .JoinWith(""),
            ".md"
        );
    }

    private string FormatToolDescription(McpServerTool tool)
    {
        var json = JsonSerializer.Serialize(
            tool.ProtocolTool.InputSchema,
            new JsonSerializerOptions { WriteIndented = true });
        return $"""
                ## {tool.ProtocolTool.Name}

                {tool.ProtocolTool.Description}

                ```json
                {json}
                ```


                """;
    }

}
