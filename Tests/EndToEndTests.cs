using ApprovalTests.Namers;

namespace RoslynRefactor.Tests;

// One black-box test per command: run the real built exe against a scratch copy of the
// Fixtures/Sample solution and assert on exit code / file contents. Roslyn's own refactoring
// logic is well tested upstream, so these only need to prove our CLI wiring, span math, and
// workspace integration work end to end - not exhaustively cover refactoring edge cases.
public class EndToEndTests
{
    [Fact]
    public async Task ExtractMethod_extracts_selected_statements()
    {
        var sample = ProcessTestHost.CreateSampleCopy();

        var result = await ProcessTestHost.RunAsync(
            "extract-method",
            "--project", sample.SolutionPath,
            "--file", sample.ProgramFilePath,
            "--start-line", "22", "--start-column", "9",
            "--end-line", "28", "--end-column", "10");

        var content = await File.ReadAllTextAsync(sample.ProgramFilePath);
        Approvals.Verify(content);
    }

    [Fact]
    public async Task IntroduceVariable_introduces_a_local_for_the_selected_expression()
    {
        var sample = ProcessTestHost.CreateSampleCopy();

        var result = await ProcessTestHost.RunAsync(
            "introduce-variable",
            "--project", sample.SolutionPath,
            "--file", sample.ProgramFilePath,
            "--start-line", "17", "--start-column", "27",
            "--end-line", "17", "--end-column", "38");

        var content = await File.ReadAllTextAsync(sample.ProgramFilePath);
        Approvals.Verify(content);
    }

    [Fact]
    public async Task InlineTemporaryVariable_inlines_the_local_into_its_usages()
    {
        var sample = ProcessTestHost.CreateSampleCopy();

        var result = await ProcessTestHost.RunAsync(
            "inline-temporary-variable",
            "--project", sample.SolutionPath,
            "--file", sample.ProgramFilePath,
            "--start-line", "16", "--start-column", "13",
            "--end-line", "16", "--end-column", "20");

        var content = await File.ReadAllTextAsync(sample.ProgramFilePath);
        Approvals.Verify(content);
    }

    [Fact]
    public async Task InlineMethod_inlines_the_called_method_at_the_call_site()
    {
        var sample = ProcessTestHost.CreateSampleCopy();

        var result = await ProcessTestHost.RunAsync(
            "inline-method",
            "--project", sample.SolutionPath,
            "--file", sample.ProgramFilePath,
            "--start-line", "18", "--start-column", "9",
            "--end-line", "18", "--end-column", "25");

        var content = await File.ReadAllTextAsync(sample.ProgramFilePath);
        Approvals.Verify(content);
    }

    [Fact]
    public async Task Rename_renames_the_symbol_at_the_given_position()
    {
        var sample = ProcessTestHost.CreateSampleCopy();

        var result = await ProcessTestHost.RunAsync(
            "rename",
            "--project", sample.SolutionPath,
            "--file", sample.ProgramFilePath,
            "--line", "16", "--column", "13",
            "--to", "newName");

        var content = await File.ReadAllTextAsync(sample.ProgramFilePath);
        Approvals.Verify(content);
    }

    [Fact]
    public async Task Rename_fails_when_the_new_name_collides_with_an_existing_symbol_in_scope()
    {
        var sample = ProcessTestHost.CreateSampleCopy();

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "rename",
            "--project", sample.SolutionPath,
            "--file", sample.ProgramFilePath,
            "--line", "16", "--column", "13",
            "--to", "widgets");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task ConvertToLinqCallForm_converts_the_foreach_loop()
    {
        var sample = ProcessTestHost.CreateSampleCopy();

        var result = await ProcessTestHost.RunAsync(
            "convert-to-linq-call-form",
            "--project", sample.SolutionPath,
            "--file", sample.ProgramFilePath,
            "--start-line", "22", "--start-column", "9",
            "--end-line", "28", "--end-column", "10");

        var content = await File.ReadAllTextAsync(sample.ProgramFilePath);
        Approvals.Verify(content);
    }

    [Fact]
    public async Task ConvertToLinqQueryForm_converts_the_foreach_loop()
    {
        var sample = ProcessTestHost.CreateSampleCopy();

        var result = await ProcessTestHost.RunAsync(
            "convert-to-linq-query-form",
            "--project", sample.SolutionPath,
            "--file", sample.ProgramFilePath,
            "--start-line", "22", "--start-column", "9",
            "--end-line", "28", "--end-column", "10");

        var content = await File.ReadAllTextAsync(sample.ProgramFilePath);
        Approvals.Verify(content);
    }
}
