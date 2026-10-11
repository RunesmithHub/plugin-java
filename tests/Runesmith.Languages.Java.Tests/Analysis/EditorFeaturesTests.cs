using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Languages.Java.Tests.Analysis;

public sealed class EditorFeaturesTests
{
    private const string Shape = """
        package app;

        public interface Shape {
            double area();
        }
        """;

    private const string Circle = """
        package app;

        /** A circle. */
        public class Circle implements Shape {
            public static final double PI = 3.14;
            private final double radius;

            public Circle(double radius) {
                this.radius = radius;
            }

            @Override
            public double area() {
                return Maths.square(radius) * PI;
            }
        }
        """;

    private const string Disc = """
        package app;

        public final class Disc extends Circle {
            public Disc(double radius) {
                super(radius);
            }

            public double twice() {
                double first = area();
                if (first > 0) {
                    first += area();
                } else {
                    first = 0;
                }
                return first;
            }
        }
        """;

    private const string Maths = """
        package app;

        import java.util.List;
        import java.util.Map;

        public final class Maths {
            public static double square(double value) {
                return value * value;
            }

            public static double sum(Disc disc) {
                return disc.twice() + new Circle(1).area();
            }
        }
        """;

    private static Dictionary<string, string> Files() => new()
    {
        ["app/Shape.java"] = Shape,
        ["app/Circle.java"] = Circle,
        ["app/Disc.java"] = Disc,
        ["app/Maths.java"] = Maths,
    };

    private static string Marked(string text, string find, int inside) => text.Insert(text.IndexOf(find, StringComparison.Ordinal) + inside, JavaTestProject.Caret);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheOutlineIsTheTypesWithTheirMembers()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        project.Open("app/Circle.java", Circle);

        var symbols = project.Analyzer.DocumentSymbols(project.FullPath("app/Circle.java"), project.Snapshot("app/Circle.java"), Cancel);

        var type = Assert.Single(symbols);
        Assert.Equal(("Circle", SymbolKind.Class), (type.Name, type.Kind));
        Assert.Equal(
            [("PI", SymbolKind.Constant, "double"), ("radius", SymbolKind.Field, "double"), ("Circle", SymbolKind.Constructor, "(double)"), ("area", SymbolKind.Method, "() : double")],
            type.Children.Select(s => (s.Name, s.Kind, s.Detail!)));
        Assert.Equal("area", project.Snapshot("app/Circle.java").GetText(type.Children[3].SelectionRange));
        Assert.StartsWith("@Override", project.Snapshot("app/Circle.java").GetText(type.Children[3].Range), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOutlineKnowsEnumsRecordsAndInterfaces()
    {
        const string Source = """
            package app;

            enum Color { RED, GREEN }

            record Point(int x, int y) {
                @Deprecated
                int sum() { return x + y; }
            }

            interface Named {
                int LIMIT = 3;
                String name(String prefix, int... counts);
            }
            """;
        await using var project = await JavaTestProject.CreateAsync(Files());
        var snapshot = TextSnapshot.Create(Source);

        var symbols = project.Analyzer.DocumentSymbols(project.FullPath("app/Other.java"), snapshot, Cancel);

        Assert.Equal([("Color", SymbolKind.Enum), ("Point", SymbolKind.Struct), ("Named", SymbolKind.Interface)], symbols.Select(s => (s.Name, s.Kind)));
        Assert.Equal([("RED", SymbolKind.EnumMember), ("GREEN", SymbolKind.EnumMember)], symbols[0].Children.Select(s => (s.Name, s.Kind)));
        Assert.Equal([("x", SymbolKind.Field, false), ("y", SymbolKind.Field, false), ("sum", SymbolKind.Method, true)], symbols[1].Children.Select(s => (s.Name, s.Kind, s.IsDeprecated)));
        Assert.Equal([("LIMIT", SymbolKind.Constant, "int"), ("name", SymbolKind.Method, "(String, int...) : String")], symbols[2].Children.Select(s => (s.Name, s.Kind, s.Detail!)));
    }

    [Fact]
    public async Task TypesMembersBranchesImportsAndCommentsFold()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        project.Open("app/Disc.java", Disc);
        project.Open("app/Maths.java", Maths);
        project.Open("app/Circle.java", Circle.Replace("/** A circle. */", "/**\n * A circle.\n */\n// one\n// two", StringComparison.Ordinal));

        var disc = project.Analyzer.FoldingRanges(project.FullPath("app/Disc.java"), project.Snapshot("app/Disc.java"), Cancel);
        var maths = project.Analyzer.FoldingRanges(project.FullPath("app/Maths.java"), project.Snapshot("app/Maths.java"), Cancel);
        var circle = project.Analyzer.FoldingRanges(project.FullPath("app/Circle.java"), project.Snapshot("app/Circle.java"), Cancel);

        Assert.Contains(disc, r => (r.StartLine, r.EndLine, r.Kind) == (2, 16, FoldingRangeKind.Block));
        Assert.Contains(disc, r => (r.StartLine, r.EndLine) == (3, 5));
        Assert.Contains(disc, r => (r.StartLine, r.EndLine) == (7, 15));
        Assert.Contains(disc, r => (r.StartLine, r.EndLine) == (9, 10));
        Assert.Contains(disc, r => (r.StartLine, r.EndLine) == (11, 13));
        Assert.All(disc, r => Assert.True(r.EndLine > r.StartLine));
        Assert.Contains(maths, r => (r.StartLine, r.EndLine, r.Kind) == (2, 3, FoldingRangeKind.Imports));
        Assert.Contains(circle, r => (r.StartLine, r.EndLine, r.Kind) == (2, 4, FoldingRangeKind.Comment));
        Assert.Contains(circle, r => (r.StartLine, r.EndLine, r.Kind) == (5, 6, FoldingRangeKind.Comment));
    }

