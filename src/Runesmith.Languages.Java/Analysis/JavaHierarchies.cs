using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Semantics;
using Runesmith.Languages.Java.Syntax;
using Runesmith.Sdk.Languages;
using JavaModifiers = Runesmith.Languages.Java.ClassFiles.JavaModifiers;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>Where a hierarchy item's symbol is declared, so it can be found again in the project as it is when its calls or types are asked
/// for.</summary>
internal sealed record JavaAnchor(string Path, int Offset);

/// <summary>Call hierarchies of methods and constructors, and type hierarchies of types, for Java sources.</summary>
internal static class JavaHierarchies
{
    /// <summary>Gets the method or constructor at an offset as a hierarchy item, or nothing; on the name of the class a <c>new</c> creates, the
    /// constructor it calls.</summary>
    public static IReadOnlyList<HierarchyItem> PrepareCalls(SemanticModel model, int offset)
    {
        var resolver = new SymbolResolver(model);
        if (resolver.IdentifierAt(offset) is not { } token
            || (resolver.ConstructorCalledAt(token) ?? resolver.At(token)) is not MemberRef<MethodSymbol> method
            || JavaSymbols.Source(model, method) is not var (path, tree, node, _))
        {
            return [];
        }

        return Item(path, tree, node) is { } item ? [item] : [];
    }

    /// <summary>Gets the methods that call the method declared at an offset, each with the places in it that call.</summary>
    public static IReadOnlyList<HierarchyCall> IncomingCalls(SemanticModel model, IReadOnlyList<JavaProjectModel> projects, int offset,
        CancellationToken cancellationToken)
    {
        var resolver = new SymbolResolver(model);
        if (resolver.IdentifierAt(offset) is not { } token || resolver.At(token) is not MemberRef<MethodSymbol> method)
            return [];

        var callers = new Dictionary<(string Path, int Start), (HierarchyItem Item, List<DocumentLocation> Sites)>();
        foreach (var occurrence in JavaReferences.Occurrences(resolver, projects, method, onlyThisFile: false, cancellationToken))
        {
            if (occurrence.IsDeclaration)
                continue;

            var tree = occurrence.Model.Tree;
            var file = occurrence.Model.File.Path;
            var owner = tree.GetPath(occurrence.Span.Start).LastOrDefault(n => n is MethodDeclarationSyntax or ConstructorDeclarationSyntax
                or CompactConstructorDeclarationSyntax or TypeDeclarationSyntax);
            if (owner is null)
                continue;

            if (!callers.TryGetValue((file, owner.Start), out var caller))
            {
                if (Item(file, tree, owner) is not { } item)
                    continue;
                callers[(file, owner.Start)] = caller = (item, []);
            }

            caller.Sites.Add(JavaReferences.Location(file, tree, occurrence.Span));
        }

        return Sorted(callers.Values.Select(c => new HierarchyCall(c.Item, JavaReferences.Sorted(c.Sites))));
    }

    /// <summary>Gets the methods and constructors of the project's sources that the method declared at an offset calls, each with the places
    /// that call it.</summary>
    public static IReadOnlyList<HierarchyCall> OutgoingCalls(SemanticModel model, int offset, CancellationToken cancellationToken)
    {
        var tree = model.Tree;
        var declaration = tree.GetPath(offset).LastOrDefault(n => n is MethodDeclarationSyntax or ConstructorDeclarationSyntax or CompactConstructorDeclarationSyntax);
        if (declaration is null)
            return [];

        var resolver = new SymbolResolver(model);
        var called = new Dictionary<(string Path, int Start), (HierarchyItem Item, List<DocumentLocation> Sites)>();
        var pending = new Stack<SyntaxNode>([declaration]);
        while (pending.TryPop(out var node))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = node switch
            {
                MethodInvocationExpressionSyntax call => call.Name,
                ObjectCreationExpressionSyntax { Type: ClassTypeSyntax created } => created.Name,
                _ => null,
            };
            if (name is { IsMissing: false } && tree.FindToken(name.Start) is { Kind: TokenKind.Identifier } token
                && (node is ObjectCreationExpressionSyntax ? resolver.ConstructorCalledAt(token) : resolver.At(token)) is MemberRef<MethodSymbol> method
                && (JavaSymbols.Source(model, method) ?? (method.Member.IsConstructor ? JavaSymbols.Source(model, new TypeName(method.Owner.BinaryName)) : null))
                    is var (path, targetTree, target, _))
            {
                if (!called.TryGetValue((path, target.Start), out var callee) && Item(path, targetTree, target) is { } item)
                    called[(path, target.Start)] = callee = (item, []);
                callee.Sites?.Add(JavaReferences.Location(model.File.Path, tree, token.Span));
            }

            // What a class declared inside the method calls is not what the method calls.
            foreach (var child in node.ChildNodes())
            {
                if (child is not (TypeDeclarationSyntax or ClassBodySyntax))
                    pending.Push(child);
            }
        }

