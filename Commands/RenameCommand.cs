using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

sealed class RenameCommand : ICommand
{
    public static string Name => "rename";
    public static string Description => "Rename a symbol across a solution/project";

    public static async Task<int> RunAsync(string[] args)
    {
        string? projectPath = null;
        string? filePath = null;
        int? line = null;
        int? column = null;
        string? newName = null;
        var dryRun = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project": projectPath = args[++i]; break;
                case "--file": filePath = args[++i]; break;
                case "--line": line = int.Parse(args[++i]); break;
                case "--column": column = int.Parse(args[++i]); break;
                case "--to": newName = args[++i]; break;
                case "--dry-run": dryRun = true; break;
                case "-h" or "--help": PrintHelp(); return 0;
                default: Console.Error.WriteLine($"error: unknown option '{args[i]}'"); return 1;
            }
        }

        if (projectPath is null || filePath is null || line is null || column is null || newName is null)
        {
            Console.Error.WriteLine("error: --project, --file, --line, --column, and --to are required");
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
        // Convert 1-based line/column to an absolute position.
        var linePosition = new LinePosition(line.Value - 1, column.Value - 1);
        if (linePosition.Line < 0 || linePosition.Line >= text.Lines.Count)
        {
            Console.Error.WriteLine($"error: line {line} is out of range for {fullFilePath}");
            return 1;
        }
        var position = text.Lines[linePosition.Line].Start + linePosition.Character;

        var semanticModel = await document.GetSemanticModelAsync()
            ?? throw new InvalidOperationException("Could not obtain a semantic model for the document.");

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, solution.Workspace);
        if (symbol is null)
        {
            Console.Error.WriteLine($"error: no symbol found at {fullFilePath}:{line}:{column}");
            return 1;
        }

        Console.WriteLine($"Renaming '{symbol.Name}' ({symbol.Kind}) -> '{newName}'");

        var options = new SymbolRenameOptions();
        var newSolution = await Renamer.RenameSymbolAsync(solution, symbol, options, newName);

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
            Console.WriteLine(dryRun ? $"would update: {doc.FilePath}" : $"updating: {doc.FilePath}");
        }

        if (dryRun)
        {
            Console.WriteLine($"{changedDocuments.Count} file(s) would change. (dry run, nothing written)");
            return 0;
        }

        if (!workspace.TryApplyChanges(newSolution))
        {
            Console.Error.WriteLine("error: workspace rejected the changes");
            return 1;
        }

        Console.WriteLine($"{changedDocuments.Count} file(s) updated.");
        return 0;
    }

    static void PrintHelp()
    {
        Console.WriteLine("""
            Usage: RoslynRefactor rename --project <sln|csproj> --file <path> --line <n> --column <n> --to <newName> [--dry-run]

            Renames the symbol at the given 1-based file position across the whole solution/project.
            """);
    }
}
