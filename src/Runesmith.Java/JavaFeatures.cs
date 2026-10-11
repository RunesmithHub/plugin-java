using System.Composition;
using Runesmith.Languages.Java.Analysis;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Java;

/// <summary>Finds the Java analyzer and the path it knows an editor's document by.</summary>
internal static class JavaRequests
{
    /// <summary>Gets the path the analyzer knows a document by; a document that was never saved has one made of its name.</summary>
    public static string PathOf(IDocument document) => document.FilePath ?? "untitled:" + document.Name;

    public static JavaAnalyzer Engine(this JavaAnalysis analysis) => (JavaAnalyzer)analysis.Analyzer;
}

/// <summary>Find References, Go to Implementation and the occurrences of the symbol under the caret, for Java.</summary>
[Export(typeof(IReferenceProvider))]
[Export(typeof(IImplementationProvider))]
[Export(typeof(IDocumentHighlightProvider))]
[Languages(JavaAnalyzer.LanguageId)]
[Shared]
[method: ImportingConstructor]
public sealed class JavaNavigationProvider(JavaAnalysis analysis) : IReferenceProvider, IImplementationProvider, IDocumentHighlightProvider
{
    public Task<IReadOnlyList<DocumentLocation>> GetReferencesAsync(IDocument document, TextSnapshot snapshot, int offset, bool includeDeclaration,
        CancellationToken cancellationToken) =>
        Task.Run(() => analysis.Engine().References(JavaRequests.PathOf(document), snapshot, offset, includeDeclaration, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<DocumentLocation>> GetImplementationsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.Run(() => analysis.Engine().Implementations(JavaRequests.PathOf(document), snapshot, offset, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<DocumentHighlight>?> GetDocumentHighlightsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.Run(() => analysis.Engine().Highlights(JavaRequests.PathOf(document), snapshot, offset, cancellationToken), cancellationToken);
}

/// <summary>The symbols of a Java document, for the outline, the breadcrumbs and Go to Symbol in File.</summary>
[Export(typeof(IDocumentSymbolProvider))]
[Languages(JavaAnalyzer.LanguageId)]
[Shared]
[method: ImportingConstructor]
public sealed class JavaDocumentSymbolProvider(JavaAnalysis analysis) : IDocumentSymbolProvider
{
    public Task<IReadOnlyList<DocumentSymbol>?> GetDocumentSymbolsAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<DocumentSymbol>?>(() => analysis.Engine().DocumentSymbols(JavaRequests.PathOf(document), snapshot, cancellationToken), cancellationToken);
}

/// <summary>The types, methods and fields of the folder's Java sources by name, for Go to Symbol in Workspace.</summary>
[Export(typeof(IWorkspaceSymbolProvider))]
[Shared]
[method: ImportingConstructor]
public sealed class JavaWorkspaceSymbolProvider(JavaAnalysis analysis) : IWorkspaceSymbolProvider
{
    public Task<IReadOnlyList<WorkspaceSymbol>> GetWorkspaceSymbolsAsync(string query, CancellationToken cancellationToken) =>
        Task.Run(() => analysis.Engine().WorkspaceSymbols(query, cancellationToken), cancellationToken);
}

/// <summary>Folding, Expand Selection and semantic highlighting for Java.</summary>
[Export(typeof(IFoldingRangeProvider))]
[Export(typeof(ISelectionRangeProvider))]
[Export(typeof(ISemanticTokensProvider))]
[Languages(JavaAnalyzer.LanguageId)]
[Shared]
public sealed class JavaSyntaxProvider : IFoldingRangeProvider, ISelectionRangeProvider, ISemanticTokensProvider
{
    private readonly JavaAnalysis analysis;

    [ImportingConstructor]
    public JavaSyntaxProvider(JavaAnalysis analysis)
    {
        this.analysis = analysis;
        // What a name means is known once the JDK and the folder's projects are loaded.
        analysis.Engine().ProjectsLoaded += () => changed?.Invoke(this, new LanguageFeatureChangedEventArgs(null));
    }

    private event EventHandler<LanguageFeatureChangedEventArgs>? changed;

    event EventHandler<LanguageFeatureChangedEventArgs>? ISemanticTokensProvider.Changed
    {
        add => changed += value;
        remove => changed -= value;
    }

    public Task<IReadOnlyList<FoldingRange>?> GetFoldingRangesAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<FoldingRange>?>(() => analysis.Engine().FoldingRanges(JavaRequests.PathOf(document), snapshot, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<TextSpan>?> GetSelectionRangesAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<TextSpan>?>(() => analysis.Engine().SelectionRanges(JavaRequests.PathOf(document), snapshot, offset), cancellationToken);

    public Task<IReadOnlyList<SemanticToken>?> GetSemanticTokensAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken) =>
        Task.Run(() => analysis.Engine().SemanticTokens(JavaRequests.PathOf(document), snapshot, null, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<SemanticToken>?> GetSemanticTokensAsync(IDocument document, TextSnapshot snapshot, TextSpan span, CancellationToken cancellationToken) =>
        Task.Run(() => analysis.Engine().SemanticTokens(JavaRequests.PathOf(document), snapshot, span, cancellationToken), cancellationToken);
}

/// <summary>Call hierarchies of methods and type hierarchies of types, for Java.</summary>
[Export(typeof(ICallHierarchyProvider))]
[Export(typeof(ITypeHierarchyProvider))]
[Languages(JavaAnalyzer.LanguageId)]
[Shared]
[method: ImportingConstructor]
public sealed class JavaHierarchyProvider(JavaAnalysis analysis) : ICallHierarchyProvider, ITypeHierarchyProvider
{
    public Task<IReadOnlyList<HierarchyItem>> PrepareCallHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.Run(() => analysis.Engine().PrepareCallHierarchy(JavaRequests.PathOf(document), snapshot, offset), cancellationToken);

    public Task<IReadOnlyList<HierarchyCall>> GetIncomingCallsAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        Task.Run(() => analysis.Engine().IncomingCalls(item, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<HierarchyCall>> GetOutgoingCallsAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        Task.Run(() => analysis.Engine().OutgoingCalls(item, cancellationToken), cancellationToken);

    public Task<IReadOnlyList<HierarchyItem>> PrepareTypeHierarchyAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        Task.Run(() => analysis.Engine().PrepareTypeHierarchy(JavaRequests.PathOf(document), snapshot, offset), cancellationToken);

    public Task<IReadOnlyList<HierarchyItem>> GetSupertypesAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        Task.Run(() => analysis.Engine().Supertypes(item), cancellationToken);

    public Task<IReadOnlyList<HierarchyItem>> GetSubtypesAsync(HierarchyItem item, CancellationToken cancellationToken) =>
        Task.Run(() => analysis.Engine().Subtypes(item, cancellationToken), cancellationToken);
}
