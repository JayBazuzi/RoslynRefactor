using System.CommandLine;

namespace RoslynRefactor.Tests;

public class RoslynRefactorRootCommandTests
{
    [Fact]
    public void RootCommand_has_expected_subcommands()
    {
        var root = new RoslynRefactorRootCommand();

        var subcommandNames = root.Subcommands.Select(c => c.Name).ToList();
        Verify(string.Join(Environment.NewLine, subcommandNames));
    }

    [Fact]
    public void ApproveAllHelpText()
    {
        var root = new RoslynRefactorRootCommand();

        var markdown = new System.Text.StringBuilder();
        markdown.AppendLine("# RoslynRefactor CLI help");
        markdown.AppendLine();
        AppendHelp(markdown, "RoslynRefactor", root);

        foreach (var command in root.Subcommands.OrderBy(c => c.Name))
        {
            AppendHelp(markdown, $"RoslynRefactor {command.Name}", command);
        }

        VerifyWithExtension(markdown.ToString(), ".md");
    }

    static void AppendHelp(System.Text.StringBuilder markdown, string title, Command command)
    {
        markdown.AppendLine($"## {title}");
        markdown.AppendLine();
        markdown.AppendLine(GetHelpText(command).TrimEnd());
        markdown.AppendLine();
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
