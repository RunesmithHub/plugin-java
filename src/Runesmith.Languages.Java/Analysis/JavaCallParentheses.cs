using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Semantics;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>The parentheses completion inserts after a name the code must call: a method, which Java only names without calling it in an
/// import or a method reference, and a type after <c>new</c>. The text becomes a snippet with the caret between the parentheses, or after
/// them when nothing the name stands for takes arguments.</summary>
/// <remarks>One is made for each computed list. A type's constructors are looked up the first time the type matches the typed word, since
/// that reads its class file, and kept for the list.</remarks>
internal sealed class JavaCallParentheses
{
    public static readonly JavaCallParentheses None = new(null, callsMethods: false, createsObjects: false);

    private readonly SemanticModel? model;
    private readonly Dictionary<string, string?> constructors = new(StringComparer.Ordinal);

    private JavaCallParentheses(SemanticModel? model, bool callsMethods, bool createsObjects)
    {
        this.model = model;
        CallsMethods = callsMethods;
        CreatesObjects = createsObjects;
    }

    /// <summary>Gets whether a method's name is called at the word.</summary>
    public bool CallsMethods { get; }

    /// <summary>Gets whether the word is the type of an object creation.</summary>
    public bool CreatesObjects { get; }

    /// <summary>Works out what completing the word that ends at an offset calls.</summary>
    public static JavaCallParentheses For(SemanticModel model, CompletionContext context, int wordEnd)
    {
        var next = NextCharacter(model.Tree.Text, wordEnd);
        return context.Kind switch
        {
            CompletionContextKind.MemberAccess or CompletionContextKind.Statement or CompletionContextKind.Expression when next != '(' =>
                new JavaCallParentheses(model, callsMethods: true, createsObjects: false),
            CompletionContextKind.NewType when next is not ('(' or '<' or '[' or '{') =>
                new JavaCallParentheses(model, callsMethods: false, createsObjects: true),
            _ => None,
        };
    }

    /// <summary>Gets the snippet a method suggestion inserts: its name, and the caret between parentheses when an overload takes arguments.</summary>
    public string? ForMethods(string name, IReadOnlyList<MemberRef<MethodSymbol>> overloads)
    {
        if (!CallsMethods)
            return null;

        return overloads.Any(method => method.Member.ParameterTypes.Count > 0) ? Escape(name) + "($0)" : Escape(name) + "()";
    }

    /// <summary>Gets the snippet a type suggestion inserts after <c>new</c>: its name, a diamond when it is generic, and parentheses with the
    /// caret between them when a constructor the code may call takes arguments; null for types that cannot be created, such as enums.</summary>
    public string? ForType(string simpleName, string binaryName)
    {
        if (!CreatesObjects)
            return null;

        lock (constructors)
        {
            if (!constructors.TryGetValue(binaryName, out var snippet))
                constructors[binaryName] = snippet = ConstructorSnippet(simpleName, binaryName);
            return snippet;
        }
    }

    /// <summary>Escapes the characters that mean something in a snippet; a Java name may contain <c>$</c>.</summary>
    public static string Escape(string name) => name.Replace("$", "\\$", StringComparison.Ordinal);

    private string? ConstructorSnippet(string simpleName, string binaryName)
    {
        // A type whose class file cannot be read is still created with parentheses; what goes in them is not known.
        if (model!.FindClass(binaryName) is not { } type)
            return Escape(simpleName) + "($0)";

        var name = Escape(simpleName) + (type.TypeParameters.Count > 0 ? "<>" : "");
        switch (type.Kind)
        {
            case ClassKind.Interface:
                // An interface is created as an anonymous class, whose constructor takes nothing.
                return name + "()";
            case ClassKind.Class or ClassKind.Record:
                var callable = type.Constructors.Where(constructor => JavaCompletion.IsAccessible(model, constructor.Modifiers, type)).ToList();
                if (callable.Count == 0)
                    return null;
                return callable.Any(constructor => constructor.ParameterTypes.Count > 0) ? name + "($0)" : name + "()";
            default:
                return null;
        }
    }

    private static char NextCharacter(string text, int offset)
    {
        for (var i = offset; i < text.Length; i++)
        {
            if (text[i] is not (' ' or '\t'))
                return text[i];
        }

        return '\0';
    }
}
