using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

sealed class InlineMethodCommand : ICommand
{
    static readonly Lazy<CodeRefactoringProvider> Provider = new(() =>
        CommandSupport.LoadInternalProvider(
            "Microsoft.CodeAnalysis.CSharp.CodeRefactorings.InlineMethod.CSharpInlineMethodRefactoringProvider"));

    public static CommandDescriptor Descriptor { get; } = new(
        "inline-method",
        "Inline a called method's (or local function's) body at the call site",
        [
            CommandSupport.ProjectParameter,
            CommandSupport.FileParameter("Path to the file containing the call site"),
            .. CommandSupport.SpanParameters,
        ],
        RunAsync);

    static async Task<int> RunAsync(IReadOnlyDictionary<string, string> arguments, TextWriter output, CancellationToken cancellationToken)
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

        // Roslyn also offers "Inline and keep 'X'", which inlines the call but leaves the original method
        // declaration in place. We only want the variant that removes the original declaration.
        var candidates = leaves.Where(a => a.Title.StartsWith("Inline '", StringComparison.Ordinal)).ToList();
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("no Inline Method refactoring is available at this location.");
        }
        if (candidates.Count > 1)
        {
            throw new InvalidOperationException("multiple matching Inline Method refactorings were found; this is a bug in RoslynRefactor.");
        }

        var inlineAction = candidates[0];
        var operations = await inlineAction.GetOperationsAsync(cancellationToken);
        var applyOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOperation is null)
        {
            throw new InvalidOperationException("Roslyn's Inline Method refactoring produced no changes.");
        }

        var newSolution = applyOperation.ChangedSolution;

        output.WriteLine($"Inlining method ({fullFilePath})");

        if (!workspace.TryApplyChanges(newSolution))
        {
            throw new InvalidOperationException("workspace rejected the changes");
        }

        output.WriteLine($"updated: {fullFilePath}");
        return 0;
    }
}
