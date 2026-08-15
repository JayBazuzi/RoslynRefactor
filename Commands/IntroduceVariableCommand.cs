using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

static class IntroduceVariableCommand
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

    public static async Task<int> RunAsync(string[] args)
    {
        string? projectPath = null;
        string? filePath = null;
        int? startLine = null;
        int? startColumn = null;
        int? endLine = null;
        int? endColumn = null;
        var kind = "local";
        var allOccurrences = false;
        string? newName = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project": projectPath = args[++i]; break;
                case "--file": filePath = args[++i]; break;
                case "--start-line": startLine = int.Parse(args[++i]); break;
                case "--start-column": startColumn = int.Parse(args[++i]); break;
                case "--end-line": endLine = int.Parse(args[++i]); break;
                case "--end-column": endColumn = int.Parse(args[++i]); break;
                case "--kind": kind = args[++i]; break;
                case "--all-occurrences": allOccurrences = true; break;
                case "--name": newName = args[++i]; break;
                case "-h" or "--help": PrintHelp(); return 0;
                default: Console.Error.WriteLine($"error: unknown option '{args[i]}'"); return 1;
            }
        }

        if (projectPath is null || filePath is null || startLine is null || startColumn is null
            || endLine is null || endColumn is null)
        {
            Console.Error.WriteLine("error: --project, --file, --start-line, --start-column, --end-line, and --end-column are required");
            PrintHelp();
            return 1;
        }

        if (!ValidKinds.Contains(kind))
        {
            Console.Error.WriteLine($"error: --kind must be one of: {string.Join(", ", ValidKinds)}");
            return 1;
        }

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

        var text = await document.GetTextAsync();
        var span = ToSpan(text, startLine.Value, startColumn.Value, endLine.Value, endColumn.Value);
        if (span is null)
        {
            Console.Error.WriteLine($"error: selection is out of range for {fullFilePath}");
            return 1;
        }

        var originalRoot = await document.GetSyntaxRootAsync();
        var existingVariableNames = originalRoot?.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Select(v => v.Identifier.Text).ToHashSet() ?? [];

        var actions = new List<CodeAction>();
        var context = new CodeRefactoringContext(document, span.Value, actions.Add, CancellationToken.None);
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
        var operations = await introduceAction.GetOperationsAsync(CancellationToken.None);
        var applyOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOperation is null)
        {
            Console.Error.WriteLine("error: Roslyn's Introduce Variable refactoring produced no changes.");
            return 1;
        }

        var newSolution = applyOperation.ChangedSolution;
        var newDocument = newSolution.GetDocument(document.Id)!;
        var newRoot = await newDocument.GetSyntaxRootAsync();

        var newDeclarators = newRoot?.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(v => !existingVariableNames.Contains(v.Identifier.Text))
            .ToList();
        if (newDeclarators is null || newDeclarators.Count != 1)
        {
            Console.Error.WriteLine($"error: could not uniquely identify the introduced variable ({newDeclarators?.Count ?? 0} candidates found).");
            return 1;
        }

        var semanticModel = await newDocument.GetSemanticModelAsync();
        var introducedSymbol = semanticModel?.GetDeclaredSymbol(newDeclarators[0]);
        if (introducedSymbol is null)
        {
            Console.Error.WriteLine("error: could not resolve the symbol for the introduced variable.");
            return 1;
        }

        if (newName is not null && newName != introducedSymbol.Name)
        {
            var renameOptions = new SymbolRenameOptions();
            newSolution = await Renamer.RenameSymbolAsync(newSolution, introducedSymbol, renameOptions, newName);
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

    static void PrintHelp()
    {
        Console.WriteLine("""
            Usage: RoslynRefactor introduce-variable --project <sln|csproj> --file <path> --start-line <n> --start-column <n> --end-line <n> --end-column <n> [--kind local|local-constant|constant|field|query-variable] [--all-occurrences] [--name <newVariableName>]

            Introduces a new variable for the expression covered by the given 1-based
            selection using Roslyn's own Introduce Variable refactoring, replacing the
            expression (and any other matched occurrences) with a reference to it.

            --kind defaults to "local". --all-occurrences replaces every matching
            occurrence in scope, not just the selected one. If --name is given, renames
            the generated variable to the requested name; otherwise the name Roslyn
            generates is kept.
            """);
    }
}
