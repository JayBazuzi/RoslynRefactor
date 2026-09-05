using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;

namespace RoslynRefactor;

// Roslyn's "Extract interface" refactoring (Microsoft.CodeAnalysis.ExtractInterface.ExtractInterfaceCodeAction)
// is designed for VS: its options (interface name, destination file, included members) normally come from
// an IExtractInterfaceOptionsService dialog. There's no CLI-friendly entry point, so this command drives the
// underlying AbstractExtractInterfaceService language service directly to analyze the type, then builds the
// CodeAction's options itself (all extractable members, a new file) and invokes it via reflection, bypassing
// the dialog service entirely (see AnalyzeAsync/ExtractOperationsAsync below).
sealed class ExtractInterfaceCommand : ICommand
{
    public static CommandDescriptor Descriptor { get; } = new(
        "extract-interface",
        "Extract the public members of a class/struct/interface into a new interface, in a new file",
        [
            CommandSupport.ProjectParameter,
            CommandSupport.FileParameter("Path to the file containing the type"),
            .. CommandSupport.PointParameters("type"),
            new("name", "Name of the extracted interface (default: \"I\" + the type's name)", Required: false),
        ],
        RunAsync);

    static async Task<int> RunAsync(IReadOnlyDictionary<string, string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        var projectPath = arguments["project"];
        var filePath = arguments["file"];
        var line = int.Parse(arguments["line"]);
        var column = int.Parse(arguments["column"]);
        arguments.TryGetValue("name", out var requestedName);

        var (workspace, solution, document, fullFilePath) = await CommandSupport.OpenDocumentAsync(projectPath, filePath);
        using var _workspace = workspace;

        var text = await document.GetTextAsync(cancellationToken);
        var position = CommandSupport.ToPosition(text, line, column, fullFilePath);

        var (service, typeAnalysisResult, typeToExtractFrom, extractableMembers) = await AnalyzeAsync(document, position, cancellationToken);

        var interfaceName = requestedName
            ?? (typeToExtractFrom.TypeKind == TypeKind.Interface ? typeToExtractFrom.Name : "I" + typeToExtractFrom.Name);
        var fileName = interfaceName + Path.GetExtension(fullFilePath);

        output.WriteLine($"Extracting interface '{interfaceName}' from '{typeToExtractFrom.Name}' ({extractableMembers.Length} member(s))");

        var newSolution = await ExtractOperationsAsync(service, typeAnalysisResult, extractableMembers, interfaceName, fileName, cancellationToken);

        CommandSupport.TryApplyChanges(workspace, solution, newSolution, "Roslyn's Extract Interface refactoring produced no changes.", output);
        return 0;
    }

    // Runs AbstractExtractInterfaceService.AnalyzeTypeAtPositionAsync (public, but on an internal type) to
    // locate the type declaration at position and compute its extractable members, then unpacks the
    // publicly-typed fields we need (TypeToExtractFrom, ExtractableMembers) out of the internal result object.
    static async Task<(object Service, object TypeAnalysisResult, INamedTypeSymbol TypeToExtractFrom, ImmutableArray<ISymbol> ExtractableMembers)> AnalyzeAsync(
        Document document, int position, CancellationToken cancellationToken)
    {
        var serviceType = CommandSupport.GetFeaturesType("Microsoft.CodeAnalysis.ExtractInterface.AbstractExtractInterfaceService");
        var typeDiscoveryRuleType = CommandSupport.GetFeaturesType("Microsoft.CodeAnalysis.ExtractInterface.TypeDiscoveryRule");

        var getRequiredService = typeof(Microsoft.CodeAnalysis.Host.LanguageServices)
            .GetMethod(nameof(Microsoft.CodeAnalysis.Host.LanguageServices.GetRequiredService))!
            .MakeGenericMethod(serviceType);
        var service = getRequiredService.Invoke(document.Project.Services, null)!;

        var typeDeclarationRule = Enum.Parse(typeDiscoveryRuleType, "TypeDeclaration");
        var analyzeMethod = serviceType.GetMethod("AnalyzeTypeAtPositionAsync")!;
        var analyzeTask = (Task)analyzeMethod.Invoke(service, [document, position, typeDeclarationRule, cancellationToken])!;
        await analyzeTask;
        var typeAnalysisResult = analyzeTask.GetType().GetProperty("Result")!.GetValue(analyzeTask)!;

        var resultType = typeAnalysisResult.GetType();
        var canExtractInterface = (bool)resultType.GetField("CanExtractInterface")!.GetValue(typeAnalysisResult)!;
        if (!canExtractInterface)
        {
            var errorMessage = (string)resultType.GetField("ErrorMessage")!.GetValue(typeAnalysisResult)!;
            throw new InvalidOperationException(errorMessage);
        }

        var typeToExtractFrom = (INamedTypeSymbol)resultType.GetField("TypeToExtractFrom")!.GetValue(typeAnalysisResult)!;
        var extractableMembers = (ImmutableArray<ISymbol>)resultType.GetField("ExtractableMembers")!.GetValue(typeAnalysisResult)!;
        return (service, typeAnalysisResult, typeToExtractFrom, extractableMembers);
    }

    // Constructs Roslyn's internal ExtractInterfaceOptionsResult (the "options" CodeActionWithOptions.GetOptions
    // would normally collect from a dialog) and ExtractInterfaceCodeAction ourselves, then invokes
    // GetOperationsAsync(options, ct) directly - this calls ComputeOperationsAsync with our options straight
    // away, without ever going through GetOptions()/the dialog service.
    static async Task<Solution> ExtractOperationsAsync(
        object service, object typeAnalysisResult, ImmutableArray<ISymbol> includedMembers, string interfaceName, string fileName, CancellationToken cancellationToken)
    {
        var optionsType = CommandSupport.GetFeaturesType("Microsoft.CodeAnalysis.ExtractInterface.ExtractInterfaceOptionsResult");
        var actionType = CommandSupport.GetFeaturesType("Microsoft.CodeAnalysis.ExtractInterface.ExtractInterfaceCodeAction");

        var extractLocationType = optionsType.GetNestedType("ExtractLocation", BindingFlags.Public | BindingFlags.NonPublic)!;
        var newFileLocation = Enum.Parse(extractLocationType, "NewFile");

        var optionsCtor = optionsType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters().Length == 5);
        var options = optionsCtor.Invoke([false, includedMembers, interfaceName, fileName, newFileLocation]);

        var actionCtor = actionType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var codeAction = (CodeActionWithOptions)actionCtor.Invoke([service, typeAnalysisResult]);

        var operations = await codeAction.GetOperationsAsync(options, cancellationToken);
        var applyOperation = operations?.OfType<ApplyChangesOperation>().FirstOrDefault()
            ?? throw new InvalidOperationException("Roslyn's Extract Interface refactoring produced no changes.");

        return applyOperation.ChangedSolution;
    }
}
