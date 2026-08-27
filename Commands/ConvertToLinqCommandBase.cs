using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

abstract class ConvertToLinqCommandBase
{
    static readonly Lazy<CodeRefactoringProvider> Provider = new(() =>
        CommandSupport.LoadInternalProvider(
            "Microsoft.CodeAnalysis.CSharp.ConvertLinq.ConvertForEachToLinqQuery.CSharpConvertForEachToLinqQueryProvider"));

    protected static CommandDescriptor BuildDescriptor(string name, string description, string equivalenceKey) => new(
        name,
        description,
        [
            CommandSupport.ProjectParameter,
            CommandSupport.FileParameter("Path to the file containing the selection"),
            .. CommandSupport.SpanParameters,
        ],
        (arguments, cancellationToken) => RunAsync(arguments, equivalenceKey, cancellationToken));

    static async Task<int> RunAsync(IReadOnlyDictionary<string, string> arguments, string equivalenceKey, CancellationToken cancellationToken)
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

        var convertAction = CommandSupport.FindByEquivalenceKey(actions, equivalenceKey);
        if (convertAction is null)
        {
            throw new InvalidOperationException("Roslyn's Convert to LINQ refactoring is not available for this selection.");
        }

        var operations = await convertAction.GetOperationsAsync(cancellationToken);
        var applyOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOperation is null)
        {
            throw new InvalidOperationException("Roslyn's Convert to LINQ refactoring produced no changes.");
        }

        var newSolution = applyOperation.ChangedSolution;

        Console.WriteLine($"Converting foreach to LINQ ({fullFilePath})");

        if (!workspace.TryApplyChanges(newSolution))
        {
            throw new InvalidOperationException("workspace rejected the changes");
        }

        Console.WriteLine($"updated: {fullFilePath}");
        return 0;
    }
}
