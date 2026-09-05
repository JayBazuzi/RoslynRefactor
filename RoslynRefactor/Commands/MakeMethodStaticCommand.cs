using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynRefactor;

// There's no Roslyn CodeAction to wrap here (see internal_documentation/make-method-static.md for
// why) - this command hand-rolls the whole rewrite: it walks the method body itself to turn every
// implicit-`this` reference into an explicit reference through a new receiver parameter, then uses
// SymbolFinder.FindReferencesAsync (as `rename` and `move-static-member` do) to rewrite every call
// site to pass its former receiver as that parameter's argument. Both the body rewrite and every
// call-site rewrite are performed by a single CSharpSyntaxRewriter per affected document
// (InstanceReferenceRewriter below) so that a call site inside the method's own body (a recursive
// call) and a call site in another file are handled by exactly the same logic, and so nested edits
// (e.g. an instance-member reference inside a recursive call's arguments) never overlap with an
// enclosing edit the way they could with two independent passes.
sealed class MakeMethodStaticCommand : ICommand
{
    public static CommandDescriptor Descriptor { get; } = new(
        "make-method-static",
        "Convert an instance method into a static method by adding a parameter for its receiver, and update all call sites",
        [
            CommandSupport.ProjectParameter,
            CommandSupport.FileParameter("Path to the file containing the method"),
            .. CommandSupport.PointParameters("method"),
            new("parameter-name", "Name for the new receiver parameter (default: derived from the type name, falling back to self/self2/... on collision)", Required: false),
        ],
        RunAsync);

    static Task<int> RunAsync(IReadOnlyDictionary<string, string> arguments, TextWriter output, CancellationToken cancellationToken)
    {
        arguments.TryGetValue("parameter-name", out var requestedParameterName);
        return RunAsync(arguments["project"], arguments["file"], int.Parse(arguments["line"]), int.Parse(arguments["column"]), requestedParameterName, output, cancellationToken);
    }

    static async Task<int> RunAsync(
        string projectPath, string filePath, int line, int column, string? requestedParameterName, TextWriter output, CancellationToken cancellationToken)
    {
        var (workspace, solution, document, _) = await CommandSupport.OpenDocumentAsync(projectPath, filePath);
        using var _workspace = workspace;

        var symbol = await CommandSupport.ResolveSymbolAtPositionAsync(document, line, column, cancellationToken);
        if (symbol is not IMethodSymbol method)
        {
            throw new InvalidOperationException($"'{symbol.Name}' ({symbol.Kind}) is not a method.");
        }

        ValidateMethod(method);

        var containingType = method.ContainingType;
        var declaringSyntaxRef = method.DeclaringSyntaxReferences.Single();
        var declaringDocument = solution.GetDocument(declaringSyntaxRef.SyntaxTree)
            ?? throw new InvalidOperationException($"could not find the document declaring '{method.Name}' in the workspace.");
        var methodNode = (MethodDeclarationSyntax)await declaringSyntaxRef.GetSyntaxAsync(cancellationToken);

        var selfName = ChooseParameterName(requestedParameterName, method, methodNode);

        // Validate every call site up front, against the untouched solution, so a call site this
        // command can't safely rewrite is reported before any edits are made anywhere.
        var referencedSymbols = await SymbolFinder.FindReferencesAsync(method, solution, cancellationToken);
        var locations = referencedSymbols.SelectMany(r => r.Locations).ToList();
        var rootsByDocument = new Dictionary<DocumentId, SyntaxNode>();
        foreach (var referenceLocation in locations)
        {
            var referenceDocument = referenceLocation.Document;
            if (!rootsByDocument.TryGetValue(referenceDocument.Id, out var root))
            {
                root = await referenceDocument.GetSyntaxRootAsync(cancellationToken)
                    ?? throw new InvalidOperationException($"could not obtain a syntax tree for {referenceDocument.FilePath}.");
                rootsByDocument[referenceDocument.Id] = root;
            }

            var nameNode = root.FindNode(referenceLocation.Location.SourceSpan, getInnermostNodeForTie: true);
            if (!IsDirectInvocationCallee(nameNode, out _))
            {
                var position = referenceLocation.Location.GetLineSpan().StartLinePosition;
                throw new InvalidOperationException(
                    $"'{method.Name}' is referenced without being called directly (e.g. as a method group or delegate) at {referenceDocument.FilePath}:{position.Line + 1}:{position.Character + 1}; this is not supported.");
            }
        }

        output.WriteLine($"Making '{method.Name}' static, adding receiver parameter '{selfName}' ({containingType.Name})");

        var documentIds = locations.Select(l => l.Document.Id).Append(declaringDocument.Id).Distinct().ToList();
        var newSolution = solution;
        foreach (var documentId in documentIds)
        {
            var currentDocument = solution.GetDocument(documentId)!;
            var semanticModel = await currentDocument.GetSemanticModelAsync(cancellationToken)
                ?? throw new InvalidOperationException($"could not obtain a semantic model for {currentDocument.FilePath}.");
            var root = rootsByDocument.TryGetValue(documentId, out var cachedRoot)
                ? cachedRoot
                : await currentDocument.GetSyntaxRootAsync(cancellationToken)
                    ?? throw new InvalidOperationException($"could not obtain a syntax tree for {currentDocument.FilePath}.");

            var generator = SyntaxGenerator.GetGenerator(currentDocument);
            var rewriter = new InstanceReferenceRewriter(
                semanticModel, method, containingType, selfName, generator, documentId == declaringDocument.Id ? methodNode : null);
            var newRoot = rewriter.Visit(root);

            newSolution = newSolution.WithDocumentSyntaxRoot(documentId, newRoot);
        }

        CommandSupport.TryApplyChanges(workspace, solution, newSolution, "No changes produced.", output);
        return 0;
    }

