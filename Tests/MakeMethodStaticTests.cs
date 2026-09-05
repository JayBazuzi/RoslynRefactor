namespace RoslynRefactor.Tests;

// Data-driven: every {name}.input.cs in Fixtures/MakeMethodStatic is run through make-method-static
// and the resulting file is compared against {name}.output.cs. This is used instead of one
// hand-written test per scenario because the interesting behavior here is almost entirely in the
// variety of code shapes the body rewrite has to handle (fields, properties, events, recursion,
// indexers, ...), not in the CLI wiring - see EndToEndTests for that side of things.
//
// Each input file marks the method to convert with a "/*caret*/" comment immediately before its
// name, and may start with a "// parameter-name: <name>" line to pass --parameter-name; both are
// stripped/consumed before the file is compiled.
public class MakeMethodStaticTests
{
    static readonly string FixturesDir = Path.Combine(ProcessTestHost.FixturesSourceDir, "MakeMethodStatic");

    public static IEnumerable<object[]> Scenarios() =>
        Directory.GetFiles(FixturesDir, "*.input.cs")
            .Select(path => Path.GetFileName(path)[..^".input.cs".Length])
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Converts_the_marked_method_to_match_the_expected_output(string scenario)
    {
        var rawInput = await File.ReadAllTextAsync(Path.Combine(FixturesDir, scenario + ".input.cs"));
        var expectedOutput = await File.ReadAllTextAsync(Path.Combine(FixturesDir, scenario + ".output.cs"));

        var (parameterName, withoutPragma) = ExtractParameterNamePragma(rawInput);
        var (content, line, column) = ExtractCaret(withoutPragma);

        var project = ProcessTestHost.CreateAdHocProject(scenario + ".cs", content);

        var args = new List<string>
        {
            "make-method-static",
            "--project", project.ProjectPath,
            "--file", project.FilePath,
            "--line", line.ToString(),
            "--column", column.ToString(),
        };
        if (parameterName is not null)
        {
            args.Add("--parameter-name");
            args.Add(parameterName);
        }

        await ProcessTestHost.RunAsync(args.ToArray());

        var actualOutput = await File.ReadAllTextAsync(project.FilePath);
        Assert.Equal(expectedOutput, actualOutput);
    }

    [Fact]
    public async Task Fails_when_the_method_is_virtual()
    {
        var project = ProcessTestHost.CreateAdHocProject("Case.cs", """
            class Widget
            {
                public virtual void Increment(int by) { }
            }
            """);

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "make-method-static",
            "--project", project.ProjectPath,
            "--file", project.FilePath,
            "--line", "3", "--column", "25");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task Fails_when_the_method_implements_an_interface()
    {
        var project = ProcessTestHost.CreateAdHocProject("Case.cs", """
            interface IWidget
            {
                void Increment(int by);
            }

            class Widget : IWidget
            {
                public void Increment(int by) { }
            }
            """);

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "make-method-static",
            "--project", project.ProjectPath,
            "--file", project.FilePath,
            "--line", "8", "--column", "17");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task Fails_when_the_method_is_already_static()
    {
        var project = ProcessTestHost.CreateAdHocProject("Case.cs", """
            class Widget
            {
                static void Increment(int by) { }
            }
            """);

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "make-method-static",
            "--project", project.ProjectPath,
            "--file", project.FilePath,
            "--line", "3", "--column", "17");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task Fails_when_a_call_site_uses_the_method_as_a_delegate()
    {
        var project = ProcessTestHost.CreateAdHocProject("Case.cs", """
            using System;

            class Widget
            {
                int _count;

                void Increment(int by) => _count += by;

                Action<int> AsDelegate() => Increment;
            }
            """);

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "make-method-static",
            "--project", project.ProjectPath,
            "--file", project.FilePath,
            "--line", "7", "--column", "10");

        Assert.NotEqual(0, result.ExitCode);
    }

    // Consumes a "// parameter-name: <name>" first line, if present, returning the name and the
    // remaining content with that line removed.
    static (string? ParameterName, string Content) ExtractParameterNamePragma(string content)
    {
        const string Prefix = "// parameter-name: ";
        if (!content.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return (null, content);
        }

        var newlineIndex = content.IndexOf('\n');
        var name = content[Prefix.Length..newlineIndex].Trim();
        return (name, content[(newlineIndex + 1)..]);
    }

    // Consumes the "/*caret*/" marker that sits immediately before the method name to convert,
    // returning the marker-free content plus the 1-based line/column where the method name now
    // starts.
    static (string Content, int Line, int Column) ExtractCaret(string content)
    {
        const string Marker = "/*caret*/";
        var index = content.IndexOf(Marker, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException("expected a /*caret*/ marker before the method name.");
        }
        if (content.IndexOf(Marker, index + 1, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException("expected exactly one /*caret*/ marker.");
        }

        var withoutMarker = content.Remove(index, Marker.Length);

        var line = 1;
        var lastNewline = -1;
        for (var i = 0; i < index; i++)
        {
            if (withoutMarker[i] == '\n')
            {
                line++;
                lastNewline = i;
            }
        }
        var column = index - lastNewline;

        return (withoutMarker, line, column);
    }
}
