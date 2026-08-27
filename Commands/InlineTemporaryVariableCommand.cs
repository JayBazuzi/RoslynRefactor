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

    public static CommandDescriptor Descriptor { get; } = new(
        "inline-temporary-variable",
        "Inline a local variable's initializer into all usages, then remove the declaration",
        [
            CommandSupport.ProjectParameter,
            CommandSupport.FileParameter("Path to the file containing the selection"),
            .. CommandSupport.SpanParameters,
        ],
        RunAsync);

    static async Task<int> RunAsync(IReadOnlyDictionary<string, string> arguments, CancellationToken cancellationToken)
    {
        var projectPath = arguments["project"];
        var filePath = arguments["file"];
        var startLine = int.Parse(arguments["start-line"]);
        var startColumn = int.Parse(arguments["start-column"]);
        var endLine = int.Parse(arguments["end-line"]);
        var endColumn = int.Parse(arguments["end-column"]);

        var (workspace, solution) = await WorkspaceLoader.OpenAsync(projectPath);
        using var _ = workspace;

        var fullFilePath = Path.GetFullPath(filePath);
        var document = CommandSupport.FindDocument(solution, fullFilePath);

        if (document is null)
        {
            throw new InvalidOperationException($"file not found in workspace: {fullFilePath}");
        }

        var text = await document.GetTextAsync(cancellationToken);
        var span = CommandSupport.ToSpan(text, startLine, startColumn, endLine, endColumn);
        if (span is null)
        {
            throw new InvalidOperationException($"selection is out of range for {fullFilePath}");
        }

        var actions = new List<CodeAction>();
        var context = new CodeRefactoringContext(document, span.Value, actions.Add, cancellationToken);
        await Provider.Value.ComputeRefactoringsAsync(context);

        var leaves = CommandSupport.CollectLeaves(actions).ToList();

        if (leaves.Count == 0)
        {
            throw new InvalidOperationException("no Inline Temporary Variable refactoring is available for this selection.");
        }
        if (leaves.Count > 1)
        {
            throw new InvalidOperationException("multiple matching Inline Temporary Variable refactorings were found; this is a bug in RoslynRefactor.");
        }

        var inlineAction = leaves[0];
        var operations = await inlineAction.GetOperationsAsync(cancellationToken);
        var applyOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOperation is null)
        {
            throw new InvalidOperationException("Roslyn's Inline Temporary Variable refactoring produced no changes.");
        }

        var newSolution = applyOperation.ChangedSolution;

        Console.WriteLine($"Inlining temporary variable ({fullFilePath})");

        if (!workspace.TryApplyChanges(newSolution))
        {
            throw new InvalidOperationException("workspace rejected the changes");
        }

        Console.WriteLine($"updated: {fullFilePath}");
        return 0;
    }
}
