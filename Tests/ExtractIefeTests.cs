namespace RoslynRefactor.Tests;

// Specification for the `extract-iefe` command, which does not exist yet - these tests are red on
// purpose.
//
// Extract IEFE (Immediately-Executed Function Expression) wraps the selection in a lambda that is
// invoked right where the selection was. A selection of whole statements becomes an Action:
//
//     ((Action)(() =>
//     {
//         // the selected statements
//     }))();
//
// A selection of a single expression instead becomes a Func<T> (or an Action, when the expression's
// type is void) evaluated in place, so it can sit inside a larger expression:
//
//     ((Func<T>)(() => the selected expression))()
//
// Either way it is the safe half of Extract Method. Because the lambda closes over everything it
// uses, no parameter list or return type has to be invented, so the move is mechanical and
// behavior-preserving; turning the IEFE into a real method (naming it, hoisting captures into
// parameters) is a separate step. That only holds while the selection is something a lambda can
// express, so the cases a lambda cannot express - control flow that leaves the selection, ref/out
// capture, locals that escape the selection - must be refused rather than silently changing
// behavior.
//
// Each case lives on disk under Fixtures/ExtractIefe/{Accepts,Refuses}/<CaseName>, so adding a case
// is adding a folder, not editing this file.
public class ExtractIefeTests
{
    static readonly string CasesDir = Path.Combine(ProcessTestHost.FixturesSourceDir, "ExtractIefe");

    [Fact]
    public void The_root_command_offers_extract_iefe()
    {
        var root = new RoslynRefactorRootCommand();

        var command = Assert.Single(root.Subcommands.Where(c => c.Name == "extract-iefe"));
        Assert.Equal("Extract selected statements into an immediately-executed function expression", command.Description);
    }

    [Fact]
    public async Task Refuses_when_the_selection_is_out_of_range()
    {
        var project = ProcessTestHost.CreateProjectWithSource(
            """
            class Program
            {
                static void Main()
                {
                }
            }
            """);

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "extract-iefe",
            "--project", project.ProjectPath,
            "--file", project.ProgramFilePath,
            "--start-line", "900", "--start-column", "1",
            "--end-line", "900", "--end-column", "1");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("out of range", result.StdErr);
    }

    public static TheoryData<string> AcceptCases() => CaseNames("Accepts");

    [Theory]
    [MemberData(nameof(AcceptCases))]
    public async Task Accepts(string caseName)
    {
        var caseDir = Path.Combine(CasesDir, "Accepts", caseName);
        var markup = SelectionMarkup.Parse(await File.ReadAllTextAsync(Path.Combine(caseDir, "Input.cs")));
        var expected = await File.ReadAllTextAsync(Path.Combine(caseDir, "Expected.cs"));

        var project = ProcessTestHost.CreateProjectWithSource(markup.Source);

        var result = await ProcessTestHost.RunAsync(
            "extract-iefe",
            "--project", project.ProjectPath,
            "--file", project.ProgramFilePath,
            "--start-line", markup.StartLine.ToString(),
            "--start-column", markup.StartColumn.ToString(),
            "--end-line", markup.EndLine.ToString(),
            "--end-column", markup.EndColumn.ToString());

        Assert.Contains("updated:", result.StdOut);
        AssertSource(expected, await File.ReadAllTextAsync(project.ProgramFilePath));
    }

    public static TheoryData<string> RefuseCases() => CaseNames("Refuses");

    [Theory]
    [MemberData(nameof(RefuseCases))]
    public async Task Refuses(string caseName)
    {
        var caseDir = Path.Combine(CasesDir, "Refuses", caseName);
        var markup = SelectionMarkup.Parse(await File.ReadAllTextAsync(Path.Combine(caseDir, "Input.cs")));
        var expectedStdErrLines = await File.ReadAllLinesAsync(Path.Combine(caseDir, "ExpectedStdErr.txt"));

        var project = ProcessTestHost.CreateProjectWithSource(markup.Source);

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "extract-iefe",
            "--project", project.ProjectPath,
            "--file", project.ProgramFilePath,
            "--start-line", markup.StartLine.ToString(),
            "--start-column", markup.StartColumn.ToString(),
            "--end-line", markup.EndLine.ToString(),
            "--end-column", markup.EndColumn.ToString());

        Assert.Equal(1, result.ExitCode);
        foreach (var expected in expectedStdErrLines.Where(line => line.Length > 0))
        {
            Assert.Contains(expected, result.StdErr);
        }

        // A refusal must leave the file alone, not half-apply the change.
        AssertSource(markup.Source, await File.ReadAllTextAsync(project.ProgramFilePath));
    }

    static TheoryData<string> CaseNames(string category) =>
        new(Directory.GetDirectories(Path.Combine(CasesDir, category))
            .Select(Path.GetFileName)!
            .OrderBy(name => name, StringComparer.Ordinal));

    static void AssertSource(string expected, string actual) =>
        Assert.Equal(Normalize(expected), Normalize(actual));

    // Line endings and a byte-order mark are Roslyn's business, not the specification's.
    static string Normalize(string source) =>
        source.TrimStart('\uFEFF').ReplaceLineEndings("\n").TrimEnd() + "\n";
}
