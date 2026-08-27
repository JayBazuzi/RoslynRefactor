using System.CommandLine;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

static class CommandSupport
{
    public static Option<string> ProjectOption() =>
        new("--project") { Required = true, Description = "Path to a .sln or .csproj file" };

    public static Option<string> FileOption(string description) =>
        new("--file") { Required = true, Description = description };

    public sealed class SpanOptions
    {
        public Option<int> StartLine { get; } = new("--start-line") { Required = true, Description = "1-based start line of the selection" };
        public Option<int> StartColumn { get; } = new("--start-column") { Required = true, Description = "1-based start column of the selection" };
        public Option<int> EndLine { get; } = new("--end-line") { Required = true, Description = "1-based end line of the selection" };
        public Option<int> EndColumn { get; } = new("--end-column") { Required = true, Description = "1-based end column of the selection" };

        public IEnumerable<Option> All => [StartLine, StartColumn, EndLine, EndColumn];
    }

    public static TextSpan? ToSpan(SourceText text, int startLine, int startColumn, int endLine, int endColumn)
    {
        var start = new LinePosition(startLine - 1, startColumn - 1);
        var end = new LinePosition(endLine - 1, endColumn - 1);
        if (start.Line < 0 || start.Line >= text.Lines.Count || end.Line < 0 || end.Line >= text.Lines.Count)
        {
            return null;
        }

        var startPos = text.Lines[start.Line].Start + start.Character;
        var endPos = text.Lines[end.Line].Start + end.Character;
        if (endPos < startPos)
        {
            return null;
        }

        return TextSpan.FromBounds(startPos, endPos);
    }

    public static Document? FindDocument(Solution solution, string fullFilePath) =>
        solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(Path.GetFullPath(d.FilePath ?? ""), fullFilePath, StringComparison.OrdinalIgnoreCase));

    // Some Roslyn CodeRefactoringProvider implementations are internal to their assembly, so they must be
    // located and instantiated via reflection. Everything else (CodeRefactoringProvider, CodeRefactoringContext,
    // CodeAction, CodeActionOperation) is public API.
    public static CodeRefactoringProvider LoadInternalProvider(string typeName)
    {
        var assemblyName = typeName.StartsWith("Microsoft.CodeAnalysis.CSharp.", StringComparison.Ordinal)
            ? "Microsoft.CodeAnalysis.CSharp.Features"
            : "Microsoft.CodeAnalysis.Features";
        var assembly = Assembly.Load(assemblyName);
        var providerType = assembly.GetType(typeName)
            ?? throw new InvalidOperationException($"Could not locate Roslyn's {typeName}. This tool depends on a Roslyn-internal type that may have moved or been renamed in this Roslyn version.");
        return (CodeRefactoringProvider)Activator.CreateInstance(providerType)!;
    }

    public static CodeAction? FindByEquivalenceKey(IEnumerable<CodeAction> actions, string equivalenceKey)
    {
        foreach (var action in actions)
        {
            if (string.Equals(action.EquivalenceKey, equivalenceKey, StringComparison.Ordinal))
            {
                return action;
            }

            var nested = FindByEquivalenceKey(action.NestedActions, equivalenceKey);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    public static void CollectLeaves(IEnumerable<CodeAction> actions, List<CodeAction> leaves)
    {
        foreach (var action in actions)
        {
            var nested = action.NestedActions;
            if (nested.Length == 0)
            {
                leaves.Add(action);
            }
            else
            {
                CollectLeaves(nested, leaves);
            }
        }
    }
}
