using ModelContextProtocol.Client;

namespace RoslynRefactor.Tests;

// Exercises the real MCP server over stdio (the built exe running `mcp`), not just the
// System.CommandLine wiring it's built from - proves tool discovery and tool invocation work
// end to end through the actual protocol.
public class McpEndToEndTests
{
    [Fact]
    public async Task Rename_tool_call_renames_the_symbol_at_the_given_position()
    {
        var sample = ProcessTestHost.CreateSampleCopy();

        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new()
        {
            Command = ProcessTestHost.ToolExePath,
            Arguments = ["mcp"],
        }));

        var result = await client.CallToolAsync("rename", new Dictionary<string, object?>
        {
            ["project"] = sample.SolutionPath,
            ["file"] = sample.ProgramFilePath,
            ["line"] = 16,
            ["column"] = 13,
            ["to"] = "newName",
        });

        Assert.False(result.IsError, string.Join('\n', result.Content));

        var content = await File.ReadAllTextAsync(sample.ProgramFilePath);
        Approvals.Verify(content);
    }
}
