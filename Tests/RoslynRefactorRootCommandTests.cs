using System.CommandLine;
using ApprovalTests.Namers;
using System.Linq;

namespace RoslynRefactor.Tests;

public class RoslynRefactorRootCommandTests
{
    [Fact]
    public void ApproveIndexOfAvailableCommands()
    {
        var root = new RoslynRefactorRootCommand();

        var markdown = new System.Text.StringBuilder();
        markdown.AppendLine("| Command | Description |");
        markdown.AppendLine("| --- | --- |");
        foreach (var command in root.Subcommands.OrderBy(c => c.Name))
        {
            var fileName = $"RoslynRefactorRootCommandTests.ApproveHelpText.{command.Name.Replace(" ", "_")}.approved.md";
            markdown.AppendLine($"| [{command.Name}]({fileName}) | {command.Description} |");
        }

        VerifyWithExtension(markdown.ToString(), ".md");
    }


    public static IEnumerable<object[]> Commands()
    {
        var root = new RoslynRefactorRootCommand();
        return root.Subcommands.OrderBy(c => c.Name).Select(command => new object[] { command });
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void ApproveHelpText(Command command)
    {
        var markdown = new System.Text.StringBuilder();
        markdown.AppendLine($"## {command.Name}");
        markdown.AppendLine();
        markdown.AppendLine(GetHelpText(command).TrimEnd());

        NamerFactory.AdditionalInformation = command.Name.Replace(" ", "_");
        VerifyWithExtension(markdown.ToString(), ".md");
    }

    static string GetHelpText(Command command)
    {
        using var writer = new StringWriter();
        var config = new InvocationConfiguration
        {
            Output = writer,
            Error = writer,
        };
        command.Parse("--help").Invoke(config);
        return writer.ToString().Replace("testhost", "RoslynRefactor");
    }
}
