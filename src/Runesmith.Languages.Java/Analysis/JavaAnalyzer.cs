using Runesmith.Languages.Java.Jdk;
using Runesmith.Languages.Java.Semantics;
using Runesmith.Languages.Java.Syntax;
using Runesmith.LanguageServices;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>Understands Java, every release from 8 to 25: completion, hover, go to definition, signature help and problems, from Runesmith's
/// own parser, the JDK's API and the project's libraries, without a JVM.</summary>
/// <remarks>The folder's Maven or Gradle projects, or the folder itself, load in the background; until then, and for files outside every
/// project, documents belong to a project of their own with the newest release the JDK has.</remarks>
public sealed class JavaAnalyzer : ILanguageAnalyzer
{
    /// <summary>The language id of Java.</summary>
    public const string LanguageId = "java";

    private readonly JavaDocuments documents = new();
    private readonly JavaCompletion completion = new();
    private AnalyzerContext? context;
    private JavaWorkspace? workspace;
    private CancellationTokenSource loading = new();

    public IReadOnlyList<string> LanguageIds { get; } = [LanguageId];

    public IReadOnlyList<char> CompletionTriggerCharacters { get; } = ['.'];

    public IReadOnlyList<char> SignatureHelpTriggerCharacters { get; } = ['(', ','];

    public IReadOnlyList<char> SignatureHelpRetriggerCharacters { get; } = [','];

    /// <summary>Gets a task that completes when the projects of the open folder are loaded and their sources indexed.</summary>
    /// <summary>Gets where to look for JDKs and which one the user prefers, asked each time a JDK is needed; null searches this system.</summary>
    public Func<JdkSearchOptions>? JdkSearch { get; init; }

    internal Task Loaded { get; private set; } = Task.CompletedTask;

    /// <summary>Raised, on a background thread, when the JDK or the folder's projects finish loading, so what names mean may have changed.</summary>
    public event Action? ProjectsLoaded;

    /// <summary>Gets how many completion lists were computed, as opposed to filtered from a kept one.</summary>
    internal int ComputedCompletionLists => completion.Computed;

    internal JavaWorkspace Workspace => workspace ?? throw new InvalidOperationException("The analyzer has not been initialized.");

    private AnalyzerContext Context => context ?? throw new InvalidOperationException("The analyzer has not been initialized.");

    public void Initialize(AnalyzerContext context)
    {
        this.context = context;
        workspace = new JavaWorkspace(context.CacheDirectory, context.Log, jdkSearch: JdkSearch);
        Loaded = context.RunInBackground("Load the JDK", _ =>
        {
            completion.WarmUp(Workspace.Fallback);
            context.InvalidateDiagnostics();
            ProjectsLoaded?.Invoke();
            return Task.CompletedTask;
        });
    }

