namespace RoslynRefactor.Tests;

// Specification for the `extract-iefe` command, which does not exist yet - these tests are red on
// purpose.
//
// Extract IEFE (Immediately-Executed Function Expression) wraps the selected statements in a lambda
// that is invoked right where they were:
//
//     ((Action)(() =>
//     {
//         // the selected statements
//     }))();
//
// It is the safe half of Extract Method. Because the lambda closes over everything it uses, no
// parameter list or return type has to be invented, so the move is mechanical and behavior-
// preserving; turning the IEFE into a real method (naming it, hoisting captures into parameters) is
// a separate step. That only holds while the selection is something a lambda can express, so the
// cases a lambda cannot express - control flow that leaves the selection, ref/out capture, locals
// that escape the selection - must be refused rather than silently changing behavior.
public class ExtractIefeTests
{
    [Fact]
    public void The_root_command_offers_extract_iefe()
    {
        var root = new RoslynRefactorRootCommand();

        var command = Assert.Single(root.Subcommands.Where(c => c.Name == "extract-iefe"));
        Assert.Equal("Extract selected statements into an immediately-executed function expression", command.Description);
    }

    [Fact]
    public async Task Wraps_the_selected_statements_in_an_immediately_executed_lambda()
    {
        var (source, result) = await ExtractIefeAsync(
            """
            using System;

            class Program
            {
                static void Main()
                {
                    var greeting = "hello";
                    [|Console.WriteLine(greeting);
                    Console.WriteLine(greeting.Length);|]
                }
            }
            """);

        AssertSource(
            """
            using System;

            class Program
            {
                static void Main()
                {
                    var greeting = "hello";
                    ((Action)(() =>
                    {
                        Console.WriteLine(greeting);
                        Console.WriteLine(greeting.Length);
                    }))();
                }
            }
            """,
            source);
        Assert.Contains("updated:", result.StdOut);
    }

    [Fact]
    public async Task Wraps_a_single_selected_statement()
    {
        var (source, _) = await ExtractIefeAsync(
            """
            using System;

            class Program
            {
                static void Main()
                {
                    [|Console.WriteLine("hello");|]
                }
            }
            """);

        AssertSource(
            """
            using System;

            class Program
            {
                static void Main()
                {
                    ((Action)(() =>
                    {
                        Console.WriteLine("hello");
                    }))();
                }
            }
            """,
            source);
    }

    [Fact]
    public async Task Indents_the_wrapper_at_the_depth_of_the_selection()
    {
        var (source, _) = await ExtractIefeAsync(
            """
            using System;

            class Program
            {
                static void Main(bool flag)
                {
                    if (flag)
                    {
                        [|Console.WriteLine("nested");|]
                    }
                }
            }
            """);

        AssertSource(
            """
            using System;

            class Program
            {
                static void Main(bool flag)
                {
                    if (flag)
                    {
                        ((Action)(() =>
                        {
                            Console.WriteLine("nested");
                        }))();
                    }
                }
            }
            """,
            source);
    }

    // The whole point of the IEFE form: a local that the selection reads and writes needs no
    // parameter and no return value, because the lambda captures it by reference.
    [Fact]
    public async Task Captures_a_local_that_the_selection_updates_and_later_code_reads()
    {
        var (source, _) = await ExtractIefeAsync(
            """
            using System;

            class Program
            {
                static void Main()
                {
                    var total = 0;
                    [|total += 1;
                    total *= 2;|]
                    Console.WriteLine(total);
                }
            }
            """);

        AssertSource(
            """
            using System;

            class Program
            {
                static void Main()
                {
                    var total = 0;
                    ((Action)(() =>
                    {
                        total += 1;
                        total *= 2;
                    }))();
                    Console.WriteLine(total);
                }
            }
            """,
            source);
    }

    // A local declared and used entirely within the selection just moves inside the lambda.
    [Fact]
    public async Task Moves_a_local_used_only_inside_the_selection_into_the_lambda()
    {
        var (source, _) = await ExtractIefeAsync(
            """
            using System;

            class Program
            {
                static void Main()
                {
                    [|var doubled = 21 * 2;
                    Console.WriteLine(doubled);|]
                }
            }
            """);

        AssertSource(
            """
            using System;

            class Program
            {
                static void Main()
                {
                    ((Action)(() =>
                    {
                        var doubled = 21 * 2;
                        Console.WriteLine(doubled);
                    }))();
                }
            }
            """,
            source);
    }

    [Fact]
    public async Task Captures_this_when_the_selection_uses_instance_state()
    {
        var (source, _) = await ExtractIefeAsync(
            """
            using System;

            class Counter
            {
                int count;

                public void Bump()
                {
                    [|count++;
                    Console.WriteLine(count);|]
                }
            }
            """);

        AssertSource(
            """
            using System;

            class Counter
            {
                int count;

                public void Bump()
                {
                    ((Action)(() =>
                    {
                        count++;
                        Console.WriteLine(count);
                    }))();
                }
            }
            """,
            source);
    }

    // An Action cannot be awaited, so an awaiting selection becomes an async Func<Task> that the
    // caller awaits - still immediately executed, and still behavior-preserving.
    [Fact]
    public async Task Uses_an_awaited_Func_of_Task_when_the_selection_awaits()
    {
        var (source, _) = await ExtractIefeAsync(
            """
            using System;
            using System.Threading.Tasks;

            class Program
            {
                static async Task Main()
                {
                    [|await Task.Delay(1);
                    Console.WriteLine("done");|]
                }
            }
            """);

        AssertSource(
            """
            using System;
            using System.Threading.Tasks;

            class Program
            {
                static async Task Main()
                {
                    await ((Func<Task>)(async () =>
                    {
                        await Task.Delay(1);
                        Console.WriteLine("done");
                    }))();
                }
            }
            """,
            source);
    }

