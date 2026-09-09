using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;

namespace RoslynRefactor;

// There's no Roslyn CodeAction for this (see internal_documentation/extract-iefe-no-captures.md
// for why this command exists at all) - it hand-rolls the whole rewrite: resolving the selection
// to either a run of complete sibling statements or a single expression, checking via
// SemanticModel.AnalyzeDataFlow (plus a syntax walk for `this`/`base`/type-parameter references,
// which data flow analysis doesn't track) that nothing from the enclosing scope is captured, then
// emitting a `static` wrapper around the selection.
//
// One thing the design doc's own worked examples get wrong: C# does not allow invoking a lambda
// expression directly - `(() => x)()` fails to compile with CS0149 ("method name expected"),
// because postfix invocation only applies to primary expressions and a bare lambda literal isn't
// one. A lambda only becomes invocable once something gives it a type, either by assigning it to
// a variable first or by casting it. So both a statement-span selection and an expression
// selection are wrapped as an explicitly-cast, immediately-invoked `static` lambda: `((System.Func
// <T>)(static () => expr))()` for an expression, `((System.Func<T>)(static () => { ...statements
// ...; return x; }))()` (or `System.Action` when nothing flows out) for a run of statements.
sealed class ExtractIefeNoCapturesCommand : ICommand
{
    public static CommandDescriptor Descriptor { get; } = new(
        "extract-iefe-no-captures",
        "(PREVIEW) Wrap a capture-free selection (statements or an expression) in an immediately-invoked static lambda. Would-be captures are instead passed in or returned.",
        [
            CommandSupport.ProjectParameter,
            CommandSupport.FileParameter("Path to the file containing the selection"),
            .. CommandSupport.SpanParameters,
        ],
        RunAsync);

    static Task<int> RunAsync(IReadOnlyDictionary<string, string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        var span = new LineAndColumnSpan(
            new LineAndColumn(int.Parse(arguments["start-line"]), int.Parse(arguments["start-column"])),
            new LineAndColumn(int.Parse(arguments["end-line"]), int.Parse(arguments["end-column"])));
        return RunAsync(arguments["project"], arguments["file"], span, output, cancellationToken);
    }

    static async Task<int> RunAsync(string projectPath, string filePath, LineAndColumnSpan span, TextWriter output, CancellationToken cancellationToken)
    {
        var (workspace, solution, document, fullFilePath) = await CommandSupport.OpenDocumentAsync(projectPath, filePath);
        using var _workspace = workspace;

        var text = await document.GetTextAsync(cancellationToken);
        var textSpan = CommandSupport.ToTextSpan(text, span)
            ?? throw new InvalidOperationException($"selection is out of range for {fullFilePath}");
        var trimmedSpan = TrimWhitespace(text, textSpan);
        if (trimmedSpan.IsEmpty)
        {
            throw new InvalidOperationException("selection is empty.");
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken)
            ?? throw new InvalidOperationException($"could not obtain a syntax tree for {fullFilePath}.");
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken)
            ?? throw new InvalidOperationException($"could not obtain a semantic model for {fullFilePath}.");

        var selection = ResolveSelection(root, trimmedSpan);
        CheckForUnsupportedConstructs(selection.Nodes);

        var analysis = selection is ExpressionSelection expressionOnlySelection
            ? semanticModel.AnalyzeDataFlow(expressionOnlySelection.Expression)
            : semanticModel.AnalyzeDataFlow(((StatementSelection)selection).Statements[0], ((StatementSelection)selection).Statements[^1]);
        if (analysis is not { Succeeded: true })
        {
            throw new InvalidOperationException("could not analyze data flow for the selection.");
        }
        CheckForWrites(analysis);
        var captures = DetermineCaptures(semanticModel, selection, analysis);

        var generator = SyntaxGenerator.GetGenerator(document);

