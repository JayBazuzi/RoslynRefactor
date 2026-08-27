using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

abstract class ConvertToLinqCommandBase
{
    static readonly Lazy<CodeRefactoringProvider> Provider = new(() =>
        CommandSupport.LoadInternalProvider(
            "Microsoft.CodeAnalysis.CSharp.ConvertLinq.ConvertForEachToLinqQuery.CSharpConvertForEachToLinqQueryProvider"));

    protected static CommandDescriptor BuildDescriptor(string name, string description, string equivalenceKey) => new(
        name,
        description,
        [
            CommandSupport.ProjectParameter,
            CommandSupport.FileParameter("Path to the file containing the selection"),
            .. CommandSupport.SpanParameters,
        ],
        (arguments, output, cancellationToken) => RunAsync(arguments, equivalenceKey, output, cancellationToken));

    static Task<int> RunAsync(IReadOnlyDictionary<string, string> arguments, string equivalenceKey, TextWriter output, CancellationToken cancellationToken) =>
        CommandSupport.RunSpanRefactoringAsync(
            arguments,
            Provider.Value,
            actions => CommandSupport.FindByEquivalenceKey(actions, equivalenceKey)
                ?? throw new InvalidOperationException("Roslyn's Convert to LINQ refactoring is not available for this selection."),
            "Converting foreach to LINQ",
            "Roslyn's Convert to LINQ refactoring produced no changes.",
            output,
            cancellationToken);
}
