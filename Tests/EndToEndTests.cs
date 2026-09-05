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
    public async Task MoveStaticMember_moves_the_static_method_to_the_destination_type()
    {
        var sample = ProcessTestHost.CreateSampleCopy();

        var result = await ProcessTestHost.RunAsync(
            "move-static-member",
            "--project", sample.SolutionPath,
            "--file", sample.ProgramFilePath,
            "--line", "31", "--column", "17",
            "--to", "Sample.Widget");

        var content = await File.ReadAllTextAsync(sample.ProgramFilePath);
        Approvals.Verify(content);
    }

    [Fact]
    public async Task MoveStaticMember_fails_when_the_member_is_not_static()
    {
        var sample = ProcessTestHost.CreateSampleCopy();

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "move-static-member",
            "--project", sample.SolutionPath,
            "--file", sample.ProgramFilePath,
            "--line", "9", "--column", "16",
            "--to", "Sample.Program");

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
    public async Task BatchResponseFile_runs_the_command_once_per_line()
    {
        var sample = ProcessTestHost.CreateSampleCopy();
        var batchFilePath = Path.Combine(Path.GetDirectoryName(sample.SolutionPath)!, "batch.txt");
        await File.WriteAllLinesAsync(batchFilePath,
        [
            "--line 21 --column 13 --to totals",
            "--line 22 --column 22 --to item",
        ]);

        var result = await ProcessTestHost.RunAsync(
            "rename",
            "--project", sample.SolutionPath,
            "--file", sample.ProgramFilePath,
            "@" + batchFilePath);

        var content = await File.ReadAllTextAsync(sample.ProgramFilePath);
        Approvals.Verify(content);
    }

    [Fact]
    public async Task BatchResponseFile_renames_are_unaffected_by_earlier_renames_shifting_positions_on_the_same_line()
    {
        // Both "results" and "widget" appear on line 26 ("results.Add(widget.Value * 2);"), at
        // columns 17 and 29 respectively. Renaming "results" (7 chars) to something much longer
        // shifts everything after it on that line, so if the batch resolved "widget" by column
        // *after* applying the first rename, it would land on the wrong token - or no token at
        // all. Both positions here are given relative to the file's ORIGINAL contents.
        var sample = ProcessTestHost.CreateSampleCopy();
        var batchFilePath = Path.Combine(Path.GetDirectoryName(sample.SolutionPath)!, "batch.txt");
        await File.WriteAllLinesAsync(batchFilePath,
        [
            "--line 26 --column 17 --to renamedResultsList",
            "--line 26 --column 29 --to renamedWidgetVar",
        ]);

        var result = await ProcessTestHost.RunAsync(
            "rename",
            "--project", sample.SolutionPath,
            "--file", sample.ProgramFilePath,
            "@" + batchFilePath);

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
