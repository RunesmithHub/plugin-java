using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Semantics;
using Runesmith.Languages.Java.Syntax;
using Runesmith.Sdk.Languages;
using Runesmith.Text;
using JavaModifiers = Runesmith.Languages.Java.ClassFiles.JavaModifiers;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>Finds the symbols of many names in one file, with one typer and one scope builder for all of them.</summary>
internal sealed class SymbolResolver(SemanticModel model)
{
    private readonly ExpressionTyper typer = new(model);
    private ScopeBuilder? builder;

    public SemanticModel Model => model;

    private ScopeBuilder Builder => builder ??= new ScopeBuilder(typer);

    /// <summary>Gets the identifier at an offset, or the one that ends there, as the caret right after a name is still on it.</summary>
    public JavaToken? IdentifierAt(int offset)
    {
        var tree = model.Tree;
        var token = tree.FindToken(offset);
        if (token.Kind == TokenKind.Identifier && token.Span.ContainsInclusive(offset))
            return token;
        return tree.FindTokenBefore(offset) is { Kind: TokenKind.Identifier } before && before.Span.End == offset ? before : null;
    }

    /// <summary>Finds the symbol an identifier stands for; the name of the class a <c>new</c> creates stands for the class.</summary>
    public object? At(JavaToken token) => token.Kind == TokenKind.Identifier ? JavaSymbols.Find(typer, Builder, token)?.Symbol : null;

    /// <summary>Finds the constructor a <c>new</c> calls, when the identifier is the name of the class it creates.</summary>
    public MemberRef<MethodSymbol>? ConstructorCalledAt(JavaToken token)
    {
        var scope = Builder.ScopeAt(token.Span.Start, out var path);
        return path.Count >= 3 && path[^2] is ClassTypeSyntax type && path[^3] is ObjectCreationExpressionSyntax creation && creation.Type == type
            ? typer.ResolveCreation(creation, scope).Chosen
            : null;
    }

    /// <summary>Gets whether an identifier names a type variable, where it is declared or where it is used.</summary>
    public bool IsTypeParameterAt(JavaToken token)
    {
        var scope = Builder.ScopeAt(token.Span.Start, out var path);
        if (path.Count < 2 || path[^1] is not IdentifierSyntax name)
            return false;
        return path[^2] is TypeParameterSyntax || (path[^2] is ClassTypeSyntax { Qualifier: null } && scope.FindTypeParameter(name.Text) is not null);
    }
}

/// <summary>One place a symbol is named.</summary>
/// <param name="IsDeclaration">Whether the name is where the symbol is declared.</param>
/// <param name="IsWrite">Whether the place gives the symbol a value.</param>
internal readonly record struct Occurrence(SemanticModel Model, SourceSpan Span, bool IsDeclaration, bool IsWrite);

/// <summary>Find References, the occurrences of the symbol under the caret and Go to Implementation, for Java sources.</summary>
internal static class JavaReferences
{
    /// <summary>Finds every use of the symbol at an offset in the sources of the open folder's projects.</summary>
    public static IReadOnlyList<DocumentLocation> Find(SemanticModel model, IReadOnlyList<JavaProjectModel> projects, int offset, bool includeDeclaration,
        CancellationToken cancellationToken)
    {
        var resolver = new SymbolResolver(model);
        if (resolver.IdentifierAt(offset) is not { } token || resolver.At(token) is not { } symbol)
            return [];

        return Locations(Occurrences(resolver, projects, symbol, onlyThisFile: false, cancellationToken).Where(o => includeDeclaration || !o.IsDeclaration));
    }

    /// <summary>Finds the occurrences of the symbol at an offset in its own file, with whether each one writes it.</summary>
    public static IReadOnlyList<DocumentHighlight> Highlights(SemanticModel model, int offset, CancellationToken cancellationToken)
    {
        var resolver = new SymbolResolver(model);
        if (resolver.IdentifierAt(offset) is not { } token || resolver.At(token) is not { } symbol)
            return [];

        return
        [
            .. Occurrences(resolver, [], symbol, onlyThisFile: true, cancellationToken)
                .OrderBy(o => o.Span.Start)
                .Select(o => new DocumentHighlight(new TextSpan(o.Span.Start, o.Span.Length), o.IsWrite ? DocumentHighlightKind.Write : DocumentHighlightKind.Read)),
        ];
    }