    // Mirrors the eligibility checks in internal_documentation/make-method-static.md: only a plain
    // instance method with no polymorphic call sites to worry about (no virtual/override/interface
    // implementation) can safely gain a parameter and go static.
    static void ValidateMethod(IMethodSymbol method)
    {
        if (method.MethodKind != MethodKind.Ordinary)
        {
            throw new InvalidOperationException($"'{method.Name}' ({method.MethodKind}) cannot be made static; only ordinary methods are supported.");
        }
        if (method.IsStatic)
        {
            throw new InvalidOperationException($"'{method.Name}' is already static.");
        }
        if (method.IsVirtual || method.IsOverride || method.IsAbstract)
        {
            throw new InvalidOperationException($"'{method.Name}' is virtual, override, or abstract; making it static would change its calling convention at every polymorphic call site.");
        }
        if (method.ExplicitInterfaceImplementations.Length > 0 || ImplementsInterfaceMember(method))
        {
            throw new InvalidOperationException($"'{method.Name}' implements an interface member and cannot be made static.");
        }
        if (method.DeclaringSyntaxReferences.Length != 1)
        {
            throw new InvalidOperationException($"'{method.Name}' has more than one declaring syntax reference and is not supported.");
        }
        var syntax = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences[0].GetSyntax();
        if (syntax.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            throw new InvalidOperationException($"'{method.Name}' is a partial method and is not supported.");
        }
    }