    [Fact]
    public async Task ExpandSelectionGrowsFromTheWordThroughEachPartAroundIt()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var offset = project.Open("app/Disc.java", Marked(Disc, "first += area();", 11));
        var snapshot = project.Snapshot("app/Disc.java");

        var spans = project.Analyzer.SelectionRanges(project.FullPath("app/Disc.java"), snapshot, offset);
        var texts = spans.Select(s => snapshot.GetText(s)).ToList();

        Assert.Equal(["area", "area()", "first += area()", "first += area();"], texts.Take(4));
        Assert.Contains(texts, t => t.StartsWith("if (first > 0)", StringComparison.Ordinal) && t.EndsWith('}'));
        Assert.Contains(texts, t => t.StartsWith("public double twice()", StringComparison.Ordinal));
        Assert.Equal(new TextSpan(0, snapshot.Length), spans[^1]);
        for (var i = 1; i < spans.Count; i++)
            Assert.True(spans[i].Length > spans[i - 1].Length && spans[i].Start <= spans[i - 1].Start && spans[i].End >= spans[i - 1].End);
    }

    [Fact]
    public async Task NamesAreClassifiedByWhatTheyMean()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        project.Open("app/Circle.java", Circle);
        var snapshot = project.Snapshot("app/Circle.java");

        var tokens = project.Analyzer.SemanticTokens(project.FullPath("app/Circle.java"), snapshot, null, Cancel);

        Assert.NotNull(tokens);
        var named = tokens.Select(t => (Text: snapshot.GetText(t.Span), t.Type, t.Modifiers)).ToList();
        Assert.Contains(named, t => (t.Text, t.Type) == ("Shape", SemanticTokenTypes.Interface));
        Assert.Contains(named, t => (t.Text, t.Type) == ("Circle", SemanticTokenTypes.Class));
        Assert.Contains(named, t => (t.Text, t.Type) == ("Override", SemanticTokenTypes.Decorator));
        Assert.Contains(named, t => (t.Text, t.Type) == ("Maths", SemanticTokenTypes.Class));
        Assert.Contains(named, t => t.Text == "PI" && t.Type == SemanticTokenTypes.Property && t.Modifiers.HasFlag(SemanticTokenModifiers.Static | SemanticTokenModifiers.Readonly));
        Assert.Contains(named, t => t.Text == "radius" && t.Type == SemanticTokenTypes.Property && !t.Modifiers.HasFlag(SemanticTokenModifiers.Static));
        Assert.Contains(named, t => (t.Text, t.Type) == ("radius", SemanticTokenTypes.Parameter));
        Assert.Contains(named, t => t.Text == "square" && t.Type == SemanticTokenTypes.Method && t.Modifiers.HasFlag(SemanticTokenModifiers.Static));
        Assert.Contains(named, t => t.Text == "area" && t.Type == SemanticTokenTypes.Method && !t.Modifiers.HasFlag(SemanticTokenModifiers.Static));
        for (var i = 1; i < tokens.Count; i++)
            Assert.True(tokens[i].Span.Start >= tokens[i - 1].Span.End);
    }

    [Fact]
    public async Task TokensOfASpanAreOnlyThoseInsideItAndTypeVariablesAndLocalsAreTold()
    {
        const string Source = "package app;\n\nclass Box<T> {\n    T value;\n\n    <R> R map(java.util.function.Function<T, R> change) {\n        var local = change.apply(value);\n        return local;\n    }\n}\n";
        await using var project = await JavaTestProject.CreateAsync(Files());
        project.Open("app/Box.java", Source);
        var snapshot = project.Snapshot("app/Box.java");
        var start = Source.IndexOf("<R>", StringComparison.Ordinal);

        var tokens = project.Analyzer.SemanticTokens(project.FullPath("app/Box.java"), snapshot, new TextSpan(start, Source.Length - start), Cancel);

        Assert.NotNull(tokens);
        var named = tokens.Select(t => (Text: snapshot.GetText(t.Span), t.Type)).ToList();
        Assert.DoesNotContain(named, t => t.Text == "Box");
        Assert.All(tokens, t => Assert.True(t.Span.Start >= start));
        Assert.Contains(named, t => (t.Text, t.Type) == ("R", SemanticTokenTypes.TypeParameter));
        Assert.Contains(named, t => (t.Text, t.Type) == ("T", SemanticTokenTypes.TypeParameter));
        Assert.Contains(named, t => (t.Text, t.Type) == ("local", SemanticTokenTypes.Variable));
        Assert.Contains(named, t => (t.Text, t.Type) == ("change", SemanticTokenTypes.Parameter));
        Assert.Contains(named, t => (t.Text, t.Type) == ("util", SemanticTokenTypes.Namespace));
        Assert.Contains(named, t => (t.Text, t.Type) == ("Function", SemanticTokenTypes.Interface));
    }

    [Fact]
    public async Task ReferencesAreFoundInEveryFileAlsoThroughOverrides()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var offset = project.Open("app/Shape.java", Marked(Shape, "area();", 2));
        var path = project.FullPath("app/Shape.java");

        var without = project.Analyzer.References(path, project.Snapshot("app/Shape.java"), offset, includeDeclaration: false, Cancel);
        var with = project.Analyzer.References(path, project.Snapshot("app/Shape.java"), offset, includeDeclaration: true, Cancel);

        Assert.Equal(
            [("Disc.java", 8), ("Disc.java", 10), ("Maths.java", 11)],
            without.Select(l => (Path.GetFileName(l.FilePath), l.Start.Line)));
        Assert.Equal(
            [("Circle.java", 12), ("Disc.java", 8), ("Disc.java", 10), ("Maths.java", 11), ("Shape.java", 3)],
            with.Select(l => (Path.GetFileName(l.FilePath), l.Start.Line)));
        Assert.All(with, l => Assert.Equal(4, l.End!.Value.Column - l.Start.Column));
    }

    [Fact]
    public async Task ReferencesOfATypeAFieldAndAConstructorAreFound()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var onType = project.Open("app/Circle.java", Marked(Circle, "class Circle", 8));
        var path = project.FullPath("app/Circle.java");
        var snapshot = project.Snapshot("app/Circle.java");
        var onField = Circle.IndexOf("double radius;", StringComparison.Ordinal) + 8;
        var onConstructor = Circle.IndexOf("Circle(double", StringComparison.Ordinal) + 2;

        var type = project.Analyzer.References(path, snapshot, onType, includeDeclaration: false, Cancel);
        var field = project.Analyzer.References(path, snapshot, onField, includeDeclaration: true, Cancel);
        var constructor = project.Analyzer.References(path, snapshot, onConstructor, includeDeclaration: false, Cancel);

        Assert.Equal([("Disc.java", 2), ("Maths.java", 11)], type.Select(l => (Path.GetFileName(l.FilePath), l.Start.Line)));
        Assert.Equal([5, 8, 13], field.Select(l => l.Start.Line));
        Assert.Equal([("Maths.java", 11)], constructor.Select(l => (Path.GetFileName(l.FilePath), l.Start.Line)));
    }

    [Fact]
    public async Task OccurrencesInTheDocumentSayWhetherTheyWrite()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var offset = project.Open("app/Disc.java", Marked(Disc, "first = area();", 2));
        var snapshot = project.Snapshot("app/Disc.java");

        var highlights = project.Analyzer.Highlights(project.FullPath("app/Disc.java"), snapshot, offset, Cancel);

        Assert.NotNull(highlights);
        Assert.Equal(
            [DocumentHighlightKind.Write, DocumentHighlightKind.Read, DocumentHighlightKind.Write, DocumentHighlightKind.Write, DocumentHighlightKind.Read],
            highlights.Select(h => h.Kind));
        Assert.All(highlights, h => Assert.Equal("first", snapshot.GetText(h.Span)));
    }

    [Fact]
    public async Task ImplementationsAreTheTypesBelowATypeAndTheMethodsThatOverride()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var onInterface = project.Open("app/Shape.java", Marked(Shape, "interface Shape", 12));
        var shape = project.FullPath("app/Shape.java");
        var onMethod = Shape.IndexOf("area();", StringComparison.Ordinal) + 1;
        var onClass = project.Open("app/Circle.java", Marked(Circle, "class Circle", 8));
        var onLeaf = project.Open("app/Disc.java", Marked(Disc, "class Disc", 7));

        var ofInterface = project.Analyzer.Implementations(shape, project.Snapshot("app/Shape.java"), onInterface, Cancel);
        var ofMethod = project.Analyzer.Implementations(shape, project.Snapshot("app/Shape.java"), onMethod, Cancel);
        var ofClass = project.Analyzer.Implementations(project.FullPath("app/Circle.java"), project.Snapshot("app/Circle.java"), onClass, Cancel);
        var ofLeaf = project.Analyzer.Implementations(project.FullPath("app/Disc.java"), project.Snapshot("app/Disc.java"), onLeaf, Cancel);

        Assert.Equal([("Circle.java", 3), ("Disc.java", 2)], ofInterface.Select(l => (Path.GetFileName(l.FilePath), l.Start.Line)));
        Assert.Equal([("Circle.java", 12)], ofMethod.Select(l => (Path.GetFileName(l.FilePath), l.Start.Line)));
        Assert.Equal([("Disc.java", 2)], ofClass.Select(l => (Path.GetFileName(l.FilePath), l.Start.Line)));
        Assert.Equal([("Disc.java", 2)], ofLeaf.Select(l => (Path.GetFileName(l.FilePath), l.Start.Line)));
    }

    [Fact]
    public async Task SymbolsOfTheFolderAreFoundByNameWithTheClosestFirst()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());

        var circle = project.Analyzer.WorkspaceSymbols("circle", Cancel);
        var square = project.Analyzer.WorkspaceSymbols("squ", Cancel);

        Assert.Equal(("Circle", SymbolKind.Class, "app"), (circle[0].Name, circle[0].Kind, circle[0].ContainerName));
        Assert.Equal((project.FullPath("app/Circle.java"), 3), (circle[0].Location.FilePath, circle[0].Location.Start.Line));
        var method = Assert.Single(square);
        Assert.Equal(("square", SymbolKind.Method, "app.Maths"), (method.Name, method.Kind, method.ContainerName));
        Assert.Empty(project.Analyzer.WorkspaceSymbols("  ", Cancel));
    }

    [Fact]
    public async Task ACallHierarchyHasTheCallersAndTheCalledMethods()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var offset = project.Open("app/Disc.java", Marked(Disc, "twice()", 2));

        var item = Assert.Single(project.Analyzer.PrepareCallHierarchy(project.FullPath("app/Disc.java"), project.Snapshot("app/Disc.java"), offset));
        var incoming = project.Analyzer.IncomingCalls(item, Cancel);
        var outgoing = project.Analyzer.OutgoingCalls(item, Cancel);

        Assert.Equal(("twice()", SymbolKind.Method, "app.Disc", 7), (item.Name, item.Kind, item.Detail, item.SelectionLocation!.Start.Line));
        var caller = Assert.Single(incoming);
        Assert.Equal(("sum(Disc)", "app.Maths"), (caller.Item.Name, caller.Item.Detail));
        Assert.Equal(11, Assert.Single(caller.CallSites).Start.Line);
        var called = Assert.Single(outgoing);
        Assert.Equal(("area()", "app.Circle"), (called.Item.Name, called.Item.Detail));
        Assert.Equal([8, 10], called.CallSites.Select(s => s.Start.Line));
    }

    [Fact]
    public async Task OutgoingCallsIncludeConstructorsAndLeaveOutTheJdk()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var offset = project.Open("app/Maths.java", Marked(Maths, "sum(Disc", 1).Replace("return disc", "System.out.println(disc);\n        return disc", StringComparison.Ordinal));

        var item = Assert.Single(project.Analyzer.PrepareCallHierarchy(project.FullPath("app/Maths.java"), project.Snapshot("app/Maths.java"), offset));
        var outgoing = project.Analyzer.OutgoingCalls(item, Cancel);
        var callersOfSum = project.Analyzer.IncomingCalls(item, Cancel);

        Assert.Equal(["area()", "Circle(double)", "twice()"], outgoing.Select(c => c.Item.Name));
        Assert.Equal(SymbolKind.Constructor, outgoing[1].Item.Kind);
        Assert.Empty(callersOfSum);
    }

    [Fact]
    public async Task ATypeHierarchyGoesUpAndDown()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var offset = project.Open("app/Disc.java", Marked(Disc, "class Disc", 7));

        var disc = Assert.Single(project.Analyzer.PrepareTypeHierarchy(project.FullPath("app/Disc.java"), project.Snapshot("app/Disc.java"), offset));
        var circle = Assert.Single(project.Analyzer.Supertypes(disc));
        var shape = Assert.Single(project.Analyzer.Supertypes(circle));
        var below = project.Analyzer.Subtypes(shape, Cancel);

        Assert.Equal(("Disc", SymbolKind.Class, "app"), (disc.Name, disc.Kind, disc.Detail));
        Assert.Equal("Circle", circle.Name);
        Assert.Equal(("Shape", SymbolKind.Interface), (shape.Name, shape.Kind));
        Assert.Empty(project.Analyzer.Supertypes(shape));
        Assert.Equal(["Circle"], below.Select(i => i.Name));
        Assert.Equal(["Disc"], project.Analyzer.Subtypes(below[0], Cancel).Select(i => i.Name));
        Assert.Empty(project.Analyzer.PrepareCallHierarchy(project.FullPath("app/Disc.java"), project.Snapshot("app/Disc.java"), offset));
    }

    [Fact]
    public async Task ADocumentIsAnsweredForTheTextAskedAbout()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        project.Open("app/Disc.java", Disc);
        var path = project.FullPath("app/Disc.java");
        var first = project.Snapshot("app/Disc.java");
        project.Insert("app/Disc.java", 0, "// a comment\n");
        var ahead = TextSnapshot.Create(Disc.Replace("twice", "thrice", StringComparison.Ordinal));
        var onFirst = Disc.IndexOf("first", StringComparison.Ordinal);

        Assert.Equal(5, project.Analyzer.Highlights(path, first, onFirst, Cancel)!.Count);
        Assert.Equal(5, project.Analyzer.Highlights(path, project.Snapshot("app/Disc.java"), onFirst + 13, Cancel)!.Count);
        Assert.Equal(5, project.Analyzer.Highlights(path, ahead, onFirst + 1, Cancel)!.Count);
        Assert.Contains(project.Analyzer.DocumentSymbols(path, ahead, Cancel)[0].Children, s => s.Name == "thrice");
        Assert.Equal(5, project.Analyzer.Highlights("untitled:Elsewhere", first, onFirst, Cancel)!.Count);
    }

    [Fact]
    public async Task ADocumentThatIsNotOpenHasItsOutlineButNoMeanings()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var path = project.FullPath("app/Circle.java");
        var snapshot = TextSnapshot.Create(Circle);

        Assert.Equal("Circle", Assert.Single(project.Analyzer.DocumentSymbols(path, snapshot, Cancel)).Name);
        Assert.NotEmpty(project.Analyzer.FoldingRanges(path, snapshot, Cancel));
        Assert.Null(project.Analyzer.SemanticTokens(path, snapshot, null, Cancel));
        Assert.Null(project.Analyzer.Highlights(path, snapshot, 30, Cancel));
        Assert.Empty(project.Analyzer.References(path, snapshot, 30, true, Cancel));
    }

    [Fact]
    public async Task NothingIsFoundWhereNoSymbolIs()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        project.Open("app/Shape.java", Shape);
        var path = project.FullPath("app/Shape.java");
        var snapshot = project.Snapshot("app/Shape.java");

        Assert.Empty(project.Analyzer.References(path, snapshot, 0, true, Cancel));
        Assert.Empty(project.Analyzer.Implementations(path, snapshot, 0, Cancel));
        Assert.Empty(project.Analyzer.Highlights(path, snapshot, 0, Cancel)!);
        Assert.Empty(project.Analyzer.PrepareCallHierarchy(path, snapshot, 0));
        Assert.Empty(project.Analyzer.PrepareTypeHierarchy(path, snapshot, 0));
        Assert.Empty(project.Analyzer.SelectionRanges(path, TextSnapshot.Create(""), 0));
    }
}