        SyntaxNode newRoot;
        string description;
        if (selection is ExpressionSelection expressionSelection)
        {
            newRoot = ReplaceExpressionSelection(root, semanticModel, generator, expressionSelection, captures);
            description = "Wrapping selected expression in an immediately-invoked static lambda";
        }
        else
        {
            var statementSelection = (StatementSelection)selection;
            var returnInfo = DetermineReturn(semanticModel, statementSelection.Statements, analysis);
            newRoot = ReplaceStatementSelection(root, semanticModel, generator, statementSelection, returnInfo, captures);
            description = "Wrapping selected statements in an immediately-invoked static lambda";
        }

        var newDocument = document.WithSyntaxRoot(newRoot);
        var simplifiedDocument = await Simplifier.ReduceAsync(newDocument, Simplifier.Annotation, cancellationToken: cancellationToken);
        var formattedDocument = await Formatter.FormatAsync(simplifiedDocument, Formatter.Annotation, cancellationToken: cancellationToken);

        // The freshly-synthesized syntax (the lambda's braces, the cast/invoke wrapper, etc.) has
        // no original trivia of its own, so Formatter.FormatAsync falls back to
        // Environment.NewLine for it - which disagrees with the file's own line endings on a
        // machine whose newline default doesn't match (e.g. producing "\r\n" in an otherwise
        // "\n" file on Windows). Re-normalize to whatever the document already used.
        var originalNewLine = DetectNewLine(text);
        var formattedText = await formattedDocument.GetTextAsync(cancellationToken);
        var normalizedContent = NormalizeNewLines(formattedText.ToString(), originalNewLine);
        var finalDocument = formattedDocument.WithText(SourceText.From(normalizedContent, formattedText.Encoding));
        var newSolution = finalDocument.Project.Solution;