    static bool ImplementsInterfaceMember(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        foreach (var @interface in containingType.AllInterfaces)
        {
            foreach (var interfaceMethod in @interface.GetMembers().OfType<IMethodSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(containingType.FindImplementationForInterfaceMember(interfaceMethod), method))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // Default to a short, uncapitalized name derived from the type ("widget" for "Widget"),
    // falling back to self/self2/... on collision with a parameter, local, or type parameter
    // already in scope in the method.
    static string ChooseParameterName(string? requestedName, IMethodSymbol method, MethodDeclarationSyntax syntax)
    {
        if (requestedName is not null)
        {
            return requestedName;
        }

        var reserved = CollectReservedNames(method, syntax);
        var derived = DecapitalizeFirstLetter(method.ContainingType.Name);
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

    static string DecapitalizeFirstLetter(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    static HashSet<string> CollectReservedNames(IMethodSymbol method, MethodDeclarationSyntax syntax)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeParameter in method.ContainingType.TypeParameters)
        {
            names.Add(typeParameter.Name);
        }
        foreach (var typeParameter in method.TypeParameters)
        {
            names.Add(typeParameter.Name);
        }
        foreach (var parameter in syntax.DescendantNodes().OfType<ParameterSyntax>())
        {
            names.Add(parameter.Identifier.Text);
        }
        foreach (var variable in syntax.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            names.Add(variable.Identifier.Text);
        }
        foreach (var designation in syntax.DescendantNodes().OfType<SingleVariableDesignationSyntax>())
        {
            names.Add(designation.Identifier.Text);
        }
        foreach (var forEachStatement in syntax.DescendantNodes().OfType<ForEachStatementSyntax>())
        {
            names.Add(forEachStatement.Identifier.Text);
        }
        foreach (var catchDeclaration in syntax.DescendantNodes().OfType<CatchDeclarationSyntax>())
        {
            if (catchDeclaration.Identifier != default)
            {
                names.Add(catchDeclaration.Identifier.Text);
            }
        }
        foreach (var localFunction in syntax.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
        {
            names.Add(localFunction.Identifier.Text);
        }
        foreach (var typeParameter in syntax.DescendantNodes().OfType<TypeParameterSyntax>())
        {
            names.Add(typeParameter.Identifier.Text);
        }
        return names;
    }

    static bool IsInstanceMemberOfType(ISymbol? symbol, INamedTypeSymbol type)
    {
        if (symbol is null || symbol.IsStatic)
        {
            return false;
        }
        if (symbol is not (IFieldSymbol or IPropertySymbol or IEventSymbol or IMethodSymbol { MethodKind: MethodKind.Ordinary }))
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

    // A reference to the method is only safe to rewrite when it's the callee of a direct
    // invocation - bare (implicit receiver) or qualified (explicit receiver). Anything else (a
    // method group used as a delegate, passed as an argument, assigned to a variable, etc.) would
    // need a captured-receiver closure synthesized for it, which this command doesn't attempt.
    static bool IsDirectInvocationCallee(SyntaxNode nameNode, out InvocationExpressionSyntax? invocation)
    {
        if (nameNode.Parent is InvocationExpressionSyntax direct && direct.Expression == nameNode)
        {
            invocation = direct;
            return true;
        }
        if (nameNode.Parent is MemberAccessExpressionSyntax { } memberAccess && memberAccess.Name == nameNode
            && memberAccess.Parent is InvocationExpressionSyntax viaMember && viaMember.Expression == memberAccess)
        {
            invocation = viaMember;
            return true;
        }
        invocation = null;
        return false;
    }

    // Keeping this to the type's simple name (no namespace/generic-argument qualification) matches
    // how a hand-written call site would read; qualifying generic types properly is out of scope.
    static string SimpleTypeName(INamedTypeSymbol type) => type.Name;

    // Rewrites a single document's syntax tree for the conversion. While descending into the
    // target method's own body (insideTargetBody), every implicit-`this` reference to an instance
    // member of the containing type is rewritten to go through `selfName`. Everywhere in the
    // document (regardless of insideTargetBody), every direct-invocation call site of the target
    // method itself is rewritten to pass its former receiver as the new first argument. Because
    // both concerns are handled by descending through the SAME tree once, a call site that's also
    // inside the method's own body (a recursive call) never produces two overlapping edits: the
    // instance-member rule explicitly leaves the callee expression of such a call untouched (see
    // VisitIdentifierName/VisitMemberAccessExpression) and only the invocation-level rule replaces it.
    sealed class InstanceReferenceRewriter(
        SemanticModel semanticModel, IMethodSymbol method, INamedTypeSymbol containingType, string selfName, SyntaxGenerator generator, MethodDeclarationSyntax? targetMethodNode)
        : CSharpSyntaxRewriter
    {
        bool insideTargetBody;

        // SeparatedSyntaxList<T>.Insert adds a bare comma with no trivia; build the list by hand so
        // the existing parameters keep the usual ", " separator.
        static SeparatedSyntaxList<ParameterSyntax> InsertFirstParameter(SeparatedSyntaxList<ParameterSyntax> parameters, ParameterSyntax first)
        {
            if (parameters.Count == 0)
            {
                return SyntaxFactory.SingletonSeparatedList(first);
            }

            var nodesAndTokens = new List<SyntaxNodeOrToken>
            {
                first,
                SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space),
            };
            nodesAndTokens.AddRange(parameters.GetWithSeparators());
            return SyntaxFactory.SeparatedList<ParameterSyntax>(SyntaxFactory.NodeOrTokenList(nodesAndTokens));
        }

        // Same reasoning as InsertFirstParameter: keep the usual ", " separator between arguments.
        static SeparatedSyntaxList<ArgumentSyntax> InsertFirstArgument(SeparatedSyntaxList<ArgumentSyntax> arguments, ArgumentSyntax first)
        {
            if (arguments.Count == 0)
            {
                return SyntaxFactory.SingletonSeparatedList(first);
            }

            var nodesAndTokens = new List<SyntaxNodeOrToken>
            {
                first,
                SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space),
            };
            nodesAndTokens.AddRange(arguments.GetWithSeparators());
            return SyntaxFactory.SeparatedList<ArgumentSyntax>(SyntaxFactory.NodeOrTokenList(nodesAndTokens));
        }

        public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            if (targetMethodNode is null || node != targetMethodNode)
            {
                return base.VisitMethodDeclaration(node);
            }

            insideTargetBody = true;
            var newBody = (BlockSyntax?)Visit(node.Body);
            var newExpressionBody = (ArrowExpressionClauseSyntax?)Visit(node.ExpressionBody);
            insideTargetBody = false;

            var withBody = node.WithBody(newBody).WithExpressionBody(newExpressionBody);

            var selfParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(selfName))
                .WithType(SyntaxFactory.ParseTypeName(SimpleTypeName(containingType)).WithTrailingTrivia(SyntaxFactory.Space));
            var withParameter = withBody.WithParameterList(
                withBody.ParameterList.WithParameters(InsertFirstParameter(withBody.ParameterList.Parameters, selfParameter)));

            var modifiers = generator.GetModifiers(withParameter);
            return (MethodDeclarationSyntax)generator.WithModifiers(withParameter, modifiers.WithIsStatic(true));
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;

            var calleeSymbol = semanticModel.GetSymbolInfo(node).Symbol;
            if (calleeSymbol is null || !SymbolEqualityComparer.Default.Equals(calleeSymbol.OriginalDefinition, method.OriginalDefinition))
            {
                return visited;
            }

            var originalReceiver = node.Expression is MemberAccessExpressionSyntax originalMemberAccess ? originalMemberAccess.Expression : null;
            var enclosingSymbol = semanticModel.GetEnclosingSymbol(node.SpanStart);
            var isRecursiveCall = enclosingSymbol is not null && SymbolEqualityComparer.Default.Equals(enclosingSymbol.OriginalDefinition, method.OriginalDefinition);

            ExpressionSyntax newReceiver;
            if (originalReceiver is null or ThisExpressionSyntax)
            {
                newReceiver = isRecursiveCall ? SyntaxFactory.IdentifierName(selfName) : SyntaxFactory.ThisExpression();
            }
            else
            {
                var visitedReceiver = visited.Expression is MemberAccessExpressionSyntax visitedMemberAccess ? visitedMemberAccess.Expression : originalReceiver;
                newReceiver = visitedReceiver.WithoutTrivia();
            }

            var inScope = semanticModel.LookupSymbols(node.SpanStart, name: method.Name)
                .Any(s => SymbolEqualityComparer.Default.Equals(s.OriginalDefinition, method.OriginalDefinition));
            ExpressionSyntax callee = inScope
                ? SyntaxFactory.IdentifierName(method.Name)
                : SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(SimpleTypeName(containingType)), SyntaxFactory.IdentifierName(method.Name));

            var newArguments = SyntaxFactory.ArgumentList(InsertFirstArgument(visited.ArgumentList.Arguments, SyntaxFactory.Argument(newReceiver)));

            return SyntaxFactory.InvocationExpression(callee, newArguments).WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (!insideTargetBody || node.Expression is not ThisExpressionSyntax)
            {
                return base.VisitMemberAccessExpression(node);
            }

            var symbol = semanticModel.GetSymbolInfo(node).Symbol;
            if (symbol is not null && SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, method.OriginalDefinition))
            {
                // A recursive call written as `this.Method(...)` - leave the callee alone; the
                // invocation-level rule above replaces the whole call.
                return node;
            }
            if (!IsInstanceMemberOfType(symbol, containingType))
            {
                return node;
            }

            return SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(selfName), node.Name.WithoutTrivia())
                .WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (!insideTargetBody)
            {
                return base.VisitIdentifierName(node);
            }
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
            if (symbol is not null && SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, method.OriginalDefinition))
            {
                // A recursive bare call, e.g. `Method(...)` - leave it alone; the invocation-level
                // rule above replaces the whole call.
                return node;
            }
            if (!IsInstanceMemberOfType(symbol, containingType))
            {
                return node;
            }

            return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression, SyntaxFactory.IdentifierName(selfName), SyntaxFactory.IdentifierName(node.Identifier.WithoutTrivia()))
                .WithTriviaFrom(node);
        }

        public override SyntaxNode? VisitThisExpression(ThisExpressionSyntax node) =>
            insideTargetBody ? SyntaxFactory.IdentifierName(selfName).WithTriviaFrom(node) : node;
    }
}
