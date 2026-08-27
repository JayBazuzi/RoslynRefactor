using System.CommandLine;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RoslynRefactor;

sealed class McpCommand : ICommand
{
    public const string Name = "mcp";

    public static Command Build()
    {
        var command = new Command(Name, "Run an MCP server exposing every refactoring command as a tool, over stdio");
        command.SetAction(async (_, cancellationToken) => await RunAsync(cancellationToken));
        return command;
    }

    static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        builder.Services
            .AddMcpServer(options => options.ServerInfo = new() { Name = "RoslynRefactor", Version = GetVersion() })
            .WithStdioServerTransport()
            .WithTools(McpTools.CreateAll());

        await builder.Build().RunAsync(cancellationToken);
        return 0;
    }

    static string GetVersion() =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";
}
