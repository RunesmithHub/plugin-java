using Runesmith.Languages.Java.Syntax;
using Runesmith.Sdk.Languages;
using Runesmith.Text;
using JavaModifiers = Runesmith.Languages.Java.Syntax.JavaModifiers;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>Turns offsets in a text into lines and columns, with the line starts found once.</summary>
internal sealed class LineMap
{
    private readonly List<int> starts = [0];

    public LineMap(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                starts.Add(i + 1);
        }
    }

    public int LineOf(int offset)
    {
        var index = starts.BinarySearch(offset);
        return index >= 0 ? index : ~index - 1;
    }

    public TextPosition PositionOf(int offset)
    {
        var line = LineOf(offset);
        return new TextPosition(line, offset - starts[line]);
    }

    public int EndOfLine(int line, int textLength) => line + 1 < starts.Count ? starts[line + 1] - 1 : textLength;
}

/// <summary>What a Java file's syntax alone tells: its outline, the lines that fold and the spans Expand Selection grows through.</summary>
internal static class JavaStructure
{
    /// <summary>Gets the types and members a file declares, each inside the type that contains it.</summary>
    public static IReadOnlyList<DocumentSymbol> Symbols(JavaSyntaxTree tree, CancellationToken cancellationToken) =>
        Members(tree.Text, tree.Root.Members, [], inInterface: false, cancellationToken);

    /// <summary>Gets the ranges that fold: types, members and blocks that span lines, the imports and comments of several lines.</summary>
    public static IReadOnlyList<FoldingRange> Folding(JavaSyntaxTree tree, CancellationToken cancellationToken)
    {
        var text = tree.Text;
        var lines = new LineMap(text);
        var ranges = new List<FoldingRange>();
        void Add(int start, int end, FoldingRangeKind kind)
        {
            var (first, last) = (lines.LineOf(start), lines.LineOf(Math.Max(start, end - 1)));
            // A line that goes on after the range, such as "} else {", stays visible.
            if (kind == FoldingRangeKind.Block && ContinuesAfter(text, end, lines.EndOfLine(last, text.Length)))
                last--;
            if (last > first)
                ranges.Add(new FoldingRange(first, last) { Kind = kind });
        }

        if (tree.Root.Imports is { Count: > 1 } imports)
            Add(imports[0].Start, imports[^1].End, FoldingRangeKind.Imports);

        foreach (var node in tree.Root.DescendantNodesAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (node)
            {
                case TypeDeclarationSyntax type:
                    Add(type.Name.Start, type.End, FoldingRangeKind.Block);
                    break;
                case MethodDeclarationSyntax { Body: not null } method:
                    Add(method.Name.Start, method.End, FoldingRangeKind.Block);
                    break;
                case ConstructorDeclarationSyntax constructor:
                    Add(constructor.Name.Start, constructor.End, FoldingRangeKind.Block);
                    break;
                case CompactConstructorDeclarationSyntax constructor:
                    Add(constructor.Name.Start, constructor.End, FoldingRangeKind.Block);
                    break;
                case EnumConstantSyntax { Body: not null } constant:
                    Add(constant.Name.Start, constant.End, FoldingRangeKind.Block);
                    break;
                case InitializerDeclarationSyntax or LambdaExpressionSyntax or ArrayInitializerExpressionSyntax or SwitchStatementSyntax
                    or SwitchExpressionSyntax or IfStatementSyntax { Else: null } or WhileStatementSyntax or DoStatementSyntax or ForStatementSyntax
                    or ForEachStatementSyntax or SynchronizedStatementSyntax or CatchClauseSyntax or ObjectCreationExpressionSyntax { Body: not null }:
                    Add(node.Start, node.End, FoldingRangeKind.Block);
                    break;
                case IfStatementSyntax { Else: { } otherwise } branch:
                    Add(branch.Start, branch.Then.End, FoldingRangeKind.Block);
                    if (otherwise is not IfStatementSyntax)
                        Add(ElseStart(text, branch.Then.End, otherwise.Start), otherwise.End, FoldingRangeKind.Block);
                    break;
                case TryStatementSyntax attempt:
                    Add(attempt.Start, attempt.Body.End, FoldingRangeKind.Block);
                    if (attempt.Finally is { } last)
                        Add(ElseStart(text, attempt.Catches.Count > 0 ? attempt.Catches[^1].End : attempt.Body.End, last.Start), last.End, FoldingRangeKind.Block);
                    break;
                case SwitchCaseSyntax { IsArrow: false, Statements.Count: > 0 } section:
                    Add(section.Start, section.End, FoldingRangeKind.Block);
                    break;
            }
        }

        AddComments(tree, lines, ranges);
        return [.. ranges.DistinctBy(r => (r.StartLine, r.EndLine)).OrderBy(r => r.StartLine).ThenByDescending(r => r.EndLine)];
    }

