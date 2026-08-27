using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

sealed class InlineMethodCommand : ICommand
{
    static readonly Lazy<CodeRefactoringProvider> Provider = new(() =>
        CommandSupport.LoadInternalProvider(
            "Microsoft.CodeAnalysis.CSharp.CodeRefactorings.InlineMethod.CSharpInlineMethodRefactoringProvider"));

    public static CommandDescriptor Descriptor { get; } = new(
        "inline-method",
        "Inline a called method's (or local function's) body at the call site",
        [
            CommandSupport.ProjectParameter,
            CommandSupport.FileParameter("Path to the file containing the call site"),
            .. CommandSupport.SpanParameters,
        ],
        RunAsync);

    static Task<int> RunAsync(IReadOnlyDictionary<string, string> arguments, TextWriter output, CancellationToken cancellationToken) =>
        CommandSupport.RunSpanRefactoringAsync(
            arguments,
            Provider.Value,
            actions => CommandSupport.SelectSingle(
                // Roslyn also offers "Inline and keep 'X'", which inlines the call but leaves the original method
                // declaration in place. We only want the variant that removes the original declaration.
                CommandSupport.CollectLeaves(actions).Where(a => a.Title.StartsWith("Inline '", StringComparison.Ordinal)).ToList(),
                "no Inline Method refactoring is available at this location.",
                "multiple matching Inline Method refactorings were found; this is a bug in RoslynRefactor."),
            "Inlining method",
            "Roslyn's Inline Method refactoring produced no changes.",
            output,
            cancellationToken);
}
