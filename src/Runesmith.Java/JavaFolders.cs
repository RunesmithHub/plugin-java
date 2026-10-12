using System.Composition;
using Runesmith.Sdk.Workspace;

namespace Runesmith.Java;

/// <summary>Leaves the folder IntelliJ IDEA keeps a project's settings in out of the Explorer, Quick Open and search.</summary>
[Export(typeof(IFileExcludeContributor))]
public sealed class JavaFolders : IFileExcludeContributor
{
    public IEnumerable<string> Globs => ["**/.idea"];
}
