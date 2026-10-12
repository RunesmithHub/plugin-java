using Runesmith.LanguageServices;

namespace Runesmith.Languages.Java.Tests.Analysis;

public sealed class CallParenthesesTests
{
    private const string Types = """

        record Point(int x, int y) { }

        class Empty { }

        abstract class Shape { }

        enum Color { RED }
        """;

    [Fact]
    public async Task AMethodThatTakesArgumentsPutsTheCaretBetweenItsParentheses()
    {
        var item = await ItemAsync("class App { void run() { System.out.$$ } }", "println", member: true);

        Assert.True(item.IsSnippet);
        Assert.Equal("println($0)", item.InsertText);
    }

    [Fact]
    public async Task AMethodWithoutArgumentsPutsTheCaretAfterItsParentheses()
    {
        var item = await ItemAsync("class App { void run() { \"abc\".$$ } }", "length", member: true);

        Assert.True(item.IsSnippet);
        Assert.Equal("length()", item.InsertText);
    }

    [Fact]
    public async Task AMethodCalledByItsNameGetsParentheses()
    {
        var item = await ItemAsync("class App { void greet(String name) { } void run() { gre$$ } }", "greet");

        Assert.Equal("greet($0)", item.InsertText);
    }

    [Fact]
    public async Task AStaticMethodOfATypeGetsParentheses()
    {
        var item = await ItemAsync("class App { void run() { int x = Math.ab$$ } }", "abs");

        Assert.Equal("abs($0)", item.InsertText);
    }

    [Fact]
    public async Task ACallWhoseParenthesesAreTypedAlreadyGetsOnlyTheName()
    {
        var item = await ItemAsync("class App { void run() { System.out.printl$$(\"hi\"); } }", "println");

        Assert.False(item.IsSnippet);
        Assert.Null(item.InsertText);
    }

    [Fact]
    public async Task AStaticImportGetsOnlyTheName()
    {
        var item = await ItemAsync("import static java.lang.Math.ab$$\n\nclass App { }", "abs");

        Assert.False(item.IsSnippet);
    }

    [Theory]
    [InlineData("new Poi$$", "Point", "Point($0)")]
    [InlineData("new Emp$$", "Empty", "Empty()")]
    [InlineData("new Shap$$", "Shape", "Shape()")]
    [InlineData("new StringBuil$$", "StringBuilder", "StringBuilder($0)")]
    [InlineData("new Runnab$$", "Runnable", "Runnable()")]
    [InlineData("new Arr$$", "ArrayList", "ArrayList<>($0)")]
    public async Task ATypeAfterNewGetsTheParenthesesOfItsConstructors(string code, string label, string expected)
    {
        var item = await ItemAsync($"class App {{ void run() {{ var x = {code}; }} }}{Types}", label);

        Assert.True(item.IsSnippet);
        Assert.Equal(expected, item.InsertText);
    }

    [Theory]
    [InlineData("new Colo$$", "Color")]
    [InlineData("new Mat$$", "Math")]
    [InlineData("new Strin$$[3]", "String")]
    [InlineData("new Arr$$<String>()", "ArrayList")]
    [InlineData("Poi$$ p", "Point")]
    public async Task ATypeThatIsNotCreatedHereGetsOnlyItsName(string code, string label)
    {
        var item = await ItemAsync($"class App {{ void run() {{ var x = {code}; }} }}{Types}", label);

        Assert.False(item.IsSnippet);
    }

    [Fact]
    public async Task ATypeThatIsNotImportedKeepsItsImport()
    {
        await using var project = await JavaTestProject.CreateAsync(new Dictionary<string, string> { ["app/App.java"] = "" });
        var offset = project.Open("app/App.java", "package app;\n\nclass App { void run() { var list = new Arr$$ } }");

        var result = await project.CompleteAsync("app/App.java", offset);
        var arrayList = Assert.Single(result.Items, i => i.Label == "ArrayList" && i.Detail == "java.util");
        var details = await project.Host.ResolveAsync(arrayList, TestContext.Current.CancellationToken);

        Assert.Equal("ArrayList<>($0)", arrayList.InsertText);
        Assert.Equal("\n\nimport java.util.ArrayList;", Assert.Single(details!.AdditionalChanges).NewText);
    }

    private static async Task<CompletionEntry> ItemAsync(string code, string label, bool member = false)
    {
        await using var project = await JavaTestProject.CreateAsync(new Dictionary<string, string> { ["App.java"] = "" });
        var offset = project.Open("App.java", code);

        var result = member ? await project.CompleteMemberAsync("App.java", offset) : await project.CompleteAsync("App.java", offset);

        return result.Items.FirstOrDefault(item => item.Label == label)
            ?? throw new Xunit.Sdk.XunitException($"No {label} in: {string.Join(", ", result.Items.Take(20).Select(item => item.Label))}");
    }
}
