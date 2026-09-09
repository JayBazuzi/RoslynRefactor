using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor.Tests;

// Data-driven: every {name}.input.cs in Fixtures/ExtractIefeNoCaptures is run through
// extract-iefe-no-captures and the resulting file is compared against {name}.output.cs. Mirrors
// MakeMethodStaticTests's approach for the same reason - the interesting behavior is in the
// variety of selections (statement runs with/without a return value, single-statement selections,
// expression selections) rather than in the CLI wiring, which EndToEndTests covers instead.
//
// A statement-span selection is marked with a "// begin selection" line immediately before, and a
// "// end selection" line immediately after, the selected statements (both lines are stripped
// before compiling). An expression selection is marked inline with /*[*/ .../*]*/ around the
// expression - the same two marker styles internal_documentation/extract-iefe-no-captures.md uses
// to illustrate extract-iefe-no-captures's two selection kinds.
//
// These fixtures (and the EndToEndTests case for this command) reflect the design as currently
// documented: captures become parameters, multiple return values become a tuple, and the wrapper
// is always a cast-and-invoked lambda, never a local function. The command's implementation
// (ExtractIefeNoCapturesCommand.cs) has not been updated to match yet - it still rejects any
// capture outright and emits a local function - so several of these are expected to fail until
// that catches up.
public class ExtractIefeNoCapturesTests
{
    static readonly string FixturesDir = Path.Combine(ProcessTestHost.FixturesSourceDir, "ExtractIefeNoCaptures");

    public static IEnumerable<object[]> Scenarios() => ProcessTestHost.Scenarios(FixturesDir);

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Wraps_the_marked_selection_to_match_the_expected_output(string scenario)
    {
        var rawInput = await File.ReadAllTextAsync(Path.Combine(FixturesDir, scenario + ".input.cs"));
        var expectedOutput = await File.ReadAllTextAsync(Path.Combine(FixturesDir, scenario + ".output.cs"));

        ProcessTestHost.AssertCompiles(scenario + ".output.cs", expectedOutput);

        var (content, span) = ExtractSelection(rawInput);

        ProcessTestHost.AssertCompiles(scenario + ".input.cs", content);

        var project = ProcessTestHost.CreateAdHocProject(scenario + ".cs", content);

        await ProcessTestHost.RunAsync(
            "extract-iefe-no-captures",
            "--project", project.ProjectPath,
            "--file", project.FilePath,
            "--start-line", span.start.line.ToString(),
            "--start-column", span.start.column.ToString(),
            "--end-line", span.end.line.ToString(),
            "--end-column", span.end.column.ToString());

        var actualOutput = await File.ReadAllTextAsync(project.FilePath);
        Assert.Equal(expectedOutput, actualOutput);
    }