    /// <summary>Finds what implements or overrides the symbol at an offset: the types that extend or implement a type, and the methods that
    /// override a method.</summary>
    public static IReadOnlyList<DocumentLocation> Implementations(SemanticModel model, IReadOnlyList<JavaProjectModel> projects, int offset,
        CancellationToken cancellationToken)
    {
        var resolver = new SymbolResolver(model);
        if (resolver.IdentifierAt(offset) is not { } token || resolver.At(token) is not { } symbol)
            return [];

        var found = new List<DocumentLocation>();
        var isAbstract = false;
        switch (symbol)
        {
            case TypeName type:
                isAbstract = model.FindClass(type.BinaryName) is { } declared && (declared.Kind == ClassKind.Interface || (declared.Modifiers & JavaModifiers.Abstract) != 0);
                foreach (var (subtype, _) in JavaTypeIndex.Subtypes(model, projects, type.BinaryName, transitive: true, cancellationToken))
                {
                    if (subtype.Declaration is { } declaration)
                        found.Add(Location(subtype.File.Path, subtype.File.Tree, declaration.Name.Span));
                }

                break;
            case MemberRef<MethodSymbol> { Member: { IsConstructor: false, IsStatic: false } } method:
                isAbstract = method.Member.IsAbstract;
                foreach (var (subtype, snapshot) in JavaTypeIndex.Subtypes(model, projects, method.Owner.BinaryName, transitive: true, cancellationToken))
                {
                    if (snapshot.FindClass(subtype.BinaryName) is { } symbolOfSubtype
                        && JavaTypeIndex.OverrideIn(snapshot.ModelFor(subtype.File), symbolOfSubtype, method) is { } overriding
                        && subtype.DeclarationOf(overriding.Member) is MethodDeclarationSyntax declaration)
                    {
                        found.Add(Location(subtype.File.Path, subtype.File.Tree, declaration.Name.Span));
                    }
                }

                break;
        }

        // A symbol that nothing implements, and that is not abstract itself, is its own implementation.
        if (found.Count == 0 && !isAbstract && JavaSymbols.Source(model, symbol) is var (path, tree, _, name))
            found.Add(Location(path, tree, name));
        return Sorted(found);
    }

    /// <summary>Finds the places that name a symbol: in the symbol's own file, or in every source file of the folder's projects.</summary>
    public static IEnumerable<Occurrence> Occurrences(SymbolResolver current, IReadOnlyList<JavaProjectModel> projects, object symbol, bool onlyThisFile,
        CancellationToken cancellationToken)
    {
        if (JavaSymbolKeys.Of(symbol) is not { } key || JavaSymbolKeys.NameOf(symbol) is not { Length: > 0 } name)
            yield break;

        var matcher = new SymbolMatcher(symbol, key);
        var models = onlyThisFile || symbol is LocalSymbol ? [current.Model] : JavaTypeIndex.Models(current.Model, projects, name);
        foreach (var model in models)
        {
            var resolver = model == current.Model ? current : new SymbolResolver(model);
            var tree = model.Tree;
            foreach (var token in tree.Tokens)
            {
                if (token.Kind != TokenKind.Identifier || token.Span.Length != name.Length || !tree.Text.AsSpan(token.Span.Start, name.Length).SequenceEqual(name))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();
                var candidate = matcher.WantsConstructor ? resolver.ConstructorCalledAt(token) ?? resolver.At(token) : resolver.At(token);
                if (candidate is null || !matcher.Matches(model, candidate))
                    continue;

                var path = tree.GetPath(token.Span.Start);
                var declaration = IsDeclaration(path);
                yield return new Occurrence(model, token.Span, declaration, declaration || IsWritten(path));
            }
        }
    }

    public static DocumentLocation Location(string path, JavaSyntaxTree tree, SourceSpan span)
    {
        var lines = new LineMap(tree.Text);
        return new DocumentLocation(path, lines.PositionOf(span.Start), lines.PositionOf(span.End));
    }

    public static IReadOnlyList<DocumentLocation> Sorted(IEnumerable<DocumentLocation> locations) =>
    [
        .. locations.Distinct()
            .OrderBy(l => l.FilePath, StringComparer.Ordinal)
            .ThenBy(l => l.Start.Line)
            .ThenBy(l => l.Start.Column),
    ];

