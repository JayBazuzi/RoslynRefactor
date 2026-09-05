using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

// Roslyn's "Change signature" refactoring (Microsoft.CodeAnalysis.ChangeSignature.ChangeSignatureCodeAction)
// is designed for VS: the new parameter order normally comes from an IChangeSignatureOptionsService
// dialog. There's no CLI-friendly entry point, so this command drives the underlying
// AbstractChangeSignatureService language service's public GetChangeSignatureCodeActionAsync to run the
// same analysis/eligibility checks the dialog-based refactoring runs, pulls the resulting analysis
// (the declared symbol and its ParameterConfiguration) out of the private context field of the
// ChangeSignatureCodeAction it returns, builds a reordered ParameterConfiguration ourselves, and invokes
// GetOperationsAsync(options, ct) directly - bypassing the dialog service entirely (see AnalyzeAsync/
// Reorder/ChangeOperationsAsync below). Only reordering existing parameters is supported; adding or
// removing parameters would additionally require synthesizing call-site values for every call site,
// which needs a much larger options object than a CLI command wants to expose.
sealed class ChangeSignatureCommand : ICommand
{
    public static CommandDescriptor Descriptor { get; } = new(
        "reorder-parameters",
        "Reorder a method/property/indexer/delegate's parameters and update all call sites",
        [
            CommandSupport.ProjectParameter,
            CommandSupport.FileParameter("Path to the file containing the member's declaration"),
            .. CommandSupport.PointParameters("member"),
            new("order", "New 1-based parameter order as a comma-separated permutation of the current positions, e.g. \"2,1,3\""),
        ],
        RunAsync);

    static async Task<int> RunAsync(IReadOnlyDictionary<string, string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        var projectPath = arguments["project"];
        var filePath = arguments["file"];
        var line = int.Parse(arguments["line"]);
        var column = int.Parse(arguments["column"]);
        var requestedOrder = arguments["order"]
            .Split(',', StringSplitOptions.TrimEntries)
            .Select(s => int.Parse(s) - 1)
            .ToList();

        var (workspace, solution, document, fullFilePath) = await CommandSupport.OpenDocumentAsync(projectPath, filePath);
        using var _workspace = workspace;

        var text = await document.GetTextAsync(cancellationToken);
        var position = CommandSupport.ToPosition(text, line, column, fullFilePath);

        var (symbol, originalParameterConfiguration, codeAction) = await AnalyzeAsync(document, position, cancellationToken);
        var newParameterConfiguration = Reorder(originalParameterConfiguration, requestedOrder);

        output.WriteLine($"Reordering parameters of '{symbol.Name}' ({symbol.Kind})");

        var newSolution = await ChangeOperationsAsync(codeAction, originalParameterConfiguration, newParameterConfiguration, cancellationToken);

        CommandSupport.TryApplyChanges(workspace, solution, newSolution, "Roslyn's Change Signature refactoring produced no changes.", output);
        return 0;
    }

    // Runs AbstractChangeSignatureService.GetChangeSignatureCodeActionAsync (public, but on an internal
    // language service) - the same analysis Roslyn's Change Signature refactoring runs to find the member
    // declaration at a position and confirm it's eligible - then unpacks the Symbol and
    // ParameterConfiguration it computed out of the private context field of the ChangeSignatureCodeAction
    // it builds.
    static async Task<(ISymbol Symbol, object ParameterConfiguration, CodeActionWithOptions CodeAction)> AnalyzeAsync(
        Document document, int position, CancellationToken cancellationToken)
    {
        var serviceType = CommandSupport.GetFeaturesType("Microsoft.CodeAnalysis.ChangeSignature.AbstractChangeSignatureService");

        var getRequiredService = typeof(LanguageServices)
            .GetMethod(nameof(LanguageServices.GetRequiredService))!
            .MakeGenericMethod(serviceType);
        var service = getRequiredService.Invoke(document.Project.Services, null)!;

        var getActionsMethod = serviceType.GetMethod("GetChangeSignatureCodeActionAsync")!;
        var span = new TextSpan(position, 0);
        var actionsTask = (Task)getActionsMethod.Invoke(service, [document, span, cancellationToken])!;
        await actionsTask;
        var actions = ((System.Collections.IEnumerable)actionsTask.GetType().GetProperty("Result")!.GetValue(actionsTask)!)
            .Cast<object>()
            .ToList();

        if (actions.Count == 0)
        {
            throw new InvalidOperationException("no method, property, indexer, or delegate declaration found to change the signature of at this location.");
        }

        var codeAction = (CodeActionWithOptions)actions[0];
        var context = codeAction.GetType().GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(codeAction)!;
        var contextType = context.GetType();
        var symbol = (ISymbol)contextType.GetField("Symbol")!.GetValue(context)!;
        var parameterConfiguration = contextType.GetField("ParameterConfiguration")!.GetValue(context)!;

        return (symbol, parameterConfiguration, codeAction);
    }