    public Task OpenWorkspaceAsync(string? rootPath, CancellationToken cancellationToken)
    {
        var context = Context;
        loading.Cancel();
        loading.Dispose();
        loading = new CancellationTokenSource();
        if (rootPath is null)
            return Task.CompletedTask;

        var token = loading.Token;
        var previous = Loaded;
        Loaded = context.RunInBackground("Load Java projects", async background =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, background);
            await previous.ConfigureAwait(false);
            await Workspace.LoadAsync(rootPath, context.YieldAsync, linked.Token).ConfigureAwait(false);
            if (linked.IsCancellationRequested)
                return;

            foreach (var project in Workspace.Projects)
                completion.WarmUp(project);
            context.InvalidateDiagnostics();
            ProjectsLoaded?.Invoke();
        });
        return Task.CompletedTask;
    }

    public void Open(SourceDocument document) => documents.Open(document);

    public void Change(SourceDocument document, IReadOnlyList<TextChange> changes)
    {
        completion.OnChanged(document.Path, document.Version, changes);
        documents.Change(document, changes);
    }

    public void Close(string path)
    {
        completion.Forget(path);
        if (documents.Close(path) is { } project)
            _ = Context.RunInBackground("Reload a closed Java file", _ =>
            {
                if (project.Owns(path))
                    JavaWorkspace.Reload(project, path);
                else
                    project.RemoveFile(path);
                return Task.CompletedTask;
            });
    }

    public ValueTask CompleteAsync(CompletionQuery query, CompletionSink sink, CancellationToken cancellationToken)
    {
        if (ModelFor(query.Document) is { } model)
            completion.Complete(model, query, sink, cancellationToken);
        return ValueTask.CompletedTask;
    }

    public ValueTask<CompletionDetails?> ResolveAsync(CompletionEntry entry, CancellationToken cancellationToken) =>
        ValueTask.FromResult(JavaCompletion.Resolve(entry));

    public ValueTask<HoverResult?> HoverAsync(DocumentPosition position, CancellationToken cancellationToken)
    {
        if (ModelFor(position.Document) is not { } model || JavaSymbols.Find(model, position.Offset) is not var (symbol, span))
            return ValueTask.FromResult<HoverResult?>(null);

        var markdown = "```java\n" + JavaSymbols.Describe(model, symbol) + "\n```";
        if (JavaSymbols.Documentation(model, symbol) is { Length: > 0 } documentation)
            markdown += "\n\n" + documentation;
        return ValueTask.FromResult<HoverResult?>(new HoverResult(markdown, new TextSpan(span.Start, span.Length)));
    }

    public ValueTask<IReadOnlyList<SourceLocation>> DefinitionAsync(DocumentPosition position, CancellationToken cancellationToken)
    {
        if (ModelFor(position.Document) is not { } model || JavaSymbols.Find(model, position.Offset) is not var (symbol, _)
            || JavaSymbols.Location(model, symbol, position.Document.Path, position.Document.Snapshot) is not { } location)
        {
            return ValueTask.FromResult<IReadOnlyList<SourceLocation>>([]);
        }

        return ValueTask.FromResult<IReadOnlyList<SourceLocation>>([location]);
    }

    public ValueTask<SignatureHelpResult?> SignatureHelpAsync(DocumentPosition position, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ModelFor(position.Document) is { } model ? JavaSignatureHelp.Get(model, position.Offset) : null);

    public async ValueTask<IReadOnlyList<Problem>> DiagnosticsAsync(SourceDocument document, CancellationToken cancellationToken)
    {
        await Context.YieldAsync(cancellationToken).ConfigureAwait(false);
        if (ModelFor(document) is not { } model)
            return [];

        var problems = new List<Problem>();
        foreach (var diagnostic in model.Tree.Diagnostics)
            problems.Add(new Problem(new TextSpan(diagnostic.Span.Start, diagnostic.Span.Length), ProblemSeverity.Error, diagnostic.Message, diagnostic.Code));

        await Context.YieldAsync(cancellationToken).ConfigureAwait(false);
        foreach (var problem in SemanticDiagnostics.Find(model, cancellationToken))
            problems.Add(new Problem(new TextSpan(problem.Span.Start, problem.Span.Length), ProblemSeverity.Error, problem.Message, problem.Code));
        return problems;
    }

    /// <summary>Gets the types and members a document declares, as a tree, for the outline and the breadcrumbs.</summary>
    public IReadOnlyList<DocumentSymbol> DocumentSymbols(string path, TextSnapshot snapshot, CancellationToken cancellationToken) =>
        JavaStructure.Symbols(TreeOf(path, snapshot), cancellationToken);

    /// <summary>Gets the lines of a document that fold.</summary>
    public IReadOnlyList<FoldingRange> FoldingRanges(string path, TextSnapshot snapshot, CancellationToken cancellationToken) =>
        JavaStructure.Folding(TreeOf(path, snapshot), cancellationToken);

    /// <summary>Gets the spans Expand Selection grows through from an offset, the smallest first.</summary>
    public IReadOnlyList<TextSpan> SelectionRanges(string path, TextSnapshot snapshot, int offset) =>
        JavaStructure.SelectionRanges(TreeOf(path, snapshot), offset);

    /// <summary>Gets the names of a document, or of a part of it, classified by what they mean; null while the document is not open.</summary>
    public IReadOnlyList<SemanticToken>? SemanticTokens(string path, TextSnapshot snapshot, TextSpan? span, CancellationToken cancellationToken) =>
        ModelOf(path, snapshot) is { } model
            ? JavaSemanticTokens.Get(model, span?.Start ?? 0, span?.End ?? model.Tree.Text.Length, cancellationToken)
            : null;

    /// <summary>Finds every use of the symbol at an offset in the sources of the folder's projects.</summary>
    public IReadOnlyList<DocumentLocation> References(string path, TextSnapshot snapshot, int offset, bool includeDeclaration, CancellationToken cancellationToken) =>
        ModelOf(path, snapshot) is { } model ? JavaReferences.Find(model, Workspace.Projects, offset, includeDeclaration, cancellationToken) : [];

    /// <summary>Finds the types that extend or implement the type at an offset, or the methods that override the method there.</summary>
    public IReadOnlyList<DocumentLocation> Implementations(string path, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        ModelOf(path, snapshot) is { } model ? JavaReferences.Implementations(model, Workspace.Projects, offset, cancellationToken) : [];

    /// <summary>Finds the occurrences of the symbol at an offset in its own document; null while the document is not open.</summary>
    public IReadOnlyList<DocumentHighlight>? Highlights(string path, TextSnapshot snapshot, int offset, CancellationToken cancellationToken) =>
        ModelOf(path, snapshot) is { } model ? JavaReferences.Highlights(model, offset, cancellationToken) : null;

    /// <summary>Finds the types, methods and fields of the folder's sources whose names contain a query.</summary>
    public IReadOnlyList<WorkspaceSymbol> WorkspaceSymbols(string query, CancellationToken cancellationToken) =>
        JavaWorkspaceSymbols.Find(Workspace.Projects, query, cancellationToken);

    /// <summary>Gets the method or constructor at an offset as the root of a call hierarchy, or nothing.</summary>
    public IReadOnlyList<HierarchyItem> PrepareCallHierarchy(string path, TextSnapshot snapshot, int offset) =>
        ModelOf(path, snapshot) is { } model ? JavaHierarchies.PrepareCalls(model, offset) : [];

    /// <summary>Gets the methods that call an item's method.</summary>
    public IReadOnlyList<HierarchyCall> IncomingCalls(HierarchyItem item, CancellationToken cancellationToken) =>
        ModelOf(item) is var (model, offset) ? JavaHierarchies.IncomingCalls(model, Workspace.Projects, offset, cancellationToken) : [];

    /// <summary>Gets the methods an item's method calls.</summary>
    public IReadOnlyList<HierarchyCall> OutgoingCalls(HierarchyItem item, CancellationToken cancellationToken) =>
        ModelOf(item) is var (model, offset) ? JavaHierarchies.OutgoingCalls(model, offset, cancellationToken) : [];

    /// <summary>Gets the type at an offset as the root of a type hierarchy, or nothing.</summary>
    public IReadOnlyList<HierarchyItem> PrepareTypeHierarchy(string path, TextSnapshot snapshot, int offset) =>
        ModelOf(path, snapshot) is { } model ? JavaHierarchies.PrepareTypes(model, offset) : [];

    /// <summary>Gets the class an item's type extends and the interfaces it names.</summary>
    public IReadOnlyList<HierarchyItem> Supertypes(HierarchyItem item) =>
        ModelOf(item) is var (model, offset) ? JavaHierarchies.Supertypes(model, offset) : [];

    /// <summary>Gets the types that extend or implement an item's type directly.</summary>
    public IReadOnlyList<HierarchyItem> Subtypes(HierarchyItem item, CancellationToken cancellationToken) =>
        ModelOf(item) is var (model, offset) ? JavaHierarchies.Subtypes(model, Workspace.Projects, offset, cancellationToken) : [];

    public ValueTask DisposeAsync()
    {
        loading.Cancel();
        loading.Dispose();
        return ValueTask.CompletedTask;
    }

    private SemanticModel? ModelFor(SourceDocument document) => documents.ModelFor(document, Workspace.ProjectFor(document.Path));

    private SemanticModel? ModelOf(string path, TextSnapshot? snapshot) => documents.ModelFor(path, snapshot, Workspace.ProjectFor);

    // What only syntax tells does not wait for the document to be opened in a project.
    private JavaSyntaxTree TreeOf(string path, TextSnapshot snapshot) =>
        ModelOf(path, snapshot)?.Tree ?? JavaSyntaxTree.Parse(snapshot.GetText(), Workspace.ProjectFor(path).ParseOptions);

    // The file an item was made in, as it is now: the open document, or the file of its project.
    private (SemanticModel Model, int Offset)? ModelOf(HierarchyItem item)
    {
        if (item.Data is not JavaAnchor anchor)
            return null;
        if (ModelOf(anchor.Path, null) is { } open)
            return (open, anchor.Offset);

        var project = Workspace.ProjectFor(anchor.Path).Snapshot;
        return project.Sources.FindFile(anchor.Path) is { } file ? (project.ModelFor(file), anchor.Offset) : null;
    }
}