    /// <summary>Gets the spans around an offset from the smallest to the largest: the token, then each syntax node around it.</summary>
    public static IReadOnlyList<TextSpan> SelectionRanges(JavaSyntaxTree tree, int offset)
    {
        if (tree.Text.Length == 0)
            return [];

        var spans = new List<TextSpan>();
        void Add(SourceSpan span)
        {
            if (span.Length > 0 && (spans.Count == 0 || (span.Length > spans[^1].Length && span.Start <= spans[^1].Start && span.End >= spans[^1].End)))
                spans.Add(new TextSpan(span.Start, span.Length));
        }

        // Right after a word, the word is what the caret is on, not the punctuation that follows it.
        var token = tree.FindToken(offset);
        if (!token.Span.ContainsInclusive(offset) || (token.Span.Start == offset && tree.FindTokenBefore(offset) is { Kind: TokenKind.Identifier } word && word.Span.End == offset))
            token = tree.FindTokenBefore(offset) ?? token;
        if (token.Kind != TokenKind.EndOfFile && token.Span.ContainsInclusive(offset))
            Add(token.Span);

        var path = tree.GetPath(Math.Clamp(token.Span.ContainsInclusive(offset) ? token.Span.Start : offset, 0, tree.Text.Length - 1));
        for (var i = path.Count - 1; i >= 0; i--)
        {
            // The inside of a block or an argument list comes before the whole of it.
            if (path[i] is BlockSyntax { Statements.Count: > 0 } block)
                Add(SourceSpan.FromBounds(block.Statements[0].Start, block.Statements[^1].End));
            else if (path[i] is ArgumentListSyntax { Arguments.Count: > 0 } arguments)
                Add(SourceSpan.FromBounds(arguments.Arguments[0].Start, arguments.Arguments[^1].End));
            else if (path[i] is ClassBodySyntax { Members.Count: > 0 } body)
                Add(SourceSpan.FromBounds(body.EnumConstants.Count > 0 ? body.EnumConstants[0].Start : body.Members[0].Start, body.Members[^1].End));
            Add(path[i].Span);
        }

        Add(new SourceSpan(0, tree.Text.Length));
        return spans;
    }

    /// <summary>Gets the kind of symbol a type declaration is.</summary>
    public static SymbolKind KindOf(TypeDeclarationSyntax type) => type switch
    {
        InterfaceDeclarationSyntax or AnnotationTypeDeclarationSyntax => SymbolKind.Interface,
        EnumDeclarationSyntax => SymbolKind.Enum,
        RecordDeclarationSyntax => SymbolKind.Struct,
        _ => SymbolKind.Class,
    };

    /// <summary>Gets a method's parameter types and return type as the outline shows them, such as <c>(String, int) : boolean</c>.</summary>
    public static string Signature(ParameterListSyntax parameters, TypeSyntax? returns, string text)
    {
        var types = string.Join(", ", parameters.Parameters.Where(p => !p.IsReceiver).Select(p => p.Type is { } type ? Text(type, text) + (IsVarargs(p, text) ? "..." : "") : p.Name.Text));
        return returns is null ? $"({types})" : $"({types}) : {Text(returns, text)}";
    }

    public static bool IsDeprecated(ModifiersSyntax modifiers) =>
        modifiers.Annotations.Any(annotation => annotation.Name.ToString() is "Deprecated" or "java.lang.Deprecated");

