using System.Text.Json;

namespace RoslynRefactor.Tests;

public class McpToolsTests
{
    [Fact]
    public void ApproveToolDescriptions()
    {
        var tools = McpTools.CreateAll().OrderBy(t => t.ProtocolTool.Name);

        var markdown = new System.Text.StringBuilder();
        foreach (var tool in tools)
        {
            var json = JsonSerializer.Serialize(
                tool.ProtocolTool.InputSchema,
                new JsonSerializerOptions { WriteIndented = true });
            markdown.Append($"""
                ## {tool.ProtocolTool.Name}

                {tool.ProtocolTool.Description}

                ```json
                {json}
                ```


                """);
        }

        VerifyWithExtension(markdown.ToString(), ".md");
    }
}
