using System.Reflection;
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

        var symbol = await ResolveSymbolAsync(document, line, column, cancellationToken);

        output.WriteLine($"Renaming '{symbol.Name}' ({symbol.Kind}) -> '{newName}'");

        var options = new SymbolRenameOptions();

        var conflicts = await RenameConflictDetector.FindConflictsAsync(symbol, solution, newName, options, cancellationToken);
        if (conflicts.Count > 0)
        {
            output.WriteLine("Rename introduces conflicts:");
            foreach (var conflict in conflicts)
            {
                output.WriteLine($"  {conflict}");
            }
            return 1;
        }

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

    static async Task<ISymbol> ResolveSymbolAsync(Document document, int line, int column, CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken);
        // Convert 1-based line/column to an absolute position.
        var linePosition = new LinePosition(line - 1, column - 1);
        if (linePosition.Line < 0 || linePosition.Line >= text.Lines.Count)
        {
            throw new InvalidOperationException($"line {line} is out of range for {document.FilePath}");
        }
        var position = text.Lines[linePosition.Line].Start + linePosition.Character;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken)
            ?? throw new InvalidOperationException("Could not obtain a semantic model for the document.");

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(semanticModel, position, document.Project.Solution.Workspace, cancellationToken: cancellationToken);
        if (symbol is null)
        {
            throw new InvalidOperationException($"no symbol found at {document.FilePath}:{line}:{column}");
        }

        return symbol;
    }

    // Batch entry point: every rename's (file, line, column) is resolved to a symbol against the
    // ORIGINAL, unmodified solution before any rename is applied. That sidesteps the classic
    // problem with line:column-addressed batch renames - a rename that changes a name's length
    // shifts the text positions of everything after it, which would otherwise invalidate the
    // positions of renames still queued in the same batch. Renames still apply sequentially (later
    // steps see the effects of earlier ones - e.g. if a later rename targets a symbol whose
    // declaration only exists after an earlier step introduces it), but each step re-resolves its
    // symbol via a SymbolKey captured from the original solution, rather than via position, so
    // drift in the text never enters the picture.
    public static async Task<int> RunBatchAsync(
        IReadOnlyList<IReadOnlyDictionary<string, string>> allArguments, TextWriter output, CancellationToken cancellationToken)
    {
        if (allArguments.Count == 0)
        {
            output.WriteLine("No renames specified.");
            return 0;
        }

        var projectPath = allArguments[0]["project"];
        var (workspace, originalSolution) = await WorkspaceLoader.OpenAsync(projectPath);
        using var _ = workspace;

        var steps = new List<(object Key, ProjectId ProjectId, string NewName, string Description)>();
        foreach (var arguments in allArguments)
        {
            var filePath = arguments["file"];
            var line = int.Parse(arguments["line"]);
            var column = int.Parse(arguments["column"]);
            var newName = arguments["to"];

            var fullFilePath = Path.GetFullPath(filePath);
            var document = CommandSupport.FindDocument(originalSolution, fullFilePath);
            if (document is null)
            {
                throw new InvalidOperationException($"file not found in workspace: {fullFilePath}");
            }

            var symbol = await ResolveSymbolAsync(document, line, column, cancellationToken);
            var key = SymbolKeyReflection.Create(symbol, cancellationToken);
            steps.Add((key, document.Project.Id, newName, $"'{symbol.Name}' ({symbol.Kind}) -> '{newName}' ({fullFilePath}:{line}:{column})"));
        }

        var currentSolution = originalSolution;
        var options = new SymbolRenameOptions();

        foreach (var step in steps)
        {
            var compilation = await currentSolution.GetProject(step.ProjectId)!.GetCompilationAsync(cancellationToken)
                ?? throw new InvalidOperationException("could not obtain a compilation to re-resolve a queued rename's symbol.");
            var symbol = SymbolKeyReflection.Resolve(step.Key, compilation, cancellationToken)
                ?? throw new InvalidOperationException($"could not re-resolve symbol for queued rename: {step.Description}");

            output.WriteLine($"Renaming {step.Description}");

            var conflicts = await RenameConflictDetector.FindConflictsAsync(symbol, currentSolution, step.NewName, options, cancellationToken);
            if (conflicts.Count > 0)
            {
                output.WriteLine("Rename introduces conflicts:");
                foreach (var conflict in conflicts)
                {
                    output.WriteLine($"  {conflict}");
                }
                return 1;
            }

            currentSolution = await Renamer.RenameSymbolAsync(currentSolution, symbol, options, step.NewName, cancellationToken);
        }

        var changes = currentSolution.GetChanges(originalSolution);
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
            var doc = currentSolution.GetDocument(docId)!;
            output.WriteLine($"updating: {doc.FilePath}");
        }

        if (!workspace.TryApplyChanges(currentSolution))
        {
            throw new InvalidOperationException("workspace rejected the changes");
        }

        output.WriteLine($"{changedDocuments.Count} file(s) updated.");
        return 0;
    }
}

// Renamer.RenameSymbolAsync's public API silently applies renames even when the new name
// collides with another symbol in scope: it only exposes the resulting Solution, with no way to
// ask whether the rename was actually conflict-free. The conflict information does exist, but
// only in Roslyn's internal ConflictEngine (Microsoft.CodeAnalysis.Rename.ConflictResolution /
// ConflictEngine.RelatedLocation), so we reach it via reflection.
static class RenameConflictDetector
{
    static readonly Assembly WorkspacesAssembly = typeof(Renamer).Assembly;

