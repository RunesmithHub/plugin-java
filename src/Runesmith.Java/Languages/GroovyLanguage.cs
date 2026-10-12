using System.Composition;
using Runesmith.Sdk.Languages;

namespace Runesmith.Java.Languages;

/// <summary>Groovy: its files, highlighting, comments and brackets.</summary>
public sealed class GroovyLanguage
{
    [Export(typeof(LanguageDefinition))]
    public LanguageDefinition Groovy { get; } = new("groovy", "Groovy")
    {
        Extensions = [".groovy", ".gvy", ".gradle", ".jenkinsfile", ".nf"],
        FileNames = ["Jenkinsfile"],
        ScopeName = "source.groovy",
        LineComment = "//",
        BlockComment = ("/*", "*/"),
        Brackets = [('{', '}'), ('[', ']'), ('(', ')')],
        Icon = "file-code",
    };

    [Export(typeof(GrammarDefinition))]
    public GrammarDefinition GroovyGrammar { get; } = GrammarFiles.Find("source.groovy", "groovy/groovy.tmLanguage.json");
}