    // The declaration would move inside the lambda, leaving the later use unresolved (CS0103).
    [Fact]
    public async Task Refuses_when_a_local_declared_in_the_selection_is_used_after_it()
    {
        var result = await RefuseExtractIefeAsync(
            """
            using System;

            class Program
            {
                static void Main()
                {
                    [|var doubled = 21 * 2;|]
                    Console.WriteLine(doubled);
                }
            }
            """);

        Assert.Contains("doubled", result.StdErr);
        Assert.Contains("declared in the selection", result.StdErr);
    }

    // Assigning a captured local inside a lambda does not make it definitely assigned afterwards, so
    // the read below would stop compiling (CS0165) even though the code still runs in the same order.
    [Fact]
    public async Task Refuses_when_the_selection_is_the_first_assignment_to_a_local_read_after_it()
    {
        var result = await RefuseExtractIefeAsync(
            """
            using System;

            class Program
            {
                static void Main()
                {
                    int total;
                    [|total = 21 * 2;|]
                    Console.WriteLine(total);
                }
            }
            """);

        Assert.Contains("total", result.StdErr);
        Assert.Contains("definitely assigned", result.StdErr);
    }

    // A return inside the lambda would return from the lambda, not from Main.
    [Fact]
    public async Task Refuses_when_the_selection_contains_a_return_statement()
    {
        var result = await RefuseExtractIefeAsync(
            """
            using System;

            class Program
            {
                static int Main(bool flag)
                {
                    [|if (flag)
                    {
                        return 1;
                    }|]

                    return 0;
                }
            }
            """);

        Assert.Contains("'return'", result.StdErr);
    }

    [Theory]
    [InlineData("break;")]
    [InlineData("continue;")]
    public async Task Refuses_when_the_selection_jumps_out_of_a_loop_outside_it(string jump)
    {
        var result = await RefuseExtractIefeAsync(
            $$"""
            using System;

            class Program
            {
                static void Main()
                {
                    for (var i = 0; i < 10; i++)
                    {
                        [|if (i > 3)
                        {
                            {{jump}}
                        }

                        Console.WriteLine(i);|]
                    }
                }
            }
            """);

        Assert.Contains("outside the selection", result.StdErr);
    }

    // yield is not allowed in a lambda at all (CS1621).
    [Fact]
    public async Task Refuses_when_the_selection_contains_a_yield_statement()
    {
        var result = await RefuseExtractIefeAsync(
            """
            using System.Collections.Generic;

            class Program
            {
                static IEnumerable<int> Numbers()
                {
                    [|yield return 1;
                    yield return 2;|]
                }
            }
            """);

        Assert.Contains("'yield'", result.StdErr);
    }

    // ref/out/in parameters and ref locals cannot be captured by a lambda (CS1628).
    [Theory]
    [InlineData("ref")]
    [InlineData("out")]
    public async Task Refuses_when_the_selection_uses_a_by_reference_parameter(string modifier)
    {
        var result = await RefuseExtractIefeAsync(
            $$"""
            class Program
            {
                static void SetToOne({{modifier}} int value)
                {
                    [|value = 1;|]
                }
            }
            """);

        Assert.Contains("value", result.StdErr);
        Assert.Contains("cannot be captured", result.StdErr);
    }

    [Fact]
    public async Task Refuses_when_the_selection_is_not_whole_statements()
    {
        var result = await RefuseExtractIefeAsync(
            """
            using System;

            class Program
            {
                static void Main()
                {
                    var greeting = "hello";
                    Console.WriteLine([|greeting.Length|]);
                }
            }
            """);

        Assert.Contains("whole statements", result.StdErr);
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

    static async Task<(string Source, ProcessResult Result)> ExtractIefeAsync(string markedSource)
    {
        var (project, _, result) = await RunExtractIefeAsync(markedSource);

        Assert.Equal(0, result.ExitCode);

        return (await File.ReadAllTextAsync(project.ProgramFilePath), result);
    }

    static async Task<ProcessResult> RefuseExtractIefeAsync(string markedSource)
    {
        var (project, markup, result) = await RunExtractIefeAsync(markedSource);

        Assert.Equal(1, result.ExitCode);
        // A refusal must leave the file alone, not half-apply the change.
        AssertSource(markup.Source, await File.ReadAllTextAsync(project.ProgramFilePath));

        return result;
    }

    static async Task<(ScratchProject Project, SelectionMarkup Markup, ProcessResult Result)> RunExtractIefeAsync(string markedSource)
    {
        var markup = SelectionMarkup.Parse(markedSource);
        var project = ProcessTestHost.CreateProjectWithSource(markup.Source);

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "extract-iefe",
            "--project", project.ProjectPath,
            "--file", project.ProgramFilePath,
            "--start-line", markup.StartLine.ToString(),
            "--start-column", markup.StartColumn.ToString(),
            "--end-line", markup.EndLine.ToString(),
            "--end-column", markup.EndColumn.ToString());

        return (project, markup, result);
    }

    static void AssertSource(string expected, string actual) =>
        Assert.Equal(Normalize(expected), Normalize(actual));

    // Line endings and a byte-order mark are Roslyn's business, not the specification's.
    static string Normalize(string source) =>
        source.TrimStart('\uFEFF').ReplaceLineEndings("\n").TrimEnd() + "\n";
}
