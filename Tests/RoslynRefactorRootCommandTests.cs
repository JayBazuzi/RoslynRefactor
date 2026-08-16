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
}