    static readonly Type SymbolicRenameLocationsType =
        WorkspacesAssembly.GetType("Microsoft.CodeAnalysis.Rename.SymbolicRenameLocations", throwOnError: true)!;

    static readonly MethodInfo FindLocationsMethod =
        SymbolicRenameLocationsType.GetMethod(
            "FindLocationsInCurrentProcessAsync",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(ISymbol), typeof(Solution), typeof(SymbolRenameOptions), typeof(CancellationToken)],
            modifiers: null)!;

    static readonly Type ConflictResolverType =
        WorkspacesAssembly.GetType("Microsoft.CodeAnalysis.Rename.ConflictEngine.ConflictResolver", throwOnError: true)!;

    static readonly MethodInfo ResolveConflictsMethod =
        ConflictResolverType.GetMethod(
            "ResolveSymbolicLocationConflictsInCurrentProcessAsync",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;

    static readonly Type RelatedLocationType =
        WorkspacesAssembly.GetType("Microsoft.CodeAnalysis.Rename.ConflictEngine.RelatedLocation", throwOnError: true)!;

    static readonly PropertyInfo RelatedLocationTypeProperty =
        RelatedLocationType.GetProperty("Type", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;

    static readonly PropertyInfo RelatedLocationDocumentIdProperty =
        RelatedLocationType.GetProperty("DocumentId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;

    static readonly PropertyInfo RelatedLocationConflictCheckSpanProperty =
        RelatedLocationType.GetProperty("ConflictCheckSpan", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;

    public static async Task<IReadOnlyList<string>> FindConflictsAsync(
        ISymbol symbol, Solution solution, string newName, SymbolRenameOptions options, CancellationToken cancellationToken)
    {
        var renameLocationsTask = (Task)FindLocationsMethod.Invoke(null, [symbol, solution, options, cancellationToken])!;
        await renameLocationsTask.ConfigureAwait(false);
        var renameLocations = renameLocationsTask.GetType().GetProperty("Result")!.GetValue(renameLocationsTask)!;

        var conflictResolutionTask = (Task)ResolveConflictsMethod.Invoke(null, [renameLocations, newName, cancellationToken])!;
        await conflictResolutionTask.ConfigureAwait(false);
        var conflictResolution = conflictResolutionTask.GetType().GetProperty("Result")!.GetValue(conflictResolutionTask)!;

        var relatedLocations = (System.Collections.IEnumerable)conflictResolution.GetType()
            .GetField("RelatedLocations", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(conflictResolution)!;

        var newSolution = (Solution)conflictResolution.GetType()
            .GetField("NewSolution", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(conflictResolution)!;

        var conflicts = new List<string>();
        foreach (var relatedLocation in relatedLocations)
        {
            var type = RelatedLocationTypeProperty.GetValue(relatedLocation)!.ToString();
            if (type is not ("UnresolvedConflict" or "UnresolvableConflict"))
            {
                continue;
            }

            var documentId = (DocumentId)RelatedLocationDocumentIdProperty.GetValue(relatedLocation)!;
            var span = (TextSpan)RelatedLocationConflictCheckSpanProperty.GetValue(relatedLocation)!;
            var document = newSolution.GetDocument(documentId);
            LinePositionSpan? lineSpan = document is null
                ? null
                : (await document.GetTextAsync(cancellationToken).ConfigureAwait(false)).Lines.GetLinePositionSpan(span);

            conflicts.Add(lineSpan is { } ls
                ? $"{type} at {document!.FilePath}:{ls.Start.Line + 1}:{ls.Start.Character + 1}"
                : $"{type} in {documentId}");
        }

        return conflicts;
    }
}

// SymbolKey is Roslyn's serializable, cross-solution-snapshot symbol identity - exactly what
// RenameCommand.RunBatchAsync needs to track "this symbol" across the sequence of forked
// Solutions a batch of renames produces - but, like the ConflictEngine types above, it's internal
// in this Roslyn version, so it's reached via reflection.
static class SymbolKeyReflection
{
    static readonly Assembly WorkspacesAssembly = typeof(Renamer).Assembly;

    static readonly Type SymbolKeyType =
        WorkspacesAssembly.GetType("Microsoft.CodeAnalysis.SymbolKey", throwOnError: true)!;

    static readonly MethodInfo CreateMethod =
        SymbolKeyType.GetMethod(
            "Create",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: [typeof(ISymbol), typeof(CancellationToken)],
            modifiers: null)!;

    static readonly MethodInfo ResolveMethod =
        SymbolKeyType.GetMethod(
            "Resolve",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            types: [typeof(Compilation), typeof(bool), typeof(CancellationToken)],
            modifiers: null)!;

    public static object Create(ISymbol symbol, CancellationToken cancellationToken) =>
        CreateMethod.Invoke(null, [symbol, cancellationToken])!;

    public static ISymbol? Resolve(object symbolKey, Compilation compilation, CancellationToken cancellationToken)
    {
        var resolution = ResolveMethod.Invoke(symbolKey, [compilation, false, cancellationToken])!;
        return (ISymbol?)resolution.GetType().GetProperty("Symbol")!.GetValue(resolution);
    }
}
