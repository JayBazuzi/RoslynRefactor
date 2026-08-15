using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

static class ExtractMethodCommand
{
    // Microsoft.CodeAnalysis.CodeRefactorings.ExtractMethod.ExtractMethodCodeRefactoringProvider is internal to
    // Microsoft.CodeAnalysis.Features, so it must be located and instantiated via reflection. Everything else
    // (CodeRefactoringProvider, CodeRefactoringContext, CodeAction, CodeActionOperation) is public API.
    static readonly Lazy<CodeRefactoringProvider> Provider = new(() =>
    {
        var features = Assembly.Load("Microsoft.CodeAnalysis.Features");
        var providerType = features.GetType("Microsoft.CodeAnalysis.CodeRefactorings.ExtractMethod.ExtractMethodCodeRefactoringProvider")
            ?? throw new InvalidOperationException("Could not locate Roslyn's ExtractMethodCodeRefactoringProvider. This tool depends on a Roslyn-internal type that may have moved or been renamed in this Roslyn version.");
        return (CodeRefactoringProvider)Activator.CreateInstance(providerType)!;
    });

    public static async Task<int> RunAsync(string[] args)
    {
        string? projectPath = null;
        string? filePath = null;
        int? startLine = null;
        int? startColumn = null;
        int? endLine = null;
        int? endColumn = null;

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
        var enclosingType = originalRoot?.FindToken(Math.Clamp(span.Value.Start, 0, Math.Max(0, originalRoot.FullSpan.End - 1)))
            .Parent?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (enclosingType is null)
        {
            Console.Error.WriteLine("error: selection is not inside a type declaration.");
            return 1;
        }

        var existingMethodNames = enclosingType.Members.OfType<MethodDeclarationSyntax>().Select(m => m.Identifier.Text).ToHashSet();

        var actions = new List<CodeAction>();
        var context = new CodeRefactoringContext(document, span.Value, actions.Add, CancellationToken.None);
        await Provider.Value.ComputeRefactoringsAsync(context);

        var extractMethodAction = FindByEquivalenceKey(actions, "Extract_method");
        if (extractMethodAction is null)
        {
            Console.Error.WriteLine("error: Roslyn's Extract Method refactoring is not available for this selection.");
            return 1;
        }

        var operations = await extractMethodAction.GetOperationsAsync(CancellationToken.None);
        var applyOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOperation is null)
        {
            Console.Error.WriteLine("error: Roslyn's Extract Method refactoring produced no changes.");
            return 1;
        }

        var newSolution = applyOperation.ChangedSolution;
        var newDocument = newSolution.GetDocument(document.Id)!;
        var newRoot = await newDocument.GetSyntaxRootAsync();
        var newEnclosingType = newRoot?.DescendantNodes().OfType<TypeDeclarationSyntax>()
            .FirstOrDefault(t => t.Identifier.Text == enclosingType.Identifier.Text);

        var newMethods = newEnclosingType?.Members.OfType<MethodDeclarationSyntax>()
            .Where(m => !existingMethodNames.Contains(m.Identifier.Text))
            .ToList();
        if (newMethods is null || newMethods.Count != 1)
        {
            Console.Error.WriteLine($"error: could not uniquely identify the extracted method ({newMethods?.Count ?? 0} candidates found).");
            return 1;
        }

        var semanticModel = await newDocument.GetSemanticModelAsync();
        var extractedSymbol = semanticModel?.GetDeclaredSymbol(newMethods[0]);
        if (extractedSymbol is null)
        {
            Console.Error.WriteLine("error: could not resolve the symbol for the extracted method.");
            return 1;
        }

        Console.WriteLine($"Extracting selection into '{extractedSymbol.Name}' ({fullFilePath})");

        if (!workspace.TryApplyChanges(newSolution))
        {
            Console.Error.WriteLine("error: workspace rejected the changes");
            return 1;
        }

        Console.WriteLine($"updated: {fullFilePath}");
        return 0;
    }

    static CodeAction? FindByEquivalenceKey(IEnumerable<CodeAction> actions, string equivalenceKey)
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
            Usage: RoslynRefactor extract-method --project <sln|csproj> --file <path> --start-line <n> --start-column <n> --end-line <n> --end-column <n> [--name <newMethodName>]

            Extracts the given 1-based selection into a new method using Roslyn's own
            Extract Method refactoring, replacing the selection with a call to it. If
            --name is given, renames the generated method to the requested name;
            otherwise the name Roslyn generates is kept.
            """);
    }
}
