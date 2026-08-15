using System.CommandLine;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

sealed class IntroduceVariableCommand : ICommand
{
    // Microsoft.CodeAnalysis.IntroduceVariable.IntroduceVariableCodeRefactoringProvider is internal to
    // Microsoft.CodeAnalysis.Features, so it must be located and instantiated via reflection. Everything else
    // (CodeRefactoringProvider, CodeRefactoringContext, CodeAction, CodeActionOperation) is public API.
    static readonly Lazy<CodeRefactoringProvider> Provider = new(() =>
    {
        var features = Assembly.Load("Microsoft.CodeAnalysis.Features");
        var providerType = features.GetType("Microsoft.CodeAnalysis.IntroduceVariable.IntroduceVariableCodeRefactoringProvider")
            ?? throw new InvalidOperationException("Could not locate Roslyn's IntroduceVariableCodeRefactoringProvider. This tool depends on a Roslyn-internal type that may have moved or been renamed in this Roslyn version.");
        return (CodeRefactoringProvider)Activator.CreateInstance(providerType)!;
    });

    public static Command Build()
    {
        var project = CommandSupport.ProjectOption();
        var file = CommandSupport.FileOption("Path to the file containing the selection");
        var span = new CommandSupport.SpanOptions();

        var command = new Command("introduce-variable", "Introduce a local variable for a selected expression")
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
        CollectLeaves(actions, leaves);

        var candidates = leaves.Where(a => MatchesKindAndScope(a.Title)).ToList();
        if (candidates.Count == 0)
        {
            Console.Error.WriteLine("error: no 'local' (single occurrence) Introduce Variable refactoring is available for this selection.");
            return 1;
        }
        if (candidates.Count > 1)
        {
            Console.Error.WriteLine("error: multiple matching Introduce Variable refactorings were found; this is a bug in RoslynRefactor.");
            return 1;
        }

        var introduceAction = candidates[0];
        var operations = await introduceAction.GetOperationsAsync(cancellationToken);
        var applyOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOperation is null)
        {
            Console.Error.WriteLine("error: Roslyn's Introduce Variable refactoring produced no changes.");
            return 1;
        }

        var newSolution = applyOperation.ChangedSolution;

        Console.WriteLine($"Introducing variable ({fullFilePath})");

        if (!workspace.TryApplyChanges(newSolution))
        {
            Console.Error.WriteLine("error: workspace rejected the changes");
            return 1;
        }

        Console.WriteLine($"updated: {fullFilePath}");
        return 0;
    }

    static void CollectLeaves(IEnumerable<CodeAction> actions, List<CodeAction> leaves)
    {
        foreach (var action in actions)
        {
            var nested = action.NestedActions;
            if (nested.Length == 0)
            {
                leaves.Add(action);
            }
            else
            {
                CollectLeaves(nested, leaves);
            }
        }
    }

    static bool MatchesKindAndScope(string title)
    {
        // "Introduce local for" is also a prefix of "Introduce local constant for", so exclude that case explicitly.
        return title.StartsWith("Introduce local for", StringComparison.Ordinal)
            && !title.StartsWith("Introduce local constant for", StringComparison.Ordinal)
            && !title.Contains("all occurrences of", StringComparison.Ordinal);
    }
}