        output.WriteLine($"{description} ({fullFilePath})");
        CommandSupport.TryApplyChanges(workspace, solution, newSolution, "No changes produced.", output);
        return 0;
    }

    static string DetectNewLine(SourceText text) => text.Lines.Count > 1
        ? text.ToString(TextSpan.FromBounds(text.Lines[0].Span.End, text.Lines[0].SpanIncludingLineBreak.End))
        : Environment.NewLine;

    static string NormalizeNewLines(string content, string newLine) =>
        newLine == "\n" ? content.Replace("\r\n", "\n") : content.Replace("\r\n", "\n").Replace("\n", newLine);

    static TextSpan TrimWhitespace(SourceText text, TextSpan span)
    {
        var start = span.Start;
        var end = span.End;
        while (start < end && char.IsWhiteSpace(text[start]))
        {
            start++;
        }
        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }
        return TextSpan.FromBounds(start, end);
    }

    // Mirrors extract-method's two selection kinds: a single complete expression, or a run of one
    // or more complete sibling statements. Unlike extract-method (which delegates entirely to
    // Roslyn's internal provider - see ExtractMethodCommand), there's no existing CodeAction to
    // resolve this for us, so it's hand-rolled here.
    static Selection ResolveSelection(SyntaxNode root, TextSpan span)
    {
        var node = root.FindNode(span, getInnermostNodeForTie: true);
        while (node.Span != span && node.Parent is not null && node.Parent.Span == span)
        {
            node = node.Parent;
        }
        if (node is ExpressionSyntax expression && expression.Span == span)
        {
            return new ExpressionSelection(expression);
        }

        var startToken = root.FindToken(span.Start);
        var endToken = root.FindToken(Math.Max(span.Start, span.End - 1));

        // A statement whose body is itself a block (e.g. a foreach) shares its closing brace's
        // position with that inner block, and AncestorsAndSelf walks innermost-first - so without
        // requiring the candidate to actually be an element of a statement list (as opposed to
        // some other statement's embedded body/block), the inner block would be matched instead of
        // the outer statement the user actually meant to select.
        var startStatement = startToken.Parent?.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(s => s.SpanStart == span.Start && IsStatementListMember(s));
        var endStatement = endToken.Parent?.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(s => s.Span.End == span.End && IsStatementListMember(s));

        if (startStatement is null || endStatement is null || startStatement.Parent is null || startStatement.Parent != endStatement.Parent)
        {
            throw new InvalidOperationException("selection must be either a single complete expression or a run of complete statements.");
        }

        var statementList = GetStatementList(startStatement.Parent)
            ?? throw new InvalidOperationException("selected statements must be directly inside a block.");

        var startIndex = statementList.IndexOf(startStatement);
        var endIndex = statementList.IndexOf(endStatement);
        if (startIndex < 0 || endIndex < startIndex)
        {
            throw new InvalidOperationException("selection must be either a single complete expression or a run of complete statements.");
        }

        return new StatementSelection(startStatement.Parent, statementList, startIndex, endIndex);
    }

    static bool IsStatementListMember(StatementSyntax statement) => statement.Parent switch
    {
        BlockSyntax block => block.Statements.Contains(statement),
        SwitchSectionSyntax section => section.Statements.Contains(statement),
        _ => false,
    };

    static SyntaxList<StatementSyntax>? GetStatementList(SyntaxNode parent) => parent switch
    {
        BlockSyntax block => block.Statements,
        SwitchSectionSyntax section => section.Statements,
        _ => null,
    };

    // Selections containing a `yield` or `await` are rejected outright rather than attempting to
    // synthesize an iterator or async wrapper - see internal_documentation/extract-iefe-no-captures.md's
    // "Failure modes to report clearly". Nested local functions/lambdas get their own iterator/async
    // context, so a yield/await inside one of those doesn't count against the outer selection.
    static void CheckForUnsupportedConstructs(IReadOnlyList<SyntaxNode> nodes)
    {
        foreach (var selectedNode in nodes)
        {
            var descendants = selectedNode.DescendantNodesAndSelf(n => n == selectedNode || n is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax));
            if (descendants.OfType<YieldStatementSyntax>().Any())
            {
                throw new InvalidOperationException("selection contains a 'yield' statement; extract-iefe-no-captures does not support iterator bodies.");
            }
            if (descendants.OfType<AwaitExpressionSyntax>().Any())
            {
                throw new InvalidOperationException("selection contains an 'await' expression; extract-iefe-no-captures does not support async code.");
            }
        }
    }

    // The defining precondition: a *write* to a local/parameter declared outside the selection
    // can't be expressed through a plain Func<>/Action<> wrapper (there's no way to pass it back
    // out), so it's rejected outright. Everything else the selection references from the enclosing
    // scope - a read of an outer local/parameter, or `this`/`base`/an instance member - becomes a
    // parameter instead: see DetermineCaptures.
    static void CheckForWrites(DataFlowAnalysis analysis)
    {
        var declaredInside = new HashSet<ISymbol>(analysis.VariablesDeclared, SymbolEqualityComparer.Default);
        var writtenOutside = analysis.WrittenInside
            .Where(v => !declaredInside.Contains(v))
            .Distinct(SymbolEqualityComparer.Default)
            .Select(v => $"'{v.Name}'")
            .ToList();

        if (writtenOutside.Count > 0)
        {
            throw new InvalidOperationException(
                $"selection writes to {string.Join(", ", writtenOutside)} from the enclosing scope; " +
                "extract-iefe-no-captures cannot express a write back to the caller through a Func<>/Action<> wrapper. " +
                "Consider extract-method or make-method-static instead.");
        }
    }

    // Walks the selection in document order, turning every capture into a parameter: a read of an
    // outer local/parameter becomes an ordinary by-value parameter (named after the variable
    // itself, so no rewriting of its references is needed - the parameter simply shadows the outer
    // variable), and any reference to `this`/`base`/an instance member collapses to a single
    // receiver parameter of the containing type, added at the position of its first reference and
    // reused for every later one. A reference to an enclosing generic method's own type parameter
    // needs nothing at all (see the type comment) and is left alone.
    static CaptureAnalysisResult DetermineCaptures(SemanticModel semanticModel, Selection selection, DataFlowAnalysis analysis)
    {
        var declaredInside = new HashSet<ISymbol>(analysis.VariablesDeclared, SymbolEqualityComparer.Default);
        var readOutside = new HashSet<ISymbol>(
            analysis.ReadInside.Where(v => !declaredInside.Contains(v)), SymbolEqualityComparer.Default);

        var parameters = new List<CaptureParameter>();
        var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        string? receiverName = null;
        INamedTypeSymbol? containingType = null;

        void EnsureReceiver(SyntaxNode node)
        {
            if (receiverName is not null)
            {
                return;
            }
            containingType = (semanticModel.GetEnclosingSymbol(node.SpanStart) as ISymbol)?.ContainingType
                ?? throw new InvalidOperationException("could not determine the containing type for 'this'.");
            receiverName = ChooseReceiverName(containingType, parameters, declaredInside);
            parameters.Add(new CaptureParameter(receiverName, containingType, IsReceiver: true));
        }

        foreach (var selectedNode in selection.Nodes)
        {
            foreach (var node in selectedNode.DescendantNodesAndSelf())
            {
                switch (node)
                {
                    case ThisExpressionSyntax or BaseExpressionSyntax:
                        EnsureReceiver(node);
                        break;
                    case IdentifierNameSyntax identifier
                        when !(identifier.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == identifier)
                        && identifier.Parent is not MemberBindingExpressionSyntax:
                    {
                        var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
                        if (symbol is not null && IsInstanceMember(symbol))
                        {
                            EnsureReceiver(node);
                        }
                        else if (symbol is ILocalSymbol or IParameterSymbol && readOutside.Contains(symbol) && seen.Add(symbol))
                        {
                            parameters.Add(new CaptureParameter(symbol.Name, GetSymbolType(symbol), IsReceiver: false));
                        }
                        break;
                    }
                }
            }
        }

        return new CaptureAnalysisResult(parameters, receiverName, containingType);
    }

    // Default to a short, uncapitalized name derived from the type ("widget" for "Widget"),
    // falling back to self/self2/... on collision - mirrors make-method-static's receiver naming.
    static string ChooseReceiverName(INamedTypeSymbol containingType, IReadOnlyList<CaptureParameter> existingParameters, HashSet<ISymbol> declaredInside)
    {
        var reserved = new HashSet<string>(existingParameters.Select(p => p.Name).Concat(declaredInside.Select(s => s.Name)), StringComparer.Ordinal);
        var derived = containingType.Name.Length == 0 ? containingType.Name : char.ToLowerInvariant(containingType.Name[0]) + containingType.Name[1..];
        if (!reserved.Contains(derived))
        {
            return derived;
        }
        if (!reserved.Contains("self"))
        {
            return "self";
        }
        for (var i = 2; ; i++)
        {
            var candidate = "self" + i;
            if (!reserved.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    // DataFlowAnalysis's symbols predate nullable reference types and don't reliably reflect real
    // nullable-flow state - see the same note on DetermineReturn's return-value symbol.
    static ITypeSymbol GetSymbolType(ISymbol symbol) => (symbol switch
    {
        ILocalSymbol local => local.Type,
        IParameterSymbol parameter => parameter.Type,
        _ => throw new InvalidOperationException("unreachable"),
    }).WithNullableAnnotation(NullableAnnotation.None);

    static bool IsInstanceMember(ISymbol symbol) =>
        !symbol.IsStatic && symbol is IFieldSymbol or IPropertySymbol or IEventSymbol
            or IMethodSymbol { MethodKind: MethodKind.Ordinary or MethodKind.PropertyGet or MethodKind.PropertySet };

    // Only meaningful for a statement-span selection - an expression selection's return is just
    // the expression's own value, so this is skipped entirely for that case (see RunAsync). Zero
    // variables flowing out means a void return; exactly one returns that variable directly; more
    // than one becomes a named-element tuple, ordered by where each variable is declared in the
    // selection (not by DataFlowAnalysis's own unordered set).
    static ReturnInfo DetermineReturn(SemanticModel semanticModel, IReadOnlyList<StatementSyntax> statements, DataFlowAnalysis analysis)
    {
        var declaredInside = new HashSet<ISymbol>(analysis.VariablesDeclared, SymbolEqualityComparer.Default);
        var flowsOut = new HashSet<ISymbol>(analysis.DataFlowsOut.Where(declaredInside.Contains), SymbolEqualityComparer.Default);
        if (flowsOut.Count == 0)
        {
            return ReturnInfo.Void;
        }

        var ordered = new List<ILocalSymbol>();
        foreach (var statement in statements)
        {
            foreach (var declarator in statement.DescendantNodesAndSelf().OfType<VariableDeclaratorSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(declarator) is ILocalSymbol local && flowsOut.Contains(local))
                {
                    ordered.Add(local);
                }
            }
        }

        // DataFlowAnalysis's symbols predate nullable reference types and don't reliably reflect
        // real nullable-flow state (e.g. a plain `var items = new List<string>()` comes back
        // annotated here even though Roslyn's own extract-method, using a different code path,
        // emits the unannotated `List<string>` for the identical selection) - erase the annotation
        // rather than risk asserting a nullability the compiler didn't actually conclude.
        var elements = ordered.Select(v => (v.Name, Type: v.Type.WithNullableAnnotation(NullableAnnotation.None))).ToList();
        return elements.Count == 1
            ? new ReturnInfo(elements[0].Name, elements[0].Type, null)
            : new ReturnInfo(null, null, elements);
    }

    // Wraps the selected expression in place as `((System.Func<...>)(static (...) => expr))(...)`
    // (or `System.Action<...>` when the expression's type is void) - see the type comment for why
    // the cast is necessary and why the whole thing needs a second layer of parens (a cast binds
    // looser than postfix invocation, so `(T)x()` means `(T)(x())`, not `((T)x)()`).
    static SyntaxNode ReplaceExpressionSelection(
        SyntaxNode root, SemanticModel semanticModel, SyntaxGenerator generator, ExpressionSelection selection, CaptureAnalysisResult captures)
    {
        var expression = selection.Expression;
        var type = semanticModel.GetTypeInfo(expression).Type;
        if (type is null || type.TypeKind == TypeKind.Error)
        {
            throw new InvalidOperationException("could not determine the type of the selected expression.");
        }
        if (type.IsAnonymousType)
        {
            throw new InvalidOperationException("selected expression has an anonymous type, which cannot be named for the wrapper; extract-iefe-no-captures does not support this.");
        }

        var isVoid = type.SpecialType == SpecialType.System_Void;
        var returnTypeSyntax = isVoid ? null : (TypeSyntax)generator.TypeExpression(type);

        var rewrittenExpression = RewriteCaptures(semanticModel, captures, expression.WithoutTrivia());

        var invocation = BuildInvocation(generator, captures.Parameters, returnTypeSyntax, rewrittenExpression)
            .WithTriviaFrom(expression)
            .WithAdditionalAnnotations(Formatter.Annotation);

        return root.ReplaceNode(expression, invocation);
    }

    // Applies CaptureRewriter to rewrite `this`/`base`/instance-member references through the
    // receiver parameter, if the selection captured one; outer local/parameter reads need no
    // rewriting since their parameter simply shadows the outer variable under the same name.
    static T RewriteCaptures<T>(SemanticModel semanticModel, CaptureAnalysisResult captures, T node) where T : SyntaxNode =>
        captures.ReceiverName is null
            ? node
            : (T)new CaptureRewriter(semanticModel, captures.ContainingType!, captures.ReceiverName).Visit(node)!;

    // When the whole selection is a single `var <returnName> = <expr>;` declaration, the lambda
    // body can just be <expr> directly - an expression-bodied lambda - rather than a block that
    // declares the variable and immediately returns it. Returns null for any other shape (multiple
    // statements, a statement that isn't that one declaration, etc.), which keeps the block-bodied
    // form.
    static ExpressionSyntax? SingleDeclarationInitializer(IReadOnlyList<StatementSyntax> statements, string returnName) =>
        statements is [LocalDeclarationStatementSyntax { Declaration.Variables: [{ Identifier.ValueText: var name, Initializer: { } initializer }] }]
            && name == returnName
            ? initializer.Value
            : null;

    // Builds `((delegateType)(static (...) => body))(...)` - a cast is required because C# does
    // not allow invoking a bare lambda expression directly (see the type comment), and the whole
    // thing needs a second layer of parens since a cast binds looser than postfix invocation.
    // `parameters` supplies both the lambda's parameter list and the delegate's type argument list
    // (one per parameter, plus `returnTypeSyntax` when present); the call-site argument for each
    // parameter is its own name for an ordinary capture, or `this` for the receiver parameter.
    static InvocationExpressionSyntax BuildInvocation(
        SyntaxGenerator generator, IReadOnlyList<CaptureParameter> parameters, TypeSyntax? returnTypeSyntax, CSharpSyntaxNode body)
    {
        var typeArgs = parameters.Select(p => (TypeSyntax)generator.TypeExpression(p.Type)).ToList();
        TypeSyntax delegateType;
        if (returnTypeSyntax is null)
        {
            delegateType = typeArgs.Count == 0
                ? SyntaxFactory.QualifiedName(SyntaxFactory.IdentifierName("System"), SyntaxFactory.IdentifierName("Action"))
                : SyntaxFactory.QualifiedName(
                    SyntaxFactory.IdentifierName("System"),
                    SyntaxFactory.GenericName(SyntaxFactory.Identifier("Action"))
                        .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(typeArgs))));
        }
        else
        {
            typeArgs.Add(returnTypeSyntax);
            delegateType = SyntaxFactory.QualifiedName(
                SyntaxFactory.IdentifierName("System"),
                SyntaxFactory.GenericName(SyntaxFactory.Identifier("Func"))
                    .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SeparatedList(typeArgs))));
        }
        delegateType = delegateType.WithAdditionalAnnotations(Simplifier.Annotation);

        var lambdaParameters = SyntaxFactory.ParameterList(
            SyntaxFactory.SeparatedList(parameters.Select(p => SyntaxFactory.Parameter(SyntaxFactory.Identifier(p.Name)))));
        var lambda = SyntaxFactory.ParenthesizedLambdaExpression()
            .WithParameterList(lambdaParameters)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.StaticKeyword)))
            .WithBody(body);
        var castExpression = SyntaxFactory.CastExpression(delegateType, SyntaxFactory.ParenthesizedExpression(lambda));
        var invocationTarget = SyntaxFactory.InvocationExpression(SyntaxFactory.ParenthesizedExpression(castExpression));

        var arguments = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(
            parameters.Select(p => SyntaxFactory.Argument(p.IsReceiver ? SyntaxFactory.ThisExpression() : SyntaxFactory.IdentifierName(p.Name)))));
        return invocationTarget.WithArgumentList(arguments);
    }

    // Builds `(int count, int total)` - a `TupleType` with named elements, in the given order.
    static TypeSyntax BuildNamedTupleType(SyntaxGenerator generator, IReadOnlyList<(string Name, ITypeSymbol Type)> elements) =>
        SyntaxFactory.TupleType(SyntaxFactory.SeparatedList(elements.Select(e =>
            SyntaxFactory.TupleElement(((TypeSyntax)generator.TypeExpression(e.Type)).WithTrailingTrivia(SyntaxFactory.Space), SyntaxFactory.Identifier(e.Name)))));

    // Wraps the selected statements as the body of an immediately-invoked static lambda in place
    // of the original statements - a bare call for a void return, a `var <name> =` declaration for
    // a single return value, or a `var (<name>, ...) =` deconstruction for a tuple return (see
    // DetermineReturn) - mirroring ReplaceExpressionSelection's cast-and-invoke shape.
    static SyntaxNode ReplaceStatementSelection(
        SyntaxNode root, SemanticModel semanticModel, SyntaxGenerator generator, StatementSelection selection, ReturnInfo returnInfo, CaptureAnalysisResult captures)
    {
        // Only the outer edges of the selection need their trivia touched (the first statement's
        // leading indentation, now wrong since it's moving one level deeper; the last statement's
        // trailing trivia, which belongs at the new call site instead) - the trivia between the
        // selected statements, including the newlines that separate them, is left alone so the
        // formatter (run with Formatter.Annotation below) has something to reindent rather than a
        // run of statements with no line breaks between them at all. The formatter's block-open/
        // block-close rules supply the missing line break for free when there's more than one
        // statement (the first statement always lands on its own line after "{" regardless of its
        // own trivia), but with exactly one statement there's no such rule forcing a break before a
        // synthesized `return` statement that follows it - hence the explicit newline below rather
        // than relying on the formatter alone.
        var bodyStatements = SyntaxFactory.List(selection.Statements.Select((s, i) =>
        {
            s = RewriteCaptures(semanticModel, captures, s);
            if (i == 0)
            {
                s = s.WithLeadingTrivia(SyntaxFactory.ElasticMarker);
            }
            if (i == selection.Statements.Count - 1)
            {
                s = s.WithTrailingTrivia(selection.Statements.Count == 1
                    ? SyntaxFactory.TriviaList(SyntaxFactory.CarriageReturnLineFeed)
                    : SyntaxFactory.TriviaList(SyntaxFactory.ElasticMarker));
            }
            return s;
        }));

        StatementSyntax callSiteStatement;
        if (returnInfo.IsVoid)
        {
            var body = SyntaxFactory.Block(bodyStatements);
            callSiteStatement = SyntaxFactory.ExpressionStatement(BuildInvocation(generator, captures.Parameters, null, body));
        }
        else if (returnInfo.TupleElements is { } tupleElements)
        {
            var returnStatement = SyntaxFactory.ReturnStatement(SyntaxFactory.TupleExpression(
                    SyntaxFactory.SeparatedList(tupleElements.Select(e => SyntaxFactory.Argument(SyntaxFactory.IdentifierName(e.Name))))))
                .WithLeadingTrivia(selection.Statements.Count == 1 ? SyntaxFactory.TriviaList(SyntaxFactory.CarriageReturnLineFeed) : default);
            var body = SyntaxFactory.Block(bodyStatements.Add(returnStatement));
            var returnTypeSyntax = BuildNamedTupleType(generator, tupleElements);
            var invocation = BuildInvocation(generator, captures.Parameters, returnTypeSyntax, body);

            var designation = SyntaxFactory.ParenthesizedVariableDesignation(
                SyntaxFactory.SeparatedList<VariableDesignationSyntax>(
                    tupleElements.Select(e => SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(e.Name)))));
            var declarationExpression = SyntaxFactory.DeclarationExpression(SyntaxFactory.IdentifierName("var"), designation);
            var assignment = SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, declarationExpression, invocation);
            callSiteStatement = SyntaxFactory.ExpressionStatement(assignment);
        }
        else
        {
            var returnName = returnInfo.Name!;
            var returnTypeSyntax = ((TypeSyntax)generator.TypeExpression(returnInfo.Type!)).WithAdditionalAnnotations(Simplifier.Annotation);

            CSharpSyntaxNode body;
            var soleInitializer = SingleDeclarationInitializer(selection.Statements, returnName);
            if (soleInitializer is not null)
            {
                body = RewriteCaptures(semanticModel, captures, soleInitializer.WithoutTrivia());
            }
            else
            {
                var returnStatement = SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName(returnName))
                    .WithLeadingTrivia(selection.Statements.Count == 1 ? SyntaxFactory.TriviaList(SyntaxFactory.CarriageReturnLineFeed) : default);
                body = SyntaxFactory.Block(bodyStatements.Add(returnStatement));
            }

            var invocation = BuildInvocation(generator, captures.Parameters, returnTypeSyntax, body);
            callSiteStatement = SyntaxFactory.LocalDeclarationStatement(
                SyntaxFactory.VariableDeclaration(SyntaxFactory.IdentifierName("var"))
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator(returnName)
                            .WithInitializer(SyntaxFactory.EqualsValueClause(invocation)))));
        }

        callSiteStatement = callSiteStatement
            .WithLeadingTrivia(selection.Statements[0].GetLeadingTrivia())
            .WithTrailingTrivia(selection.Statements[^1].GetTrailingTrivia())
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newStatements = SyntaxFactory.List(
            selection.AllStatements.Take(selection.StartIndex)
                .Append(callSiteStatement)
                .Concat(selection.AllStatements.Skip(selection.EndIndex + 1)));

        var newParent = selection.Parent switch
        {
            BlockSyntax block => (SyntaxNode)block.WithStatements(newStatements),
            SwitchSectionSyntax section => section.WithStatements(newStatements),
            _ => throw new InvalidOperationException("unreachable"),
        };
        return root.ReplaceNode(selection.Parent, newParent);
    }

    // A single capture that becomes a lambda parameter: an ordinary outer local/parameter (passed
    // by its own name at the call site) or the single receiver parameter standing in for
    // `this`/`base`/instance members (passed `this` at the call site).
    sealed record CaptureParameter(string Name, ITypeSymbol Type, bool IsReceiver);

    // The result of walking a selection for captures: its parameters in call order, plus (when the
    // selection referenced `this`/`base`/an instance member) the chosen receiver parameter's name
    // and the containing type it was derived from - both null when there was no such reference.
    sealed record CaptureAnalysisResult(IReadOnlyList<CaptureParameter> Parameters, string? ReceiverName, INamedTypeSymbol? ContainingType);

    // What a statement-span selection returns: nothing (Void), a single named value, or a
    // named-element tuple of more than one.
    sealed record ReturnInfo(string? Name, ITypeSymbol? Type, IReadOnlyList<(string Name, ITypeSymbol Type)>? TupleElements)
    {
        public static readonly ReturnInfo Void = new(null, null, null);
        public bool IsVoid => Name is null && TupleElements is null;
    }

    // Rewrites `this`/`base`/implicit-instance-member references inside a selection to go through
    // the chosen receiver parameter - the same mechanics make-method-static uses for its whole-
    // method version (see MakeMethodStaticCommand.InstanceReferenceRewriter), scoped down to a
    // single selection with no call-site rewriting to do.
    sealed class CaptureRewriter(SemanticModel semanticModel, INamedTypeSymbol containingType, string receiverName) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (node.Expression is not (ThisExpressionSyntax or BaseExpressionSyntax))
            {
                return base.VisitMemberAccessExpression(node);
            }

            var visitedName = (SimpleNameSyntax)Visit(node.Name)!;
            return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(receiverName), visitedName)
                .WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (node.Parent is MemberAccessExpressionSyntax { } memberAccess && memberAccess.Name == node)
            {
                // Handled (or intentionally skipped) by VisitMemberAccessExpression.
                return node;
            }
            if (node.Parent is MemberBindingExpressionSyntax)
            {
                return node;
            }

            var symbol = semanticModel.GetSymbolInfo(node).Symbol;
            if (symbol is null || !IsInstanceMemberOfType(symbol, containingType))
            {
                return node;
            }

            return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(receiverName), SyntaxFactory.IdentifierName(node.Identifier.WithoutTrivia()))
                .WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitThisExpression(ThisExpressionSyntax node) => SyntaxFactory.IdentifierName(receiverName).WithTriviaFrom(node);

        public override SyntaxNode? VisitBaseExpression(BaseExpressionSyntax node) => SyntaxFactory.IdentifierName(receiverName).WithTriviaFrom(node);

        static bool IsInstanceMemberOfType(ISymbol symbol, INamedTypeSymbol type)
        {
            if (!IsInstanceMember(symbol))
            {
                return false;
            }
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, symbol.ContainingType))
                {
                    return true;
                }
            }
            return false;
        }
    }

    abstract class Selection
    {
        public abstract IReadOnlyList<SyntaxNode> Nodes { get; }
    }

    sealed class ExpressionSelection(ExpressionSyntax expression) : Selection
    {
        public ExpressionSyntax Expression { get; } = expression;
        public override IReadOnlyList<SyntaxNode> Nodes { get; } = [expression];
    }

    sealed class StatementSelection(SyntaxNode parent, SyntaxList<StatementSyntax> allStatements, int startIndex, int endIndex) : Selection
    {
        public SyntaxNode Parent { get; } = parent;
        public SyntaxList<StatementSyntax> AllStatements { get; } = allStatements;
        public int StartIndex { get; } = startIndex;
        public int EndIndex { get; } = endIndex;
        public IReadOnlyList<StatementSyntax> Statements { get; } =
            [.. allStatements.Skip(startIndex).Take(endIndex - startIndex + 1)];
        public override IReadOnlyList<SyntaxNode> Nodes => Statements;
    }
}
