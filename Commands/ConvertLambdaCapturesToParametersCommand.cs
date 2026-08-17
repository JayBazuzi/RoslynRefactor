using System.CommandLine;

namespace RoslynRefactor;

sealed class ConvertLambdaCapturesToParametersCommand : ICommand
{
    public static Command Build()
    {
        var project = CommandSupport.ProjectOption();
        var file = CommandSupport.FileOption("Path to the file containing the selection");
        var span = new CommandSupport.SpanOptions();

        var command = new Command("convert-lambda-captures-to-parameters", "Convert all variables captured by a lambda into parameters")
        {
            project, file, span.StartLine, span.StartColumn, span.EndLine, span.EndColumn,
        };

        command.SetAction(async (parseResult, cancellationToken) => await RunAsync(
            parseResult.GetValue(project)!,
            parseResult.GetValue(file)!,
            parseResult.GetValue(span.StartLine),
            parseResult.GetValue(span.StartColumn),
            parseResult.GetValue(span.EndLine),
            parseResult.GetValue(span.EndColumn),
            cancellationToken));

        return command;
    }

    static Task<int> RunAsync(string projectPath, string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken cancellationToken)
    {
        // Not yet implemented. See Tests/ConvertLambdaCapturesToParametersTests.cs for the intended
        // behavior: every free variable a lambda captures from an enclosing scope becomes a trailing
        // parameter, the lambda's delegate-typed local is widened to match, and every statically
        // resolvable invocation of that local is updated to pass the captured variable along.
        Console.Error.WriteLine("error: convert-lambda-captures-to-parameters is not implemented yet");
        return Task.FromResult(1);
    }
}
