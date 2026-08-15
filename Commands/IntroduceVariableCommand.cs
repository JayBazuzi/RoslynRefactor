using System.CommandLine;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
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

    static readonly string[] ValidKinds = ["local", "local-constant", "constant", "field", "query-variable"];

    public static Command Build()
    {
        var project = new Option<string>("--project") { Required = true, Description = "Path to a .sln or .csproj file" };
        var file = new Option<string>("--file") { Required = true, Description = "Path to the file containing the selection" };
        var startLine = new Option<int>("--start-line") { Required = true, Description = "1-based start line of the selection" };
        var startColumn = new Option<int>("--start-column") { Required = true, Description = "1-based start column of the selection" };
        var endLine = new Option<int>("--end-line") { Required = true, Description = "1-based end line of the selection" };
        var endColumn = new Option<int>("--end-column") { Required = true, Description = "1-based end column of the selection" };
        var kind = new Option<string>("--kind") { DefaultValueFactory = _ => "local", Description = "Kind of variable to introduce" };
        kind.AcceptOnlyFromAmong(ValidKinds);
        var allOccurrences = new Option<bool>("--all-occurrences") { Description = "Replace every matching occurrence in scope, not just the selected one" };
        var name = new Option<string>("--name") { Description = "Rename the generated variable to this name" };

        var command = new Command("introduce-variable", "Introduce a local variable for a selected expression")
        {
            project, file, startLine, startColumn, endLine, endColumn, kind, allOccurrences, name,
        };

        command.SetAction(async (parseResult, cancellationToken) => await RunAsync(
            parseResult.GetValue(project)!,
            parseResult.GetValue(file)!,
            parseResult.GetValue(startLine),
            parseResult.GetValue(startColumn),
            parseResult.GetValue(endLine),
            parseResult.GetValue(endColumn),
            parseResult.GetValue(kind)!,
            parseResult.GetValue(allOccurrences),
            parseResult.GetValue(name),
            cancellationToken));

        return command;
    }

    static async Task<int> RunAsync(string projectPath, string filePath, int startLine, int startColumn, int endLine, int endColumn, string kind, bool allOccurrences, string? newName, CancellationToken cancellationToken)
    {
        var (workspace, solution) = await WorkspaceLoader.OpenAsync(projectPath);
        using var _ = workspace;

        var fullFilePath = Path.GetFullPath(filePath);
        var document = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(Path.GetFullPath(d.FilePath ?? ""), fullFilePath, StringComparison.OrdinalIgnoreCase));

        if (document is null)
        {
            Console.Error.WriteLine($"error: file not found in workspace: {fullFilePath}");
            return 1;
        }

        var text = await document.GetTextAsync(cancellationToken);
        var span = ToSpan(text, startLine, startColumn, endLine, endColumn);
        if (span is null)
        {
            Console.Error.WriteLine($"error: selection is out of range for {fullFilePath}");
            return 1;
        }

        var originalRoot = await document.GetSyntaxRootAsync(cancellationToken);
        var existingVariableNames = originalRoot?.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Select(v => v.Identifier.Text).ToHashSet() ?? [];

        var actions = new List<CodeAction>();
        var context = new CodeRefactoringContext(document, span.Value, actions.Add, cancellationToken);
        await Provider.Value.ComputeRefactoringsAsync(context);

        var leaves = new List<CodeAction>();
        CollectLeaves(actions, leaves);

        var candidates = leaves.Where(a => MatchesKindAndScope(a.Title, kind, allOccurrences)).ToList();
        if (candidates.Count == 0)
        {
            Console.Error.WriteLine($"error: no '{kind}' ({(allOccurrences ? "all occurrences" : "single occurrence")}) Introduce Variable refactoring is available for this selection.");
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
        var newDocument = newSolution.GetDocument(document.Id)!;
        var newRoot = await newDocument.GetSyntaxRootAsync(cancellationToken);

        var newDeclarators = newRoot?.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(v => !existingVariableNames.Contains(v.Identifier.Text))
            .ToList();
        if (newDeclarators is null || newDeclarators.Count != 1)
        {
            Console.Error.WriteLine($"error: could not uniquely identify the introduced variable ({newDeclarators?.Count ?? 0} candidates found).");
            return 1;
        }

        var semanticModel = await newDocument.GetSemanticModelAsync(cancellationToken);
        var introducedSymbol = semanticModel?.GetDeclaredSymbol(newDeclarators[0], cancellationToken);
        if (introducedSymbol is null)
        {
            Console.Error.WriteLine("error: could not resolve the symbol for the introduced variable.");
            return 1;
        }

        if (newName is not null && newName != introducedSymbol.Name)
        {
            var renameOptions = new SymbolRenameOptions();
            newSolution = await Renamer.RenameSymbolAsync(newSolution, introducedSymbol, renameOptions, newName, cancellationToken);
            Console.WriteLine($"Introducing '{introducedSymbol.Name}' ({fullFilePath}), renamed to '{newName}'");
        }
        else
        {
            Console.WriteLine($"Introducing '{introducedSymbol.Name}' ({fullFilePath})");
        }

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

    static bool MatchesKindAndScope(string title, string kind, bool allOccurrences)
    {
        var prefix = kind switch
        {
            "local" => "Introduce local for",
            "local-constant" => "Introduce local constant for",
            "constant" => "Introduce constant for",
            "field" => "Introduce field for",
            "query-variable" => "Introduce query variable for",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        if (!title.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        // "Introduce local for" is also a prefix of "Introduce local constant for", so exclude that case explicitly.
        if (kind == "local" && title.StartsWith("Introduce local constant for", StringComparison.Ordinal))
        {
            return false;
        }

        var isAllOccurrences = title.Contains("all occurrences of", StringComparison.Ordinal);
        return isAllOccurrences == allOccurrences;
    }

    static TextSpan? ToSpan(SourceText text, int startLine, int startColumn, int endLine, int endColumn)
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
}
