namespace RoslynRefactor.Tests;

// Spec-first tests for `convert-lambda-captures-to-parameters`, a refactoring RoslynRefactor implements
// itself rather than delegating to a Roslyn-internal provider (unlike the other commands), because no
// such provider exists upstream. These tests pin the intended behavior before the command is implemented
// (ConvertLambdaCapturesToParametersCommand.RunAsync currently just reports "not implemented"), so they
// are expected to fail (red) until that logic is written.
//
// Design decisions baked into the expected output below:
//  - Every free local/parameter the lambda reads from an enclosing scope becomes a trailing parameter,
//    in the order it is first referenced in the lambda body, appended after any existing parameters.
//  - The lambda's delegate-typed local is widened to match (Func<int,bool> -> Func<int,int,bool>, an
//    inferred `var` picks up the new arity automatically). Existing parameters keep an explicit type only
//    if the lambda already used explicit types (a lambda parameter list can't mix implicit and explicit).
//  - A new parameter cannot reuse the captured variable's own name: that variable's declaration is still
//    in scope at the lambda (that's what "captured" means), and C# forbids a nested parameter shadowing
//    an enclosing local (CS0136). The new parameter gets a "1" suffix, matching Roslyn's own convention
//    for generating a unique name from a preferred one, and each call site passes the original variable.
//  - Every invocation of the lambda's local within the same method is updated to pass the captured
//    variable as a trailing argument - including one that runs after the captured variable was mutated,
//    since passing the variable itself (not a value snapshot) reproduces the original closure semantics.
//  - The refactoring is unavailable - no file changes, non-zero exit - whenever it can't guarantee every
//    invocation gets updated: when the lambda has no captures to convert, when its delegate type is fixed
//    by the API it's passed to (e.g. LINQ's Select), or when it escapes its declaring method (e.g. is
//    returned) so its call sites aren't all known.
public class ConvertLambdaCapturesToParametersTests
{
    [Fact]
    public async Task Converts_a_single_capture_into_a_trailing_parameter_and_updates_the_call_sites()
    {
        var sample = ProcessTestHost.CreateSampleCopy();
        var filePath = sample.FilePath("LambdaCaptures.cs");

        await ProcessTestHost.RunAsync(
            "convert-lambda-captures-to-parameters",
            "--project", sample.SolutionPath,
            "--file", filePath,
            "--start-line", "12", "--start-column", "44",
            "--end-line", "12", "--end-column", "62");

        var content = await File.ReadAllTextAsync(filePath);
        Approvals.Verify(content);
    }

    [Fact]
    public async Task Converts_multiple_captures_in_the_order_they_are_first_referenced()
    {
        var sample = ProcessTestHost.CreateSampleCopy();
        var filePath = sample.FilePath("LambdaCaptures.cs");

        await ProcessTestHost.RunAsync(
            "convert-lambda-captures-to-parameters",
            "--project", sample.SolutionPath,
            "--file", filePath,
            "--start-line", "21", "--start-column", "25",
            "--end-line", "21", "--end-column", "56");

        var content = await File.ReadAllTextAsync(filePath);
        Approvals.Verify(content);
    }

    [Fact]
    public async Task Updates_every_call_site_of_the_lambdas_local()
    {
        var sample = ProcessTestHost.CreateSampleCopy();
        var filePath = sample.FilePath("LambdaCaptures.cs");

        await ProcessTestHost.RunAsync(
            "convert-lambda-captures-to-parameters",
            "--project", sample.SolutionPath,
            "--file", filePath,
            "--start-line", "28", "--start-column", "25",
            "--end-line", "28", "--end-column", "46");

        var content = await File.ReadAllTextAsync(filePath);
        Approvals.Verify(content);
    }

    [Fact]
    public async Task Passes_the_captured_variable_itself_so_mutation_before_a_call_is_still_observed()
    {
        var sample = ProcessTestHost.CreateSampleCopy();
        var filePath = sample.FilePath("LambdaCaptures.cs");

        await ProcessTestHost.RunAsync(
            "convert-lambda-captures-to-parameters",
            "--project", sample.SolutionPath,
            "--file", filePath,
            "--start-line", "43", "--start-column", "25",
            "--end-line", "43", "--end-column", "57");

        var content = await File.ReadAllTextAsync(filePath);
        Approvals.Verify(content);
    }

    [Fact]
    public async Task Converts_a_capture_used_inside_a_statement_bodied_lambda()
    {
        var sample = ProcessTestHost.CreateSampleCopy();
        var filePath = sample.FilePath("LambdaCaptures.cs");

        await ProcessTestHost.RunAsync(
            "convert-lambda-captures-to-parameters",
            "--project", sample.SolutionPath,
            "--file", filePath,
            "--start-line", "51", "--start-column", "39",
            "--end-line", "57", "--end-column", "10");

        var content = await File.ReadAllTextAsync(filePath);
        Approvals.Verify(content);
    }

    [Fact]
    public async Task Is_unavailable_when_the_lambda_has_no_captures()
    {
        var sample = ProcessTestHost.CreateSampleCopy();
        var filePath = sample.FilePath("LambdaCaptures.cs");
        var originalContent = await File.ReadAllTextAsync(filePath);

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "convert-lambda-captures-to-parameters",
            "--project", sample.SolutionPath,
            "--file", filePath,
            "--start-line", "36", "--start-column", "33",
            "--end-line", "36", "--end-column", "43");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error:", result.StdErr);
        Assert.Equal(originalContent, await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task Is_unavailable_when_the_lambdas_delegate_type_is_fixed_by_its_usage()
    {
        // `numbers.Select(n => n * multiplier)` requires a Func<int,int>; widening it to
        // Func<int,int,int> would no longer match any Enumerable.Select overload.
        var sample = ProcessTestHost.CreateSampleCopy();
        var filePath = sample.FilePath("LambdaCaptures.cs");
        var originalContent = await File.ReadAllTextAsync(filePath);

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "convert-lambda-captures-to-parameters",
            "--project", sample.SolutionPath,
            "--file", filePath,
            "--start-line", "65", "--start-column", "31",
            "--end-line", "65", "--end-column", "50");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error:", result.StdErr);
        Assert.Equal(originalContent, await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task Is_unavailable_when_the_lambda_escapes_its_declaring_method()
    {
        // Returned from the method, so every caller of LambdaEscapesDeclaringScope is a potential
        // invocation site; they aren't all statically resolvable here, so nothing can be updated safely.
        var sample = ProcessTestHost.CreateSampleCopy();
        var filePath = sample.FilePath("LambdaCaptures.cs");
        var originalContent = await File.ReadAllTextAsync(filePath);

        var result = await ProcessTestHost.RunAllowingFailureAsync(
            "convert-lambda-captures-to-parameters",
            "--project", sample.SolutionPath,
            "--file", filePath,
            "--start-line", "71", "--start-column", "16",
            "--end-line", "71", "--end-column", "34");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("error:", result.StdErr);
        Assert.Equal(originalContent, await File.ReadAllTextAsync(filePath));
    }
}
