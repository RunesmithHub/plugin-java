using System.Composition;
using System.Globalization;
using System.Text.RegularExpressions;
using Runesmith.Sdk.Running;

namespace Runesmith.Java.Running;

/// <summary>Finds the source locations in the stack traces of Java and the other languages of the Java virtual machine, such as
/// <c>at com.example.Main.run(Main.java:12)</c>, so the Run console and the terminal link them.</summary>
/// <remarks>A frame names its class and its file but not the folder the sources are in, so the link is the end of a path, such as
/// <c>com/example/Main.java</c>, which Runesmith looks for in the open folder.</remarks>
[Export(typeof(IConsoleLinkProvider))]
public sealed partial class JavaConsoleLinks : IConsoleLinkProvider
{
    public IReadOnlyList<ConsoleLink> FindLinks(string line)
    {
        if (!line.Contains('(', StringComparison.Ordinal))
            return [];

        var links = new List<ConsoleLink>();
        foreach (Match match in Frame().Matches(line))
        {
            if (!int.TryParse(match.Groups["line"].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                continue;

            // The frame is module/package.Class.method; the package is the folder of the file.
            var file = match.Groups["file"];
            var frame = match.Groups["frame"].Value;
            var parts = frame[(frame.LastIndexOf('/') + 1)..].Split('.');
            var package = parts.Length > 2 ? string.Join('/', parts[..^2]) + "/" : "";
            links.Add(new ConsoleLink(file.Index, match.Groups["end"].Index - file.Index, package + file.Value, number, 0, IsSourceRelative: true));
        }

        return links;
    }

    [GeneratedRegex(@"\bat\s+(?<frame>[\w$.<>/-]+)\((?<file>[\w$-]+\.(?:java|kt|groovy|scala)):(?<line>\d+)(?<end>\))", RegexOptions.CultureInvariant)]
    private static partial Regex Frame();
}
