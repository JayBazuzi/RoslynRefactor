using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor.Tests;

public class CommandSupportTests
{
    static readonly SourceText Text = SourceText.From("first\nsecond\nthird");

    [Fact]
    public void ToSpan_converts_1_based_line_and_column_to_a_TextSpan()
    {
        // "second" starts at offset 6 (after "first\n"); column 3 is 2 chars in -> offset 8.
        var span = CommandSupport.ToTextSpan(Text, new LineAndColumnSpan(new LineAndColumn(2, 3), new LineAndColumn(2, 6)));

        Assert.NotNull(span);
        Assert.Equal("con", Text.ToString(span.Value));
    }

    [Theory]
    [InlineData(1, 1, 5, 1)] // end line beyond range
    [InlineData(10, 1, 10, 1)] // start line beyond range
    public void ToSpan_returns_null_when_a_line_is_out_of_range(int startLine, int startColumn, int endLine, int endColumn)
    {
        var span = CommandSupport.ToTextSpan(Text, new LineAndColumnSpan(new LineAndColumn(startLine, startColumn), new LineAndColumn(endLine, endColumn)));

        Assert.Null(span);
    }

    [Fact]
    public void ToSpan_returns_null_when_the_end_position_precedes_the_start_position()
    {
        var span = CommandSupport.ToTextSpan(Text, new LineAndColumnSpan(new LineAndColumn(2, 5), new LineAndColumn(2, 3)));

        Assert.Null(span);
    }

    [Fact]
    public void FindDocument_matches_by_full_path_case_insensitively()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var project = workspace.AddProject(ProjectInfo.Create(projectId, VersionStamp.Default, "Proj", "Proj", LanguageNames.CSharp));
        var filePath = Path.Combine(Path.GetTempPath(), "Foo.cs");
        var documentInfo = DocumentInfo.Create(
            DocumentId.CreateNewId(project.Id),
            "Foo.cs",
            filePath: filePath,
            loader: TextLoader.From(TextAndVersion.Create(SourceText.From("class Foo {}"), VersionStamp.Default)));
        var document = workspace.AddDocument(documentInfo);

        var found = CommandSupport.FindDocument(document.Project.Solution, filePath.ToUpperInvariant());

        Assert.Equal(document.Id, found?.Id);
    }

    [Fact]
    public void FindDocument_returns_null_when_no_document_matches()
    {
        using var workspace = new AdhocWorkspace();
        var solution = workspace.CurrentSolution;

        var found = CommandSupport.FindDocument(solution, Path.Combine(Path.GetTempPath(), "Missing.cs"));

        Assert.Null(found);
    }
}
