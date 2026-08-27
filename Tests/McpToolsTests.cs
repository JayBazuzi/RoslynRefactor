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
            markdown.AppendLine($"## {tool.ProtocolTool.Name}");
            markdown.AppendLine();
            markdown.AppendLine(tool.ProtocolTool.Description);
            markdown.AppendLine();
            markdown.AppendLine("```json");
            markdown.AppendLine(JsonSerializer.Serialize(tool.ProtocolTool.InputSchema,
                new JsonSerializerOptions { WriteIndented = true }));
            markdown.AppendLine("```");
            markdown.AppendLine();
        }

        VerifyWithExtension(markdown.ToString(), ".md");
    }
}