    private static IReadOnlyList<DocumentLocation> Locations(IEnumerable<Occurrence> occurrences)
    {
        var found = new List<DocumentLocation>();
        foreach (var file in occurrences.GroupBy(o => o.Model))
        {
            var lines = new LineMap(file.Key.Tree.Text);
            found.AddRange(file.Select(o => new DocumentLocation(file.Key.File.Path, lines.PositionOf(o.Span.Start), lines.PositionOf(o.Span.End))));
        }

        return Sorted(found);
    }

    // The name of a declaration is the identifier child of the node that declares it.
    private static bool IsDeclaration(IReadOnlyList<SyntaxNode> path) => path.Count >= 2 && path[^2] is TypeDeclarationSyntax or MethodDeclarationSyntax
        or ConstructorDeclarationSyntax or CompactConstructorDeclarationSyntax or VariableDeclaratorSyntax or EnumConstantSyntax or RecordComponentSyntax
        or ParameterSyntax or CatchClauseSyntax or TypePatternSyntax or ForEachStatementSyntax or TypeParameterSyntax;

    private static bool IsWritten(IReadOnlyList<SyntaxNode> path)
    {
        if (path.Count < 3 || path[^2] is not (NameExpressionSyntax or FieldAccessExpressionSyntax))
            return false;

        return path[^3] switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == path[^2],
            UnaryExpressionSyntax unary => unary.Operator is UnaryOperator.PreIncrement or UnaryOperator.PreDecrement or UnaryOperator.PostIncrement
                or UnaryOperator.PostDecrement,
            _ => false,
        };
    }
}

/// <summary>Names symbols so they can be compared across files and across versions of a project.</summary>
internal static class JavaSymbolKeys
{
    public static string? Of(object symbol) => symbol switch
    {
        TypeName type => "T:" + type.BinaryName,
        MemberRef<FieldSymbol> field => $"F:{field.Owner.BinaryName}#{field.Member.Name}",
        MemberRef<MethodSymbol> method => $"M:{method.Owner.BinaryName}#{Signature(method.Member)}",
        LocalSymbol local => $"L:{local.NameSpan.Start}",
        PackageName package => "P:" + package.Name,
        _ => null,
    };

    /// <summary>Gets the identifier source uses for a symbol; a constructor goes by its class's name.</summary>
    public static string? NameOf(object symbol) => symbol switch
    {
        TypeName type => ClassNames.SimpleName(type.BinaryName),
        MemberRef<FieldSymbol> field => field.Member.Name,
        MemberRef<MethodSymbol> { Member.IsConstructor: true } constructor => constructor.Owner.SimpleName,
        MemberRef<MethodSymbol> method => method.Member.Name,
        LocalSymbol local => local.Name,
        PackageName package => package.Name[(package.Name.LastIndexOf('.') + 1)..],
        _ => null,
    };

    public static string Signature(MethodSymbol method) =>
        $"{method.Name}({string.Join(',', method.ParameterTypes.Select(p => JavaTypes.Erase(p).ToJavaString()))})";
}

/// <summary>Decides whether a name stands for the symbol that is searched for: the symbol itself, or a method it overrides or that
/// overrides it.</summary>
internal sealed class SymbolMatcher(object target, string key)
{
    private readonly Dictionary<string, bool> related = new(StringComparer.Ordinal);

    public bool WantsConstructor => target is MemberRef<MethodSymbol> { Member.IsConstructor: true };

    public bool Matches(SemanticModel model, object candidate)
    {
        if (JavaSymbolKeys.Of(candidate) is not { } candidateKey)
            return false;
        if (candidateKey == key)
            return true;
        if (target is not MemberRef<MethodSymbol> { Member: { IsConstructor: false, IsStatic: false } } wanted
            || candidate is not MemberRef<MethodSymbol> { Member: { IsConstructor: false, IsStatic: false } } found
            || wanted.Member.Name != found.Member.Name || wanted.Member.ParameterTypes.Count != found.Member.ParameterTypes.Count)
        {
            return false;
        }

        if (!related.TryGetValue(candidateKey, out var known))
            related[candidateKey] = known = Overrides(model, found, wanted) || Overrides(model, wanted, found);
        return known;
    }

    private static bool Overrides(SemanticModel model, MemberRef<MethodSymbol> lower, MemberRef<MethodSymbol> upper) =>
        JavaTypeIndex.OverrideIn(model, lower.Owner, upper) is { } overriding && JavaSymbolKeys.Signature(overriding.Member) == JavaSymbolKeys.Signature(lower.Member);
}

