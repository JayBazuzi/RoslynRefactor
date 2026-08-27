using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

sealed class InlineTemporaryVariableCommand : ICommand
{
    static readonly Lazy<CodeRefactoringProvider> Provider = new(() =>
        CommandSupport.LoadInternalProvider(
            "Microsoft.CodeAnalysis.CSharp.CodeRefactorings.InlineTemporary.CSharpInlineTemporaryCodeRefactoringProvider"));

    public static Command Build()
    {
        var project = CommandSupport.ProjectOption();
        var file = CommandSupport.FileOption("Path to the file containing the selection");
        var span = new CommandSupport.SpanOptions();

        var command = new Command("inline-temporary-variable", "Inline a local variable's initializer into all usages, then remove the declaration")
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

    static async Task<int> RunAsync(string projectPath, string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken cancellationToken)
    {
        var (workspace, solution) = await WorkspaceLoader.OpenAsync(projectPath);
        using var _ = workspace;

        var fullFilePath = Path.GetFullPath(filePath);
        var document = CommandSupport.FindDocument(solution, fullFilePath);

        if (document is null)
        {
            Console.Error.WriteLine($"error: file not found in workspace: {fullFilePath}");
            return 1;
        }

        var text = await document.GetTextAsync(cancellationToken);
        var span = CommandSupport.ToSpan(text, startLine, startColumn, endLine, endColumn);
        if (span is null)
        {
            Console.Error.WriteLine($"error: selection is out of range for {fullFilePath}");
            return 1;
        }

        var actions = new List<CodeAction>();
        var context = new CodeRefactoringContext(document, span.Value, actions.Add, cancellationToken);
        await Provider.Value.ComputeRefactoringsAsync(context);

        var leaves = new List<CodeAction>();
        CommandSupport.CollectLeaves(actions, leaves);

        if (leaves.Count == 0)
        {
            Console.Error.WriteLine("error: no Inline Temporary Variable refactoring is available for this selection.");
            return 1;
        }
        if (leaves.Count > 1)
        {
            Console.Error.WriteLine("error: multiple matching Inline Temporary Variable refactorings were found; this is a bug in RoslynRefactor.");
            return 1;
        }

        var inlineAction = leaves[0];
        var operations = await inlineAction.GetOperationsAsync(cancellationToken);
        var applyOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOperation is null)
        {
            Console.Error.WriteLine("error: Roslyn's Inline Temporary Variable refactoring produced no changes.");
            return 1;
        }

        var newSolution = applyOperation.ChangedSolution;

        Console.WriteLine($"Inlining temporary variable ({fullFilePath})");

        if (!workspace.TryApplyChanges(newSolution))
        {
            Console.Error.WriteLine("error: workspace rejected the changes");
            return 1;
        }

        Console.WriteLine($"updated: {fullFilePath}");
        return 0;
    }
}
