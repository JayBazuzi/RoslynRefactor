using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

sealed class IntroduceVariableCommand : ICommand
{
    static readonly Lazy<CodeRefactoringProvider> Provider = new(() =>
        CommandSupport.LoadInternalProvider(
            "Microsoft.CodeAnalysis.IntroduceVariable.IntroduceVariableCodeRefactoringProvider"));

    public static CommandDescriptor Descriptor { get; } = new(
        "introduce-variable",
        "Introduce a local variable for a selected expression",
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
                CommandSupport.CollectLeaves(actions).Where(a => MatchesKindAndScope(a.Title)).ToList(),
                "no 'local' (single occurrence) Introduce Variable refactoring is available for this selection.",
                "multiple matching Introduce Variable refactorings were found; this is a bug in RoslynRefactor."),
            "Introducing variable",
            "Roslyn's Introduce Variable refactoring produced no changes.",
            output,
            cancellationToken);

    static bool MatchesKindAndScope(string title)
    {
        // "Introduce local for" is also a prefix of "Introduce local constant for", so exclude that case explicitly.
        return title.StartsWith("Introduce local for", StringComparison.Ordinal)
            && !title.StartsWith("Introduce local constant for", StringComparison.Ordinal)
            && !title.Contains("all occurrences of", StringComparison.Ordinal);
    }
}
