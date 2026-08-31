using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

sealed class InlineTemporaryVariableCommand : ICommand
{
    static readonly Lazy<CodeRefactoringProvider> Provider = new(() =>
        CommandSupport.LoadInternalProvider(
            "Microsoft.CodeAnalysis.CSharp.CodeRefactorings.InlineTemporary.CSharpInlineTemporaryCodeRefactoringProvider"));

    public static CommandDescriptor Descriptor { get; } = new(
        "inline-temporary-variable",
        "Inline a local variable's initializer into all usages, then remove the declaration",
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
            actions => CommandSupport.SelectSingle(
                CommandSupport.CollectLeaves(actions).ToList(),
                "no Inline Temporary Variable refactoring is available for this selection.",
                "multiple matching Inline Temporary Variable refactorings were found; this is a bug in RoslynRefactor."),
            "Inlining temporary variable",
            "Roslyn's Inline Temporary Variable refactoring produced no changes.",
            output,
            cancellationToken);
}