    // Rebuilds Roslyn's internal ParameterConfiguration with the declared (non-'this') parameters
    // permuted according to requestedOrder (0-based positions). Any 'this' parameter (extension methods)
    // is kept fixed in front, and any 'params' parameter is required to stay last - ParameterConfiguration.Create
    // re-derives which parameter is 'this'/'params' from position, exactly as Roslyn's own analysis did.
    static object Reorder(object originalParameterConfiguration, IReadOnlyList<int> requestedOrder)
    {
        var parameterConfigurationType = CommandSupport.GetFeaturesType("Microsoft.CodeAnalysis.ChangeSignature.ParameterConfiguration");
        var parameterType = CommandSupport.GetFeaturesType("Microsoft.CodeAnalysis.ChangeSignature.Parameter");

        var thisParameter = parameterConfigurationType.GetField("ThisParameter")!.GetValue(originalParameterConfiguration);
        var fullList = ((System.Collections.IEnumerable)parameterConfigurationType.GetMethod("ToListOfParameters")!.Invoke(originalParameterConfiguration, null)!)
            .Cast<object>()
            .ToList();
        var declaredParameters = thisParameter is null ? fullList : fullList.Skip(1).ToList();

        if (requestedOrder.Count != declaredParameters.Count
            || requestedOrder.Distinct().Count() != declaredParameters.Count
            || requestedOrder.Any(i => i < 0 || i >= declaredParameters.Count))
        {
            throw new InvalidOperationException($"--order must be a permutation of 1-{declaredParameters.Count} (this member has {declaredParameters.Count} parameter(s)).");
        }

        var paramsParameter = parameterConfigurationType.GetField("ParamsParameter")!.GetValue(originalParameterConfiguration);
        if (paramsParameter is not null && !ReferenceEquals(declaredParameters[requestedOrder[^1]], paramsParameter))
        {
            var name = parameterType.GetProperty("Name")!.GetValue(paramsParameter);
            throw new InvalidOperationException($"'{name}' is a params parameter and must remain last.");
        }

        var reorderedDeclaredParameters = requestedOrder.Select(i => declaredParameters[i]).ToList();
        var newFullList = thisParameter is null ? reorderedDeclaredParameters : [thisParameter, .. reorderedDeclaredParameters];

        var parameterArray = Array.CreateInstance(parameterType, newFullList.Count);
        for (var i = 0; i < newFullList.Count; i++)
        {
            parameterArray.SetValue(newFullList[i], i);
        }

        var immutableArrayCreate = typeof(ImmutableArray)
            .GetMethods()
            .Single(m => m.Name == "Create" && m.IsGenericMethodDefinition && m.GetParameters() is [{ ParameterType.IsArray: true }])
            .MakeGenericMethod(parameterType);
        var parametersImmutableArray = immutableArrayCreate.Invoke(null, [parameterArray]);

        var create = parameterConfigurationType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static)!;
        return create.Invoke(null, [parametersImmutableArray, thisParameter is not null, 0])!;
    }

    // Constructs Roslyn's internal SignatureChange (original -> updated ParameterConfiguration) and
    // ChangeSignatureOptionsResult ourselves - the "options" CodeActionWithOptions.GetOptions would
    // normally collect from a dialog - then invokes GetOperationsAsync(options, ct) directly, which calls
    // ComputeOperationsAsync with our options straight away, without ever going through the dialog service.
    static async Task<Solution> ChangeOperationsAsync(
        CodeActionWithOptions codeAction, object originalParameterConfiguration, object newParameterConfiguration, CancellationToken cancellationToken)
    {
        var signatureChangeType = CommandSupport.GetFeaturesType("Microsoft.CodeAnalysis.ChangeSignature.SignatureChange");
        var optionsType = CommandSupport.GetFeaturesType("Microsoft.CodeAnalysis.ChangeSignature.ChangeSignatureOptionsResult");

        var signatureChangeCtor = signatureChangeType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var signatureChange = signatureChangeCtor.Invoke([originalParameterConfiguration, newParameterConfiguration]);

        var optionsCtor = optionsType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var options = optionsCtor.Invoke([signatureChange, false]);

        // Change Signature reports its result via its own internal ChangeSignatureCodeActionOperation
        // (so it can show a confirmation dialog before applying), not the usual public
        // ApplyChangesOperation - so ChangedSolution is read off it by reflection too.
        var operation = (await codeAction.GetOperationsAsync(options, cancellationToken))?.FirstOrDefault()
            ?? throw new InvalidOperationException("Roslyn's Change Signature refactoring produced no changes.");
        return (Solution)operation.GetType().GetProperty("ChangedSolution")!.GetValue(operation)!;
    }
}