    // Under the current design (internal_documentation/extract-iefe-no-captures.md), a read of an
    // outer local/parameter/this becomes a parameter instead of being rejected, and more than one
    // variable flowing out of a statement-span selection becomes a tuple return instead of being
    // rejected - see the "statement-span-with-return", "statement-span-with-this-parameter" and
    // "statement-span-with-tuple-return" fixtures for the (currently unimplemented - see below)
    // passing versions of those cases. The only capture-related failure left is a *write* to a
    // captured outer variable, which needs a `ref` parameter that the command's always-a-lambda,
    // Func<>/Action<>-cast output has no way to express.
    [Fact]
    public async Task Fails_when_the_selection_writes_to_a_captured_outer_variable()
    {
        var project = ProcessTestHost.CreateAdHocProject("Case.cs", """
            class Widget
            {
                static void Render()
                {
                    var count = 0;
                    count = count + 1;
                    System.Console.WriteLine(count);
                }
            }
            """);

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "extract-iefe-no-captures",
            "--project", project.ProjectPath,
            "--file", project.FilePath,
            "--start-line", "6", "--start-column", "9",
            "--end-line", "6", "--end-column", "27");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("'count'", result.StdErr);
    }

    [Fact]
    public async Task Fails_when_the_selection_contains_a_yield_statement()
    {
        var project = ProcessTestHost.CreateAdHocProject("Case.cs", """
            using System.Collections.Generic;

            class Widget
            {
                static IEnumerable<int> Yielder()
                {
                    var a = 1;
                    yield return a;
                }
            }
            """);

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "extract-iefe-no-captures",
            "--project", project.ProjectPath,
            "--file", project.FilePath,
            "--start-line", "7", "--start-column", "9",
            "--end-line", "8", "--end-column", "24");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("yield", result.StdErr);
    }

    [Fact]
    public async Task Fails_when_the_selection_contains_an_await_expression()
    {
        var project = ProcessTestHost.CreateAdHocProject("Case.cs", """
            using System.Threading.Tasks;

            class Widget
            {
                static async Task Awaiter()
                {
                    var t = Task.CompletedTask;
                    await t;
                }
            }
            """);

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "extract-iefe-no-captures",
            "--project", project.ProjectPath,
            "--file", project.FilePath,
            "--start-line", "7", "--start-column", "9",
            "--end-line", "8", "--end-column", "17");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("await", result.StdErr);
    }

    [Fact]
    public async Task Fails_when_the_selection_is_a_partial_statement()
    {
        var project = ProcessTestHost.CreateAdHocProject("Case.cs", """
            class Widget
            {
                static void Render()
                {
                    var result = 1 + 2;
                    System.Console.WriteLine(result);
                }
            }
            """);

        // Selects "var result = 1 + 2" without the trailing semicolon - neither a complete
        // statement nor a complete expression.
        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "extract-iefe-no-captures",
            "--project", project.ProjectPath,
            "--file", project.FilePath,
            "--start-line", "4", "--start-column", "9",
            "--end-line", "4", "--end-column", "28");

        Assert.NotEqual(0, result.ExitCode);
    }

    static (string Content, LineAndColumnSpan Span) ExtractSelection(string rawInput)
    {
        const string BeginMarker = "// begin selection";
        if (rawInput.Contains(BeginMarker, StringComparison.Ordinal))
        {
            return ExtractStatementSelection(rawInput);
        }

        const string StartExpressionMarker = "/*[*/";
        if (rawInput.Contains(StartExpressionMarker, StringComparison.Ordinal))
        {
            return ExtractExpressionSelection(rawInput);
        }

        throw new InvalidOperationException(
            "expected either \"// begin selection\"/\"// end selection\" lines or /*[*/.../*]*/ markers.");
    }

    // Strips the "// begin selection"/"// end selection" marker lines entirely (including their
    // own trailing newline), and reports the statement span as running from where the first line
    // used to start to where the second line used to start - since removing a marker line makes
    // its position become exactly the start of whatever followed it, that's true both immediately
    // after removing the begin marker and, since the end marker sits later in the text and its
    // removal doesn't shift anything before it, after removing the end marker too.
    static (string Content, LineAndColumnSpan Span) ExtractStatementSelection(string rawInput)
    {
        const string BeginMarker = "// begin selection";
        const string EndMarker = "// end selection";

        var beginMarkerIndex = rawInput.IndexOf(BeginMarker, StringComparison.Ordinal);
        var beginLineStart = rawInput.LastIndexOf('\n', beginMarkerIndex) + 1;
        var afterBeginLine = rawInput.IndexOf('\n', beginMarkerIndex) + 1;
        if (afterBeginLine == 0)
        {
            throw new InvalidOperationException("expected content after the \"// begin selection\" line.");
        }
        var withoutBeginMarker = rawInput.Remove(beginLineStart, afterBeginLine - beginLineStart);
        var selectionStart = beginLineStart;

        var endMarkerIndex = withoutBeginMarker.IndexOf(EndMarker, StringComparison.Ordinal);
        if (endMarkerIndex < 0)
        {
            throw new InvalidOperationException("expected an \"// end selection\" line after \"// begin selection\".");
        }
        var endLineStart = withoutBeginMarker.LastIndexOf('\n', endMarkerIndex) + 1;
        var afterEndLine = withoutBeginMarker.IndexOf('\n', endMarkerIndex) + 1;
        var content = afterEndLine == 0
            ? withoutBeginMarker[..endLineStart]
            : withoutBeginMarker.Remove(endLineStart, afterEndLine - endLineStart);
        var selectionEnd = endLineStart;

        var text = SourceText.From(content);
        var startPosition = text.Lines.GetLinePosition(selectionStart);
        var endPosition = text.Lines.GetLinePosition(selectionEnd);
        var span = new LineAndColumnSpan(
            new LineAndColumn(startPosition.Line + 1, startPosition.Character + 1),
            new LineAndColumn(endPosition.Line + 1, endPosition.Character + 1));
        return (content, span);
    }

    static (string Content, LineAndColumnSpan Span) ExtractExpressionSelection(string rawInput)
    {
        const string StartMarker = "/*[*/";
        const string EndMarker = "/*]*/";

        var startMarkerIndex = rawInput.IndexOf(StartMarker, StringComparison.Ordinal);
        var withoutStartMarker = rawInput.Remove(startMarkerIndex, StartMarker.Length);
        var selectionStart = startMarkerIndex;

        var endMarkerIndex = withoutStartMarker.IndexOf(EndMarker, StringComparison.Ordinal);
        if (endMarkerIndex < 0)
        {
            throw new InvalidOperationException("expected an /*]*/ marker after /*[*/.");
        }
        var content = withoutStartMarker.Remove(endMarkerIndex, EndMarker.Length);
        var selectionEnd = endMarkerIndex;

        var text = SourceText.From(content);
        var startPosition = text.Lines.GetLinePosition(selectionStart);
        var endPosition = text.Lines.GetLinePosition(selectionEnd);
        var span = new LineAndColumnSpan(
            new LineAndColumn(startPosition.Line + 1, startPosition.Character + 1),
            new LineAndColumn(endPosition.Line + 1, endPosition.Character + 1));
        return (content, span);
    }
}
