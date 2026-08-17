using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor.Tests;

/// <summary>
/// A test source that marks the selection to refactor with <c>[|</c> ... <c>|]</c>, the way Roslyn's
/// own tests do. Parsing gives back the source without the markers plus the 1-based line/column pair
/// the CLI wants, so a test case reads as source-in/source-out instead of hand-counted coordinates.
/// </summary>
record SelectionMarkup(string Source, int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    const string StartMarker = "[|";
    const string EndMarker = "|]";

    public static SelectionMarkup Parse(string markedSource)
    {
        var start = markedSource.IndexOf(StartMarker, StringComparison.Ordinal);
        var end = markedSource.IndexOf(EndMarker, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Test source must mark the selection with {StartMarker} ... {EndMarker}.");

        // Remove the end marker first, so the start marker's index is still valid for the second removal.
        var source = markedSource.Remove(end, EndMarker.Length).Remove(start, StartMarker.Length);

        // In the marker-free source the selection starts where "[|" was, and ends two characters
        // earlier than "|]" was, since removing "[|" shifted everything after it left.
        var text = SourceText.From(source);
        var startPosition = text.Lines.GetLinePosition(start);
        var endPosition = text.Lines.GetLinePosition(end - StartMarker.Length);

        return new SelectionMarkup(
            source,
            startPosition.Line + 1,
            startPosition.Character + 1,
            endPosition.Line + 1,
            endPosition.Character + 1);
    }
}
