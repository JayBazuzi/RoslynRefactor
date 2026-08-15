using System.CommandLine;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

sealed class ExtractMethodCommand : ICommand
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

    public static Command Build()
    {
        var project = new Option<string>("--project") { Required = true, Description = "Path to a .sln or .csproj file" };
        var file = new Option<string>("--file") { Required = true, Description = "Path to the file containing the selection" };
        var startLine = new Option<int>("--start-line") { Required = true, Description = "1-based start line of the selection" };
        var startColumn = new Option<int>("--start-column") { Required = true, Description = "1-based start column of the selection" };
        var endLine = new Option<int>("--end-line") { Required = true, Description = "1-based end line of the selection" };
        var endColumn = new Option<int>("--end-column") { Required = true, Description = "1-based end column of the selection" };

        var command = new Command("extract-method", "Extract selected statements into a new method")
        {
            project, file, startLine, startColumn, endLine, endColumn,
        };

        command.SetAction(async (parseResult, cancellationToken) => await RunAsync(
            parseResult.GetValue(project)!,
            parseResult.GetValue(file)!,
            parseResult.GetValue(startLine),
            parseResult.GetValue(startColumn),
            parseResult.GetValue(endLine),
            parseResult.GetValue(endColumn),
            cancellationToken));

        return command;
    }

    static async Task<int> RunAsync(string projectPath, string filePath, int startLine, int startColumn, int endLine, int endColumn, CancellationToken cancellationToken)
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
        var enclosingType = originalRoot?.FindToken(Math.Clamp(span.Value.Start, 0, Math.Max(0, originalRoot.FullSpan.End - 1)))
            .Parent?.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (enclosingType is null)
        {
            Console.Error.WriteLine("error: selection is not inside a type declaration.");
            return 1;
        }

        var existingMethodNames = enclosingType.Members.OfType<MethodDeclarationSyntax>().Select(m => m.Identifier.Text).ToHashSet();

        var actions = new List<CodeAction>();
        var context = new CodeRefactoringContext(document, span.Value, actions.Add, cancellationToken);
        await Provider.Value.ComputeRefactoringsAsync(context);

        var extractMethodAction = FindByEquivalenceKey(actions, "Extract_method");
        if (extractMethodAction is null)
        {
            Console.Error.WriteLine("error: Roslyn's Extract Method refactoring is not available for this selection.");
            return 1;
        }

        var operations = await extractMethodAction.GetOperationsAsync(cancellationToken);
        var applyOperation = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyOperation is null)
        {
            Console.Error.WriteLine("error: Roslyn's Extract Method refactoring produced no changes.");
            return 1;
        }

        var newSolution = applyOperation.ChangedSolution;
        var newDocument = newSolution.GetDocument(document.Id)!;
        var newRoot = await newDocument.GetSyntaxRootAsync(cancellationToken);
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

        var semanticModel = await newDocument.GetSemanticModelAsync(cancellationToken);
        var extractedSymbol = semanticModel?.GetDeclaredSymbol(newMethods[0], cancellationToken);
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
}
