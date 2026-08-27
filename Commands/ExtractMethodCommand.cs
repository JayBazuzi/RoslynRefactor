using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

sealed class ExtractMethodCommand : ICommand
{
    static readonly Lazy<CodeRefactoringProvider> Provider = new(() =>
        CommandSupport.LoadInternalProvider(
            "Microsoft.CodeAnalysis.CodeRefactorings.ExtractMethod.ExtractMethodCodeRefactoringProvider"));

    public static CommandDescriptor Descriptor { get; } = new(
        "extract-method",
        "Extract selected statements into a new method",
        [
            CommandSupport.ProjectParameter,
            CommandSupport.FileParameter("Path to the file containing the selection"),
            .. CommandSupport.SpanParameters,
        ],
        RunAsync);

    static Task<int> RunAsync(IReadOnlyDictionary<string, string> arguments, TextWriter output, CancellationToken cancellationToken) =>
        CommandSupport.RunSpanRefactoringAsync(
            arguments,
            Provider.Value,
            actions => CommandSupport.FindByEquivalenceKey(actions, "Extract_method")
                ?? throw new InvalidOperationException("Roslyn's Extract Method refactoring is not available for this selection."),
            "Extracting selection into a new method",
            "Roslyn's Extract Method refactoring produced no changes.",
            output,
            cancellationToken);
}
