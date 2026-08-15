using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

sealed class RenameCommand : ICommand
{
    public static Command Build()
    {
        var project = CommandSupport.ProjectOption();
        var file = CommandSupport.FileOption("Path to the file containing the symbol");
        var line = new Option<int>("--line") { Required = true, Description = "1-based line of the symbol" };
        var column = new Option<int>("--column") { Required = true, Description = "1-based column of the symbol" };
        var to = new Option<string>("--to") { Required = true, Description = "The new name for the symbol" };

        var command = new Command("rename", "Rename a symbol across a solution/project")
        {
            project, file, line, column, to,
        };

        command.SetAction(async (parseResult, cancellationToken) => await RunAsync(
            parseResult.GetValue(project)!,
            parseResult.GetValue(file)!,
            parseResult.GetValue(line),
            parseResult.GetValue(column),
            parseResult.GetValue(to)!,
            cancellationToken));

        return command;
    }

    static async Task<int> RunAsync(string projectPath, string filePath, int line, int column, string newName, CancellationToken cancellationToken)
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
        // Convert 1-based line/column to an absolute position.
        var linePosition = new LinePosition(line - 1, column - 1);
        if (linePosition.Line < 0 || linePosition.Line >= text.Lines.Count)
        {
            Console.Error.WriteLine($"error: line {line} is out of range for {fullFilePath}");
            return 1;
        }
        var position = text.Lines[linePosition.Line].Start + linePosition.Character;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken)
            ?? throw new InvalidOperationException("Could not obtain a semantic model for the document.");

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, solution.Workspace, cancellationToken: cancellationToken);
        if (symbol is null)
        {
            Console.Error.WriteLine($"error: no symbol found at {fullFilePath}:{line}:{column}");
            return 1;
        }

        Console.WriteLine($"Renaming '{symbol.Name}' ({symbol.Kind}) -> '{newName}'");

        var options = new SymbolRenameOptions();
        var newSolution = await Renamer.RenameSymbolAsync(solution, symbol, options, newName, cancellationToken);

        var changes = newSolution.GetChanges(solution);
        var changedDocuments = changes.GetProjectChanges()
            .SelectMany(pc => pc.GetChangedDocuments())
            .ToList();

        if (changedDocuments.Count == 0)
        {
            Console.WriteLine("No changes produced.");
            return 0;
        }

        foreach (var docId in changedDocuments)
        {
            var doc = newSolution.GetDocument(docId)!;
            Console.WriteLine($"updating: {doc.FilePath}");
        }


        if (!workspace.TryApplyChanges(newSolution))
        {
            Console.Error.WriteLine("error: workspace rejected the changes");
            return 1;
        }

        Console.WriteLine($"{changedDocuments.Count} file(s) updated.");
        return 0;
    }
}