        return Sorted(called.Values.Select(c => new HierarchyCall(c.Item, JavaReferences.Sorted(c.Sites))));
    }

    /// <summary>Gets the type at an offset as a hierarchy item, or nothing.</summary>
    public static IReadOnlyList<HierarchyItem> PrepareTypes(SemanticModel model, int offset)
    {
        var resolver = new SymbolResolver(model);
        return resolver.IdentifierAt(offset) is { } token && resolver.At(token) is TypeName type && TypeItem(model, type.BinaryName) is { } item ? [item] : [];
    }

    /// <summary>Gets the class a type extends and the interfaces it names, when they are declared in source.</summary>
    public static IReadOnlyList<HierarchyItem> Supertypes(SemanticModel model, int offset)
    {
        var resolver = new SymbolResolver(model);
        if (resolver.IdentifierAt(offset) is not { } token || resolver.At(token) is not TypeName type || model.FindClass(type.BinaryName) is not { } symbol)
            return [];

        IEnumerable<ClassTypeReference> named = symbol.SuperClass is { } extended ? symbol.Interfaces.Prepend(extended) : symbol.Interfaces;
        return [.. named.Select(n => TypeItem(model, n.BinaryName)).OfType<HierarchyItem>().OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Gets the source types that extend or implement a type directly.</summary>
    public static IReadOnlyList<HierarchyItem> Subtypes(SemanticModel model, IReadOnlyList<JavaProjectModel> projects, int offset, CancellationToken cancellationToken)
    {
        var resolver = new SymbolResolver(model);
        if (resolver.IdentifierAt(offset) is not { } token || resolver.At(token) is not TypeName type)
            return [];

        return
        [
            .. JavaTypeIndex.Subtypes(model, projects, type.BinaryName, transitive: false, cancellationToken)
                .Select(s => s.Type.Declaration is { } declaration ? Item(s.Type.File.Path, s.Type.File.Tree, declaration) : null)
                .OfType<HierarchyItem>()
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
        ];
    }

    /// <summary>Makes a hierarchy item of a method, constructor or type declaration.</summary>
    internal static HierarchyItem? Item(string path, JavaSyntaxTree tree, SyntaxNode declaration)
    {
        var (name, kind, label) = declaration switch
        {
            MethodDeclarationSyntax method => (method.Name, SymbolKind.Method, method.Name.Text + JavaStructure.Signature(method.Parameters, null, tree.Text)),
            ConstructorDeclarationSyntax constructor => (constructor.Name, SymbolKind.Constructor,
                constructor.Name.Text + JavaStructure.Signature(constructor.Parameters, null, tree.Text)),
            CompactConstructorDeclarationSyntax constructor => (constructor.Name, SymbolKind.Constructor, constructor.Name.Text),
            TypeDeclarationSyntax type => (type.Name, JavaStructure.KindOf(type), type.Name.Text),
            _ => (null, default, ""),
        };
        if (name is null || name.IsMissing)
            return null;

        var containers = tree.GetPath(declaration.Start).OfType<TypeDeclarationSyntax>().Where(t => t != declaration).Select(t => t.Name.Text);
        var container = string.Join('.', tree.Root.Package is { } package ? containers.Prepend(package.Name.ToString()) : containers);
        return new HierarchyItem(label, kind, JavaReferences.Location(path, tree, declaration.Span))
        {
            Detail = container.Length > 0 ? container : null,
            SelectionLocation = JavaReferences.Location(path, tree, name.Span),
            Data = new JavaAnchor(path, name.Start),
        };
    }

    private static HierarchyItem? TypeItem(SemanticModel model, string binaryName) =>
        JavaSymbols.Source(model, new TypeName(binaryName)) is var (path, tree, node, _) ? Item(path, tree, node) : null;

    private static IReadOnlyList<HierarchyCall> Sorted(IEnumerable<HierarchyCall> calls) =>
        [.. calls.OrderBy(c => c.Item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Item.Location.FilePath, StringComparer.Ordinal)];
}

/// <summary>The names in a Java file classified by what they mean: types by their kind, fields, methods, parameters, locals, type variables
/// and packages.</summary>
internal static class JavaSemanticTokens
{
    /// <summary>Gets the tokens of a file, or of the part of it between two offsets, in the order of the text.</summary>
    public static IReadOnlyList<SemanticToken> Get(SemanticModel model, int start, int end, CancellationToken cancellationToken)
    {
        var tree = model.Tree;
        var resolver = new SymbolResolver(model);
        var tokens = new List<SemanticToken>();
        for (var i = tree.FindTokenIndex(Math.Max(0, start)); i < tree.Tokens.Length && tree.Tokens[i].Span.Start < end; i++)
        {
            var token = tree.Tokens[i];
            if (token.Kind != TokenKind.Identifier || token.Span.Start < start)
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            if (Classify(resolver, token) is var (type, modifiers))
                tokens.Add(new SemanticToken(new Runesmith.Text.TextSpan(token.Span.Start, token.Span.Length), type, modifiers));
        }

        return tokens;
    }

    private static (string Type, SemanticTokenModifiers Modifiers)? Classify(SymbolResolver resolver, JavaToken token)
    {
        switch (resolver.At(token))
        {
            case TypeName type:
                var symbol = resolver.Model.FindClass(type.BinaryName);
                var kind = symbol?.Kind switch
                {
                    ClassKind.Interface => SemanticTokenTypes.Interface,
                    ClassKind.Enum => SemanticTokenTypes.Enum,
                    ClassKind.Annotation => SemanticTokenTypes.Decorator,
                    ClassKind.Record => SemanticTokenTypes.Struct,
                    _ => SemanticTokenTypes.Class,
                };
                return (kind, Flags(symbol?.IsDeprecated ?? false, false, false, symbol is { Kind: ClassKind.Class } && (symbol.Modifiers & JavaModifiers.Abstract) != 0));
            case MemberRef<FieldSymbol> { Member.IsEnumConstant: true } constant:
                return (SemanticTokenTypes.EnumMember, Flags(constant.Member.IsDeprecated, true, true, false));
            case MemberRef<FieldSymbol> field:
                return (SemanticTokenTypes.Property, Flags(field.Member.IsDeprecated, field.Member.IsStatic, (field.Member.Modifiers & JavaModifiers.Final) != 0, false));
            case MemberRef<MethodSymbol> method:
                return (SemanticTokenTypes.Method, Flags(method.Member.IsDeprecated, method.Member.IsStatic, false, method.Member.IsAbstract));
            case LocalSymbol { Kind: LocalKind.Parameter or LocalKind.LambdaParameter or LocalKind.CatchParameter }:
                return (SemanticTokenTypes.Parameter, SemanticTokenModifiers.None);
            case LocalSymbol:
                return (SemanticTokenTypes.Variable, SemanticTokenModifiers.None);
            case PackageName:
                return (SemanticTokenTypes.Namespace, SemanticTokenModifiers.None);
            default:
                return resolver.IsTypeParameterAt(token) ? (SemanticTokenTypes.TypeParameter, SemanticTokenModifiers.None) : null;
        }
    }

    private static SemanticTokenModifiers Flags(bool deprecated, bool isStatic, bool isFinal, bool isAbstract) =>
        (deprecated ? SemanticTokenModifiers.Deprecated : 0) | (isStatic ? SemanticTokenModifiers.Static : 0) | (isFinal ? SemanticTokenModifiers.Readonly : 0)
        | (isAbstract ? SemanticTokenModifiers.Abstract : 0);
}

/// <summary>The types, methods and fields of the folder's sources by name, for Go to Symbol in Workspace.</summary>
internal static class JavaWorkspaceSymbols
{
    private const int MaxSymbols = 500;

    /// <summary>Finds the declarations whose names contain a query, whatever its case, the closest names first.</summary>
    public static IReadOnlyList<WorkspaceSymbol> Find(IEnumerable<JavaProjectModel> projects, string query, CancellationToken cancellationToken)
    {
        query = query.Trim();
        if (query.Length == 0)
            return [];

        var found = new List<(WorkspaceSymbol Symbol, int Rank)>();
        var seen = new HashSet<string>(SourceLibrary.PathComparer);
        foreach (var file in projects.SelectMany(p => p.Snapshot.Sources.Files))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(file.Path) || !file.Tree.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            LineMap? lines = null;
            void Consider(IdentifierSyntax name, SymbolKind kind, string container, bool deprecated)
            {
                var index = name.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    return;

                lines ??= new LineMap(file.Tree.Text);
                var rank = name.Text.Length == query.Length ? 0 : index == 0 ? 1 : 2;
                found.Add((new WorkspaceSymbol(name.Text, kind, new DocumentLocation(file.Path, lines.PositionOf(name.Start), lines.PositionOf(name.End)))
                {
                    ContainerName = container.Length > 0 ? container : null,
                    IsDeprecated = deprecated,
                }, rank));
            }

            foreach (var type in file.Types)
            {
                if (type.Declaration is not { } declaration)
                    continue;

                var own = ClassNames.SourceName(type.BinaryName);
                Consider(declaration.Name, JavaStructure.KindOf(declaration), type.Outer is { } outer ? ClassNames.SourceName(outer.BinaryName) : file.PackageName,
                    JavaStructure.IsDeprecated(declaration.Modifiers));
                foreach (var constant in declaration.Body.EnumConstants)
                    Consider(constant.Name, SymbolKind.EnumMember, own, JavaStructure.IsDeprecated(constant.Modifiers));
                foreach (var member in declaration.Body.Members)
                {
                    var deprecated = JavaStructure.IsDeprecated(member.Modifiers);
                    if (member is MethodDeclarationSyntax method)
                    {
                        Consider(method.Name, SymbolKind.Method, own, deprecated);
                    }
                    else if (member is FieldDeclarationSyntax field)
                    {
                        foreach (var variable in field.Variables)
                            Consider(variable.Name, SymbolKind.Field, own, deprecated);
                    }
                }
            }
        }

        return
        [
            .. found.OrderBy(f => f.Rank)
                .ThenBy(f => f.Symbol.Name.Length)
                .ThenBy(f => f.Symbol.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Symbol.ContainerName, StringComparer.Ordinal)
                .Take(MaxSymbols)
                .Select(f => f.Symbol),
        ];
    }
}
