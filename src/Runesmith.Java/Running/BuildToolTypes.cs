using System.Composition;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Sdks;

namespace Runesmith.Java.Running;

/// <summary>Runs Maven goals in the open folder, with its <c>mvnw</c> when it has one.</summary>
[Export(typeof(IRunConfigurationType))]
[Shared]
[method: ImportingConstructor]
public sealed class MavenGoalsType([Import(AllowDefault = true)] ISdkService? sdks) : IRunConfigurationType
{
    /// <summary>The type's id.</summary>
    public const string TypeId = "java.maven";

    internal const string Goals = "goals";
    internal const string Profiles = "profiles";

    public string Id => TypeId;

    public string Name => Strings.RunMavenName;

    public string Icon => "package";

    public string Description => Strings.RunMavenDescription;

    public bool CanDebug => false;

    public Task<OptionSet> GetOptionsAsync(string rootPath, CancellationToken cancellationToken) => Task.FromResult(Options);

    private static OptionSet Options => new(
    [
        new Option(Goals, Strings.OptionGoals, OptionKind.Text) { IsRequired = true, Default = "package", Placeholder = "clean install" },
        new Option(Profiles, Strings.OptionProfiles, OptionKind.Text) { Placeholder = "dev,fast", Description = Strings.OptionProfilesDescription },
        JavaRunSupport.ArgumentsOption(Strings.OptionArguments, Strings.OptionMavenArgumentsDescription),
        JavaRunSupport.JdkOption,
        JavaRunSupport.EnvironmentOption,
        JavaRunSupport.WorkingDirectoryOption,
    ]);

    public Task<IReadOnlyList<RunConfiguration>> DetectAsync(string rootPath, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunConfiguration>>([]);

    public Task<LaunchPlan> PrepareAsync(RunConfiguration configuration, RunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(context);
        var values = configuration.Values;
        List<string> arguments = [];
        if (values.Get(Profiles) is { Length: > 0 } profiles && !string.IsNullOrWhiteSpace(profiles))
            arguments.AddRange(["-P", profiles.Trim()]);
        arguments.AddRange(JavaRunSupport.Words(values.Get(Goals)));
        arguments.AddRange(values.GetList(JavaRunSupport.Arguments));
        var tool = JavaBuildProvider.Tool(context.RootPath, "Maven", "mvnw", "mvn", arguments);
        return JavaRunSupport.ToolPlanAsync(tool, configuration, context, sdks, cancellationToken);
    }

    public Task<BuildResult?> BuildAsync(RunConfiguration configuration, BuildContext context, CancellationToken cancellationToken) =>
        Task.FromResult<BuildResult?>(new BuildResult(true, 0, 0, TimeSpan.Zero));
}

/// <summary>Runs Gradle tasks in the open folder, with its <c>gradlew</c> when it has one.</summary>
[Export(typeof(IRunConfigurationType))]
[Shared]
[method: ImportingConstructor]
public sealed class GradleTasksType([Import(AllowDefault = true)] ISdkService? sdks) : IRunConfigurationType
{
    /// <summary>The type's id.</summary>
    public const string TypeId = "java.gradle";

    internal const string Tasks = "tasks";

    public string Id => TypeId;

    public string Name => Strings.RunGradleName;

    public string Icon => "layers";

    public string Description => Strings.RunGradleDescription;

    public bool CanDebug => false;

    public Task<OptionSet> GetOptionsAsync(string rootPath, CancellationToken cancellationToken) => Task.FromResult(Options);

    private static OptionSet Options => new(
    [
        new Option(Tasks, Strings.OptionTasks, OptionKind.Text) { IsRequired = true, Default = "build", Placeholder = "clean build" },
        JavaRunSupport.ArgumentsOption(Strings.OptionArguments, Strings.OptionGradleArgumentsDescription),
        JavaRunSupport.JdkOption,
        JavaRunSupport.EnvironmentOption,
        JavaRunSupport.WorkingDirectoryOption,
    ]);

    public Task<IReadOnlyList<RunConfiguration>> DetectAsync(string rootPath, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunConfiguration>>([]);

    public Task<LaunchPlan> PrepareAsync(RunConfiguration configuration, RunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(context);
        List<string> arguments = [.. JavaRunSupport.Words(configuration.Values.Get(Tasks)), .. configuration.Values.GetList(JavaRunSupport.Arguments)];
        var tool = JavaBuildProvider.Tool(context.RootPath, "Gradle", "gradlew", "gradle", arguments);
        return JavaRunSupport.ToolPlanAsync(tool, configuration, context, sdks, cancellationToken);
    }

    public Task<BuildResult?> BuildAsync(RunConfiguration configuration, BuildContext context, CancellationToken cancellationToken) =>
        Task.FromResult<BuildResult?>(new BuildResult(true, 0, 0, TimeSpan.Zero));
}

/// <summary>Runs a project's JUnit tests through its build: <c>mvn test -Dtest=...</c> or <c>gradle test --tests ...</c>.</summary>
[Export(typeof(IRunConfigurationType))]
[Shared]
[method: ImportingConstructor]
public sealed class JUnitTestsType([Import(AllowDefault = true)] ISdkService? sdks) : IRunConfigurationType
{
    /// <summary>The type's id.</summary>
    public const string TypeId = "java.junit";

    internal const string Filter = "filter";

    public string Id => TypeId;

    public string Name => Strings.RunJunitName;

    public string Icon => "check-circle";

    public string Description => Strings.RunJunitDescription;

    public bool CanDebug => false;

    public Task<OptionSet> GetOptionsAsync(string rootPath, CancellationToken cancellationToken) => Task.FromResult(Options);

    private static OptionSet Options => new(
    [
        new Option(Filter, Strings.OptionTests, OptionKind.Text)
        {
            Placeholder = Strings.OptionTestsPlaceholder,
            Description = Strings.OptionTestsDescription,
        },
        JavaRunSupport.ArgumentsOption(Strings.OptionArguments, Strings.OptionBuildToolArgumentsDescription),
        JavaRunSupport.JdkOption,
        JavaRunSupport.EnvironmentOption,
        JavaRunSupport.WorkingDirectoryOption,
    ]);

    public Task<IReadOnlyList<RunConfiguration>> DetectAsync(string rootPath, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RunConfiguration>>([]);

    public Task<LaunchPlan> PrepareAsync(RunConfiguration configuration, RunContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(context);
        var filter = configuration.Values.Get(Filter)?.Trim();
        var extra = configuration.Values.GetList(JavaRunSupport.Arguments);
        var build = JavaBuildProvider.FindTool(context.RootPath)
            ?? throw new InvalidOperationException(Strings.RunNoBuildTool);
        JavaBuildProvider.BuildTool tool = build.Name == "Maven"
            ? JavaBuildProvider.Tool(context.RootPath, "Maven", "mvnw", "mvn",
                string.IsNullOrEmpty(filter) ? ["test", .. extra] : ["test", "-Dtest=" + filter, "-Dsurefire.failIfNoSpecifiedTests=false", .. extra])
            : JavaBuildProvider.Tool(context.RootPath, "Gradle", "gradlew", "gradle",
                string.IsNullOrEmpty(filter) ? ["test", .. extra] : ["test", "--tests", filter.Replace('#', '.'), .. extra]);
        return JavaRunSupport.ToolPlanAsync(tool, configuration, context, sdks, cancellationToken);
    }

    public Task<BuildResult?> BuildAsync(RunConfiguration configuration, BuildContext context, CancellationToken cancellationToken) =>
        Task.FromResult<BuildResult?>(new BuildResult(true, 0, 0, TimeSpan.Zero));
}
