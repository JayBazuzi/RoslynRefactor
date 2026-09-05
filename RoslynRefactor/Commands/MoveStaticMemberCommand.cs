using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;

namespace RoslynRefactor;

// Roslyn's "Move static members to another type" refactoring
// (Microsoft.CodeAnalysis.MoveStaticMembers.MoveStaticMembersWithDialogCodeAction) is designed
// for VS: it takes its destination/selection from an IMoveStaticMembersOptionsService that shows
// a dialog. There's no CLI-friendly entry point, so this command builds the CodeAction's options
// itself (destination type, selected member) and invokes it directly via reflection, bypassing the
// dialog service entirely (see MoveOperationsAsync below).
sealed class MoveStaticMemberCommand : ICommand
{
    public static CommandDescriptor Descriptor { get; } = new(
        "move-static-member",
        "Move a static member (method/property/field/event) to another type in the same project",
        [
            CommandSupport.ProjectParameter,
            CommandSupport.FileParameter("Path to the file containing the member"),
            .. CommandSupport.PointParameters("member"),
            new("to", "Fully qualified name of the destination type (must already exist in the same project)"),
        ],
        RunAsync);

    static async Task<int> RunAsync(IReadOnlyDictionary<string, string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        var projectPath = arguments["project"];
        var filePath = arguments["file"];
        var line = int.Parse(arguments["line"]);
        var column = int.Parse(arguments["column"]);
        var destinationTypeName = arguments["to"];

        var (workspace, solution, document, _) = await CommandSupport.OpenDocumentAsync(projectPath, filePath);
        using var _workspace = workspace;

        var member = await CommandSupport.ResolveSymbolAtPositionAsync(document, line, column, cancellationToken);
        ValidateMember(member);
        var containingType = member.ContainingType
            ?? throw new InvalidOperationException($"'{member.Name}' has no containing type.");
        if (containingType.TypeKind == TypeKind.Interface)
        {
            throw new InvalidOperationException("cannot move a member out of an interface.");
        }

        var compilation = await document.Project.GetCompilationAsync(cancellationToken)
            ?? throw new InvalidOperationException("could not obtain a compilation for the project.");
        var destinationType = compilation.GetTypeByMetadataName(destinationTypeName)
            ?? throw new InvalidOperationException($"destination type '{destinationTypeName}' not found in project '{document.Project.Name}'.");
        ValidateDestination(destinationType, containingType);

        output.WriteLine($"Moving '{member.Name}' ({member.Kind}) from '{containingType.Name}' to '{destinationType.Name}'");

        var newSolution = await MoveOperationsAsync(document, containingType, destinationType, member, cancellationToken);

        CommandSupport.TryApplyChanges(workspace, solution, newSolution, "Roslyn's Move Static Members refactoring produced no changes.", output);
        return 0;
    }

    // Mirrors Roslyn's internal MemberAndDestinationValidator.IsMemberValid + the IsStatic/kind
    // checks AbstractMoveStaticMembersRefactoringProvider applies before offering the refactoring.
    static void ValidateMember(ISymbol member)
    {
        if (!member.IsStatic)
        {
            throw new InvalidOperationException($"'{member.Name}' is not static; only static members can be moved to another type.");
        }
        if (member.IsImplicitlyDeclared)
        {
            throw new InvalidOperationException($"'{member.Name}' is compiler-generated and cannot be moved.");
        }
        var isMovableKind = member switch
        {
            IFieldSymbol or IPropertySymbol or IEventSymbol => true,
            IMethodSymbol { MethodKind: MethodKind.Ordinary } => true,
            _ => false,
        };
        if (!isMovableKind)
        {
            throw new InvalidOperationException($"'{member.Name}' ({member.Kind}) cannot be moved to another type.");
        }
    }

    // Mirrors Roslyn's internal MemberAndDestinationValidator.IsDestinationValid.
    static void ValidateDestination(INamedTypeSymbol destination, INamedTypeSymbol containingType)
    {
        if (SymbolEqualityComparer.Default.Equals(destination, containingType))
        {
            throw new InvalidOperationException("source and destination types are the same.");
        }
        if (destination.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            throw new InvalidOperationException($"'{destination.Name}' is not a class or struct.");
        }
        if (!destination.Locations.Any(l => l.IsInSource))
        {
            throw new InvalidOperationException($"'{destination.Name}' has no source location to move members into.");
        }
    }

    // Constructs and invokes Roslyn's internal MoveStaticMembersWithDialogCodeAction directly,
    // supplying the destination/selection ourselves instead of going through
    // IMoveStaticMembersOptionsService (which normally collects them from a VS dialog). The
    // service argument is passed as null: CodeActionWithOptions.GetOperationsAsync(options, ct)
    // calls ComputeOperationsAsync directly with the options we hand it, never touching the
    // service field (that only happens via the GetOptions() path, which we skip).
    static async Task<Solution> MoveOperationsAsync(
        Document document, INamedTypeSymbol containingType, INamedTypeSymbol destinationType, ISymbol member, CancellationToken cancellationToken)
    {
        var optionsType = CommandSupport.GetFeaturesType("Microsoft.CodeAnalysis.MoveStaticMembers.MoveStaticMembersOptions");
        var actionType = CommandSupport.GetFeaturesType("Microsoft.CodeAnalysis.MoveStaticMembers.MoveStaticMembersWithDialogCodeAction");

        var selectedMembers = ImmutableArray.Create(member);

        var optionsCtor = optionsType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(c => c.GetParameters() is [{ ParameterType: var t }, _, _] && t == typeof(INamedTypeSymbol));
        var options = optionsCtor.Invoke([destinationType, selectedMembers, false]);

        var actionCtor = actionType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single();
        var codeAction = (CodeActionWithOptions)actionCtor.Invoke([document, null, containingType, selectedMembers]);

        var operations = await codeAction.GetOperationsAsync(options, cancellationToken);
        var applyOperation = operations?.OfType<ApplyChangesOperation>().FirstOrDefault()
            ?? throw new InvalidOperationException("Roslyn's Move Static Members refactoring produced no changes.");

        return applyOperation.ChangedSolution;
    }
}
