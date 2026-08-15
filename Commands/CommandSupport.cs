using System.CommandLine;
using Microsoft.CodeAnalysis;
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
}
