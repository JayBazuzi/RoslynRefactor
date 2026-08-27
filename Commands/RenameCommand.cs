using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

sealed class RenameCommand : ICommand
{
    public static CommandDescriptor Descriptor { get; } = new(
        "rename",
        "Rename a symbol across a solution/project",
        [
            CommandSupport.ProjectParameter,
            CommandSupport.FileParameter("Path to the file containing the symbol"),
            new("line", "1-based line of the symbol", ValueType: typeof(int)),
            new("column", "1-based column of the symbol", ValueType: typeof(int)),
            new("to", "The new name for the symbol"),
        ],
        RunAsync);

    static async Task<int> RunAsync(IReadOnlyDictionary<string, string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        var projectPath = arguments["project"];
        var filePath = arguments["file"];
        var line = int.Parse(arguments["line"]);
        var column = int.Parse(arguments["column"]);
        var newName = arguments["to"];

        var (workspace, solution) = await WorkspaceLoader.OpenAsync(projectPath);
        using var _ = workspace;

        var fullFilePath = Path.GetFullPath(filePath);
        var document = CommandSupport.FindDocument(solution, fullFilePath);

        if (document is null)
        {
            throw new InvalidOperationException($"file not found in workspace: {fullFilePath}");
        }

        var text = await document.GetTextAsync(cancellationToken);
        // Convert 1-based line/column to an absolute position.
        var linePosition = new LinePosition(line - 1, column - 1);
        if (linePosition.Line < 0 || linePosition.Line >= text.Lines.Count)
        {
            throw new InvalidOperationException($"line {line} is out of range for {fullFilePath}");
        }
        var position = text.Lines[linePosition.Line].Start + linePosition.Character;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken)
            ?? throw new InvalidOperationException("Could not obtain a semantic model for the document.");

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, solution.Workspace, cancellationToken: cancellationToken);
        if (symbol is null)
        {
            throw new InvalidOperationException($"no symbol found at {fullFilePath}:{line}:{column}");
        }

        output.WriteLine($"Renaming '{symbol.Name}' ({symbol.Kind}) -> '{newName}'");

        var options = new SymbolRenameOptions();
        var newSolution = await Renamer.RenameSymbolAsync(solution, symbol, options, newName, cancellationToken);

        var changes = newSolution.GetChanges(solution);
        var changedDocuments = changes.GetProjectChanges()
            .SelectMany(pc => pc.GetChangedDocuments())
            .ToList();

        if (changedDocuments.Count == 0)
        {
            output.WriteLine("No changes produced.");
            return 0;
        }

        foreach (var docId in changedDocuments)
        {
            var doc = newSolution.GetDocument(docId)!;
            output.WriteLine($"updating: {doc.FilePath}");
        }


        if (!workspace.TryApplyChanges(newSolution))
        {
            throw new InvalidOperationException("workspace rejected the changes");
        }

        output.WriteLine($"{changedDocuments.Count} file(s) updated.");
        return 0;
    }
}