/// <summary>The source types of the open folder's projects: which files to search, which types extend a type, and which method overrides
/// another.</summary>
internal static class JavaTypeIndex
{
    /// <summary>Gets the snapshot of each project to search: the one a request works on, and the others as they are now.</summary>
    public static IEnumerable<ProjectSnapshot> Snapshots(SemanticModel current, IReadOnlyList<JavaProjectModel> projects)
    {
        yield return current.Project;
        foreach (var project in projects)
        {
            if (project != current.Project.Model)
                yield return project.Snapshot;
        }
    }

    /// <summary>Gets the model of each source file whose text has a word; the file a request is about comes first, as the request has it.</summary>
    public static IEnumerable<SemanticModel> Models(SemanticModel current, IReadOnlyList<JavaProjectModel> projects, string word)
    {
        yield return current;
        foreach (var snapshot in Snapshots(current, projects))
        {
            foreach (var file in snapshot.Sources.Files)
            {
                if (!SourceLibrary.PathComparer.Equals(file.Path, current.File.Path) && file.Tree.Text.Contains(word, StringComparison.Ordinal))
                    yield return snapshot.ModelFor(file);
            }
        }
    }

    /// <summary>Finds the source types that extend or implement a type: only those that name it, or also the ones below them.</summary>
    public static IReadOnlyList<(SourceType Type, ProjectSnapshot Project)> Subtypes(SemanticModel current, IReadOnlyList<JavaProjectModel> projects,
        string binaryName, bool transitive, CancellationToken cancellationToken)
    {
        var snapshots = Snapshots(current, projects).ToList();
        var found = new List<(SourceType, ProjectSnapshot)>();
        var seen = new HashSet<string>(StringComparer.Ordinal) { binaryName };
        var pending = new Queue<string>([binaryName]);
        while (pending.TryDequeue(out var parent))
        {
            var simpleName = ClassNames.SimpleName(parent);
            foreach (var snapshot in snapshots)
            {
                foreach (var file in snapshot.Sources.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!file.Tree.Text.Contains(simpleName, StringComparison.Ordinal))
                        continue;

                    foreach (var type in file.Types)
                    {
                        if (type.Declaration is null || !NamesSupertype(type.Declaration, simpleName) || seen.Contains(type.BinaryName)
                            || snapshot.FindClass(type.BinaryName) is not { } symbol
                            || (symbol.SuperClass?.BinaryName != parent && !symbol.Interfaces.Any(i => i.BinaryName == parent)))
                        {
                            continue;
                        }

                        seen.Add(type.BinaryName);
                        found.Add((type, snapshot));
                        if (transitive)
                            pending.Enqueue(type.BinaryName);
                    }
                }
            }
        }

        return found;
    }

    /// <summary>Gets the method a type declares that overrides a method of one of its supertypes, or null.</summary>
    public static MemberRef<MethodSymbol>? OverrideIn(SemanticModel model, ClassSymbol type, MemberRef<MethodSymbol> method)
    {
        if (type.BinaryName == method.Owner.BinaryName)
            return null;

        var hierarchy = model.Hierarchy(SemanticModel.SelfType(type));
        if (hierarchy.Count == 0 || hierarchy.FirstOrDefault(e => e.Symbol.BinaryName == method.Owner.BinaryName) is not { } inherited)
            return null;

        var wanted = SemanticModel.OverrideKey(new MemberRef<MethodSymbol>(method.Member, inherited.Symbol, inherited.Map, inherited.IsExact));
        var self = hierarchy[0];
        foreach (var own in self.Symbol.Methods)
        {
            if (own.IsConstructor || own.IsStatic || own.Name != method.Member.Name)
                continue;

            var reference = new MemberRef<MethodSymbol>(own, self.Symbol, self.Map, self.IsExact);
            if (SemanticModel.OverrideKey(reference) == wanted)
                return reference;
        }

        return null;
    }

    private static bool NamesSupertype(TypeDeclarationSyntax declaration, string simpleName)
    {
        IEnumerable<TypeSyntax> named = declaration switch
        {
            ClassDeclarationSyntax type => type.Extends is { } extended ? type.Implements.Prepend(extended) : type.Implements,
            InterfaceDeclarationSyntax type => type.Extends,
            EnumDeclarationSyntax type => type.Implements,
            RecordDeclarationSyntax type => type.Implements,
            _ => [],
        };
        return named.Any(type => type is ClassTypeSyntax supertype && supertype.Name.Text == simpleName);
    }
}
