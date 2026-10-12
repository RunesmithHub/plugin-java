using Runesmith.Java.Running;

namespace Runesmith.Java.Tests.Running;

public sealed class JavaConsoleLinksTests
{
    [Fact]
    public void FindsJavaStackFramesRelativeToTheirPackage()
    {
        var links = new JavaConsoleLinks().FindLinks("\tat com.example.app.Main$Inner.run(Main.java:42)");
        var noPackage = new JavaConsoleLinks().FindLinks("\tat Main.main(Main.java:3)");
        var module = new JavaConsoleLinks().FindLinks("\tat java.base/java.lang.Thread.run(Thread.java:1583)");

        var link = Assert.Single(links);
        Assert.Equal("com/example/app/Main.java", link.Path);
        Assert.Equal(42, link.Line);
        Assert.True(link.IsSourceRelative);
        Assert.Equal("Main.java:42", "\tat com.example.app.Main$Inner.run(Main.java:42)".Substring(link.Start, link.Length));
        Assert.Equal("Main.java", Assert.Single(noPackage).Path);
        Assert.Equal("java/lang/Thread.java", Assert.Single(module).Path);
    }
}
