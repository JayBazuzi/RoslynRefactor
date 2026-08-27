using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

static class CommandSupport
{
    public static readonly CommandParameter ProjectParameter =
        new("project", "Path to a .sln or .csproj file");

    public static CommandParameter FileParameter(string description) =>
        new("file", description);

    public static readonly IReadOnlyList<CommandParameter> SpanParameters =
    [
        new("start-line", "1-based start line of the selection", ValueType: typeof(int)),
        new("start-column", "1-based start column of the selection", ValueType: typeof(int)),
        new("end-line", "1-based end line of the selection", ValueType: typeof(int)),
        new("end-column", "1-based end column of the selection", ValueType: typeof(int)),
    ];

    public static TextSpan? ToSpan(SourceText text, int startLine, int startColumn, int endLine, int endColumn)
    {
        var start = new LinePosition(startLine - 1, startColumn - 1);
        var end = new LinePosition(endLine - 1, endColumn - 1);
        if (start.Line < 0 || start.Line >= text.Lines.Count || end.Line < 0 || end.Line >= text.Lines.Count)
        {
            return null;
        }

        var startPos = text.Lines[start.Line].Start + start.Character;
        var endPos = text.Lines[end.Line].Start + end.Character;
        if (endPos < startPos)
        {
            return null;
        }

        return TextSpan.FromBounds(startPos, endPos);
    }

    public static Document? FindDocument(Solution solution, string fullFilePath) =>
        solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(Path.GetFullPath(d.FilePath ?? ""), fullFilePath, StringComparison.OrdinalIgnoreCase));

    // Some Roslyn CodeRefactoringProvider implementations are internal to their assembly, so they must be
    // located and instantiated via reflection. Everything else (CodeRefactoringProvider, CodeRefactoringContext,
    // CodeAction, CodeActionOperation) is public API.
    public static CodeRefactoringProvider LoadInternalProvider(string typeName)
    {
        var assemblyName = typeName.StartsWith("Microsoft.CodeAnalysis.CSharp.", StringComparison.Ordinal)
            ? "Microsoft.CodeAnalysis.CSharp.Features"
            : "Microsoft.CodeAnalysis.Features";
        var assembly = Assembly.Load(assemblyName);
        var providerType = assembly.GetType(typeName)
            ?? throw new InvalidOperationException($"Could not locate Roslyn's {typeName}. This tool depends on a Roslyn-internal type that may have moved or been renamed in this Roslyn version.");
        return (CodeRefactoringProvider)Activator.CreateInstance(providerType)!;
    }

    public static CodeAction? FindByEquivalenceKey(IEnumerable<CodeAction> actions, string equivalenceKey)
    {
        foreach (var action in actions)
        {
            if (string.Equals(action.EquivalenceKey, equivalenceKey, StringComparison.Ordinal))
            {
                return action;
            }

            var nested = FindByEquivalenceKey(action.NestedActions, equivalenceKey);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    public static IEnumerable<CodeAction> CollectLeaves(IEnumerable<CodeAction> actions)
    {
        return actions.SelectMany(action =>
            action.NestedActions.Length == 0
                ? [action]
                : CollectLeaves(action.NestedActions));
    }

    public static CodeAction SelectSingle(IReadOnlyList<CodeAction> candidates, string noneMessage, string multipleMessage)
    {
        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(noneMessage);
        }
        if (candidates.Count > 1)
        {
            throw new InvalidOperationException(multipleMessage);
        }
        return candidates[0];
    }

    // Shared pipeline for the span-based refactoring commands: parse arguments, open the workspace, resolve the
    // selection to a span, run the provider, let the caller pick the CodeAction to apply, then apply it.
    public static async Task<int> RunSpanRefactoringAsync(
        IReadOnlyDictionary<string, string> arguments,
        CodeRefactoringProvider provider,
        Func<IReadOnlyList<CodeAction>, CodeAction> selectAction,
        string progressMessage,
        string noChangesMessage,
        TextWriter output,
        CancellationToken cancellationToken)
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
        var document = FindDocument(solution, fullFilePath);
        if (document is null)
        {
            throw new InvalidOperationException($"file not found in workspace: {fullFilePath}");
        }

        var text = await document.GetTextAsync(cancellationToken);
        var span = ToSpan(text, startLine, startColumn, endLine, endColumn);
        if (span is null)
        {
            throw new InvalidOperationException($"selection is out of range for {fullFilePath}");
        }

        var actions = new List<CodeAction>();
        var context = new CodeRefactoringContext(document, span.Value, actions.Add, cancellationToken);
        await provider.ComputeRefactoringsAsync(context);

        var selectedAction = selectAction(actions);

        var operations = await selectedAction.GetOperationsAsync(cancellationToken);
        var applyOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOperation is null)
        {
            throw new InvalidOperationException(noChangesMessage);
        }

        var newSolution = applyOperation.ChangedSolution;

        output.WriteLine($"{progressMessage} ({fullFilePath})");

        if (!workspace.TryApplyChanges(newSolution))
        {
            throw new InvalidOperationException("workspace rejected the changes");
        }

        output.WriteLine($"updated: {fullFilePath}");
        return 0;
    }
}
