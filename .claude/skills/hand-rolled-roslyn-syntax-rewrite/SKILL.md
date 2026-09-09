---
name: hand-rolled-roslyn-syntax-rewrite
description: Add a new RoslynRefactor command that synthesizes brand-new C# syntax by hand (SyntaxFactory nodes with no source-derived trivia) because no Roslyn CodeAction exists for it - e.g. make-method-static, extract-iefe-no-captures. Use whenever a command builds new statements/expressions from scratch rather than editing existing nodes in place, especially anything invoking a lambda immediately or generating multi-statement blocks.
---

Some refactorings have no underlying Roslyn `CodeRefactoringProvider` to delegate to (see
`wrap-dialog-based-roslyn-refactoring` for the case where one exists but needs an options
service - this skill is for when there's genuinely nothing to call). The command has to build the
new syntax itself with `SyntaxFactory`. Three gotchas repeatedly bite this:

1. **A bare lambda literal cannot be invoked directly.** `(() => x)()` does not compile
   (`CS0149: Method name expected`) - postfix invocation only applies to primary expressions, and
   an anonymous function expression isn't one, even with C# 10+ natural delegate types. This holds
   for both expression- and block-bodied lambdas, with or without `static`. Verify any "immediately
   invoke this lambda inline" design against `dotnet run` on a throwaway snippet before writing the
   generator code - don't trust a hand-written example (including ones in a design doc) that shows
   `(lambda)()` or `lambda()`. The fix is an explicit **delegate cast**, wrapped in an *extra* layer
   of parens because a cast binds looser than postfix invocation: `(T)x()` parses as `(T)(x())`,
   not `((T)x)()`. The working shape is `((System.Func<T>)(static () => expr))()` (or
   `System.Action` when `expr`'s type is `void`) - and the same shape works with a block body
   (`static () => { ...; return x; }`) for wrapping a run of statements, so there's no need to fall
   back to a local function for either case. Reject anonymous return types up front - there's no
   way to spell `Func<>`'s type argument for one.

2. **`SemanticModel.AnalyzeDataFlow(...)`'s symbols carry unreliable nullable-reference
   annotations**, unlike `SemanticModel.GetTypeInfo(...)`/`GetSymbolInfo(...)`. A plain
   `var x = new List<string>();` can come back from `DataFlowAnalysis.DataFlowsOut` as
   `List<string>?` even though Roslyn's own `extract-method` (which resolves the return type
   through a different path) emits the unannotated `List<string>` for the identical selection.
   When synthesizing a return type from a `DataFlowAnalysis`-derived `ITypeSymbol`, strip the
   annotation with `type.WithNullableAnnotation(NullableAnnotation.None)` rather than trusting it -
   don't apply the same erasure to types obtained via `GetTypeInfo`/`GetSymbolInfo`, which are
   reliable.

3. **`Formatter.FormatAsync` uses `Environment.NewLine` for any trivia it invents**, not the
   document's own line-ending convention. Freshly-`SyntaxFactory`-built nodes (a new `Block`, a new
   statement) have no original trivia, so the formatter's reformatting of the annotated span (via
   `Formatter.Annotation`) fills in `Environment.NewLine` - `\r\n` on Windows - even inside a file
   that otherwise uses bare `\n` throughout, producing mixed line endings. Detect the document's
   actual newline from its `SourceText` (grab the line-break substring between
   `text.Lines[0].Span.End` and `text.Lines[0].SpanIncludingLineBreak.End`) and normalize the
   formatted output's line endings to match afterward - don't assume `Formatter.FormatAsync`
   preserves the file's convention on its own.

Related gotcha in the same vein: when synthesizing new sibling statements from scratch (e.g. a
statement's own trivia stripped to `SyntaxFactory.ElasticMarker` on both edges because it's both
the first and only element), the formatter's "always break after `{` / before `}`" rules cover the
block's own braces but do **not** invent a break between two adjacent statements that both carry
empty trivia - if a synthesized statement is immediately followed by another synthesized statement
(e.g. a body statement followed by a synthesized `return`), give the second one explicit leading
`SyntaxFactory.CarriageReturnLineFeed` trivia (any real newline character - the formatter fixes
indentation and, per point 3, you fix the newline style afterward) rather than relying on the
formatter to insert one from nothing.
