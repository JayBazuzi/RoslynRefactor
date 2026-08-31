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

    public static TextSpan? ToTextSpan(SourceText text, LineAndColumnSpan span)
    {
        var startPosition = new LinePosition(span.start.line - 1, span.start.column - 1);
        var endPosition = new LinePosition(span.end.line - 1, span.end.column - 1);
        if (startPosition.Line < 0 || startPosition.Line >= text.Lines.Count || endPosition.Line < 0 || endPosition.Line >= text.Lines.Count)
        {
            return null;
        }

        var startPos = text.Lines[startPosition.Line].Start + startPosition.Character;
        var endPos = text.Lines[endPosition.Line].Start + endPosition.Character;
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
        var match = actions.FirstOrDefault(action => string.Equals(action.EquivalenceKey, equivalenceKey, StringComparison.Ordinal));
        if (match is not null)
        {
            return match;
        }

        return actions
            .Select(action => FindByEquivalenceKey(action.NestedActions, equivalenceKey))
            .FirstOrDefault(nested => nested is not null);
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
        var span = new LineAndColumnSpan(
            new LineAndColumn(int.Parse(arguments["start-line"]), int.Parse(arguments["start-column"])),
            new LineAndColumn(int.Parse(arguments["end-line"]), int.Parse(arguments["end-column"])));

        var (workspace, solution) = await WorkspaceLoader.OpenAsync(projectPath);
        using var _ = workspace;

        var fullFilePath = Path.GetFullPath(filePath);
        var document = FindDocument(solution, fullFilePath);
        if (document is null)
        {
            throw new InvalidOperationException($"file not found in workspace: {fullFilePath}");
        }

        var text = await document.GetTextAsync(cancellationToken);
        var textSpan = ToTextSpan(text, span);
        if (textSpan is null)
        {
            throw new InvalidOperationException($"selection is out of range for {fullFilePath}");
        }

        var actions = new List<CodeAction>();
        var context = new CodeRefactoringContext(document, textSpan.Value, actions.Add, cancellationToken);
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