    private static List<DocumentSymbol> Members(string text, IEnumerable<MemberDeclarationSyntax> members, IEnumerable<EnumConstantSyntax> constants,
        bool inInterface, CancellationToken cancellationToken)
    {
        var symbols = new List<DocumentSymbol>();
        foreach (var constant in constants)
        {
            symbols.Add(Symbol(constant, constant.Name, SymbolKind.EnumMember, null, IsDeprecated(constant.Modifiers),
                constant.Body is { } body ? Members(text, body.Members, body.EnumConstants, false, cancellationToken) : []));
        }

        foreach (var member in members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var deprecated = IsDeprecated(member.Modifiers);
            switch (member)
            {
                case TypeDeclarationSyntax type:
                    var children = Members(text, type.Body.Members, type.Body.EnumConstants, type is InterfaceDeclarationSyntax or AnnotationTypeDeclarationSyntax,
                        cancellationToken);
                    if (type is RecordDeclarationSyntax record)
                    {
                        children.InsertRange(0, record.Header.Components.Select(c =>
                            Symbol(c, c.Name, SymbolKind.Field, Text(c.Type, text), IsDeprecated(c.Modifiers), [])));
                    }

                    symbols.Add(Symbol(type, type.Name, KindOf(type), null, deprecated, children));
                    break;
                case MethodDeclarationSyntax method:
                    symbols.Add(Symbol(method, method.Name, SymbolKind.Method, Signature(method.Parameters, method.ReturnType, text), deprecated, []));
                    break;
                case ConstructorDeclarationSyntax constructor:
                    symbols.Add(Symbol(constructor, constructor.Name, SymbolKind.Constructor, Signature(constructor.Parameters, null, text), deprecated, []));
                    break;
                case CompactConstructorDeclarationSyntax constructor:
                    symbols.Add(Symbol(constructor, constructor.Name, SymbolKind.Constructor, null, deprecated, []));
                    break;
                case FieldDeclarationSyntax field:
                    var constant = inInterface || (field.Modifiers.Has(JavaModifiers.Static) && field.Modifiers.Has(JavaModifiers.Final));
                    foreach (var variable in field.Variables)
                    {
                        var declared = Text(field.Type, text) + string.Concat(Enumerable.Repeat("[]", variable.Dimensions.Count));
                        symbols.Add(Symbol(field, variable.Name, constant ? SymbolKind.Constant : SymbolKind.Field, declared, deprecated, []));
                    }

                    break;
            }
        }

        return symbols;
    }

    private static DocumentSymbol Symbol(SyntaxNode node, IdentifierSyntax name, SymbolKind kind, string? detail, bool deprecated, IReadOnlyList<DocumentSymbol> children) =>
        new(name.Text.Length == 0 ? "?" : name.Text, kind, new TextSpan(node.Start, node.Span.Length), new TextSpan(name.Start, name.Span.Length))
        {
            Detail = detail,
            Children = children,
            IsDeprecated = deprecated,
        };

    private static string Text(SyntaxNode node, string text) =>
        node.End <= text.Length ? string.Join(' ', text[node.Start..node.End].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) : "";

    private static bool IsVarargs(ParameterSyntax parameter, string text) =>
        parameter.Type is { } type && type.End <= parameter.Name.Start && parameter.Name.Start <= text.Length && text[type.End..parameter.Name.Start].Contains("...", StringComparison.Ordinal);

    // The else or finally keyword sits between the end of what comes before it and the start of its block.
    private static int ElseStart(string text, int after, int blockStart)
    {
        for (var i = after; i < blockStart && i < text.Length; i++)
        {
            if (char.IsLetter(text[i]))
                return i;
        }

        return blockStart;
    }

    private static bool ContinuesAfter(string text, int end, int lineEnd)
    {
        for (var i = end; i < lineEnd && i < text.Length; i++)
        {
            if (text[i] == '/' && i + 1 < lineEnd && text[i + 1] is '/' or '*')
                return false;
            if (char.IsLetter(text[i]) || text[i] == '{')
                return true;
        }

        return false;
    }

    private static void AddComments(JavaSyntaxTree tree, LineMap lines, List<FoldingRange> ranges)
    {
        var comments = tree.Comments;
        for (var i = 0; i < comments.Length; i++)
        {
            var comment = comments[i];
            var first = lines.LineOf(comment.Span.Start);
            var last = lines.LineOf(Math.Max(comment.Span.Start, comment.Span.End - 1));
            if (comment.Kind is CommentKind.Line or CommentKind.MarkdownLine)
            {
                // Line comments on lines that follow each other fold as one.
                while (i + 1 < comments.Length && comments[i + 1].Kind == comment.Kind && lines.LineOf(comments[i + 1].Span.Start) == last + 1)
                    last = lines.LineOf(comments[++i].Span.Start);
            }

            if (last > first)
                ranges.Add(new FoldingRange(first, last) { Kind = FoldingRangeKind.Comment });
        }
    }
}
