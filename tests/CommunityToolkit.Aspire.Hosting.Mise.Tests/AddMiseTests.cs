using Aspire.Hosting;
using CommunityToolkit.Aspire.Hosting.Mise;
using CommunityToolkit.Aspire.Testing;

namespace CommunityToolkit.Aspire.Hosting.Mise.Tests;

[Collection("MiseResolverHooks")]
public class AddMiseTests
{
    private static readonly string s_testConfigDirectory = AppContext.BaseDirectory;

    [Fact]
    public async Task AddMiseCreatesResourceWithNormalizedDirectoryAndTrustEnvironment()
    {
        var builder = DistributedApplication.CreateBuilder();

        var mise = builder.AddMise("mise", s_testConfigDirectory);

        Assert.Equal(Path.GetFullPath(s_testConfigDirectory), mise.Resource.ConfigDirectory);
        Assert.Equal(mise.Resource.ConfigDirectory, mise.Resource.WorkingDirectory);

        var env = await mise.Resource.GetEnvironmentVariablesAsync();
        Assert.Equal("1", env["MISE_YES"]);
        Assert.Equal(mise.Resource.ConfigDirectory, env["MISE_TRUSTED_CONFIG_PATHS"]);
    }

    [Fact]
    public async Task AddMiseConfiguresInstallCommandOnBeforeStart()
    {
        using var scope = new MiseResolverHookScope(findCommandOnPath: _ => "/usr/local/bin/mise");

        var builder = DistributedApplication.CreateBuilder();
        var mise = builder.AddMise("mise", s_testConfigDirectory);

        using var app = builder.Build();
        await MiseTestHelpers.RunMiseBeforeStartAsync(app);

        Assert.Equal("/usr/local/bin/mise", mise.Resource.Command);
        var args = await mise.Resource.GetArgumentListAsync();
        Assert.Equal(["install"], args);
    }

    [Fact]
    public async Task AddMiseThrowsOnBeforeStartWhenMiseIsNotFound()
    {
        using var scope = new MiseResolverHookScope(findCommandOnPath: _ => null);

        var builder = DistributedApplication.CreateBuilder();
        builder.AddMise("mise", s_testConfigDirectory);

        using var app = builder.Build();

        var exception = await Assert.ThrowsAsync<DistributedApplicationException>(() => MiseTestHelpers.RunMiseBeforeStartAsync(app));
        Assert.Contains("mise was not found on PATH", exception.Message);
    }

    [Fact]
    public void AddMiseRegistersDashboardCommands()
    {
        var builder = DistributedApplication.CreateBuilder();

        var mise = builder.AddMise("mise", s_testConfigDirectory);

        var commandNames = mise.Resource.Annotations.OfType<ResourceCommandAnnotation>().Select(a => a.Name).ToArray();
        Assert.Contains("reinstall-tools", commandNames);
        Assert.Contains("refresh-environment", commandNames);
    }

    [Fact]
    public void AddMiseIsExcludedFromPublish()
    {
        var builder = DistributedApplication.CreateBuilder(["Publishing:Publisher=manifest", "Publishing:OutputPath=./publish"]);

        var mise = builder.AddMise("mise", s_testConfigDirectory);
        var task = mise.AddMiseTask("build");

        Assert.True(mise.Resource.IsExcludedFromPublish());
        Assert.True(task.Resource.IsExcludedFromPublish());

        // No waits, no dashboard commands, no required command in publish mode.
        Assert.DoesNotContain(task.Resource.Annotations, a => a is WaitAnnotation);
        Assert.DoesNotContain(mise.Resource.Annotations, a => a is ResourceCommandAnnotation);
    }

    [Fact]
    public async Task WithMiseEnvironmentContributesNothingInPublishMode()
    {
        var builder = DistributedApplication.CreateBuilder(["Publishing:Publisher=manifest", "Publishing:OutputPath=./publish"]);

        var mise = builder.AddMise("mise", s_testConfigDirectory);
        var app = builder.AddExecutable("app", "node", s_testConfigDirectory).WithMiseEnvironment(mise);

        Assert.DoesNotContain(app.Resource.Annotations, a => a is WaitAnnotation);

        var env = await app.Resource.GetEnvironmentVariablesAsync(DistributedApplicationOperation.Publish);
        Assert.Empty(env);
    }

    [Fact]
    public void WithMiseEnvironmentAddsWaitAndAnnotations()
    {
        var builder = DistributedApplication.CreateBuilder();

        var mise = builder.AddMise("mise", s_testConfigDirectory);
        var app = builder.AddExecutable("app", "node", s_testConfigDirectory)
            .WithMiseEnvironment(mise)
            .WithMiseEnvironment(mise); // idempotent

        var wait = Assert.Single(app.Resource.Annotations.OfType<WaitAnnotation>());
        Assert.Same(mise.Resource, wait.Resource);
        Assert.Equal(WaitType.WaitForCompletion, wait.WaitType);

        var environmentAnnotation = Assert.Single(app.Resource.Annotations.OfType<MiseEnvironmentAnnotation>());
        Assert.Same(mise.Resource, environmentAnnotation.Mise);
        var installerAnnotation = Assert.Single(app.Resource.Annotations.OfType<MiseInstallerAnnotation>());
        Assert.Same(mise.Resource, installerAnnotation.Resource);
    }

    [Fact]
    public void WithMiseEnvironmentAndWithMiseExecAreMutuallyExclusive()
    {
        var builder = DistributedApplication.CreateBuilder();

        var mise = builder.AddMise("mise", s_testConfigDirectory);
        var app = builder.AddExecutable("app", "node", s_testConfigDirectory).WithMiseEnvironment(mise);
        var other = builder.AddExecutable("other", "node", s_testConfigDirectory).WithMiseExec(mise);

        Assert.Throws<InvalidOperationException>(() => app.WithMiseExec(mise));
        Assert.Throws<InvalidOperationException>(() => other.WithMiseEnvironment(mise));
    }

    [Fact]
    public void WithMiseEnvironmentThrowsWhenBoundToDifferentMise()
    {
        var builder = DistributedApplication.CreateBuilder();

        var mise1 = builder.AddMise("mise1", s_testConfigDirectory);
        var mise2 = builder.AddMise("mise2", s_testConfigDirectory);
        var app = builder.AddExecutable("app", "node", s_testConfigDirectory).WithMiseEnvironment(mise1);

        Assert.Throws<InvalidOperationException>(() => app.WithMiseEnvironment(mise2));
    }

    [Fact]
    public async Task AddMiseTaskRunsTaskViaMiseRun()
    {
        using var scope = new MiseResolverHookScope(findCommandOnPath: _ => "/usr/local/bin/mise");

        var builder = DistributedApplication.CreateBuilder();
        var mise = builder.AddMise("mise", s_testConfigDirectory);
        var task = mise.AddMiseTask("build-frontend", taskName: "build", "--release");

        using var app = builder.Build();
        await MiseTestHelpers.RunMiseBeforeStartAsync(app);

        Assert.Equal("/usr/local/bin/mise", task.Resource.Command);
        Assert.Equal(mise.Resource.ConfigDirectory, task.Resource.WorkingDirectory);
        Assert.Same(mise.Resource, task.Resource.Parent);

        var args = await task.Resource.GetArgumentListAsync();
        Assert.Equal(["run", "build", "--", "--release"], args);

        var wait = Assert.Single(task.Resource.Annotations.OfType<WaitAnnotation>());
        Assert.Same(mise.Resource, wait.Resource);
        Assert.Equal(WaitType.WaitForCompletion, wait.WaitType);
    }

    [Fact]
    public async Task WithMiseEnvironmentRewritesBareCommandThroughMiseExec()
    {
        using var scope = new MiseResolverHookScope(findCommandOnPath: _ => "/usr/local/bin/mise");

        var builder = DistributedApplication.CreateBuilder();
        var mise = builder.AddMise("mise", s_testConfigDirectory);
        var app = builder.AddExecutable("app", "node", s_testConfigDirectory, "server.js")
            .WithMiseEnvironment(mise);

        using var distributedApp = builder.Build();
        await MiseTestHelpers.RunMiseBeforeStartAsync(distributedApp);

        // Bare commands cannot resolve against the injected PATH (the orchestrator uses the app
        // host's PATH), so the launch is wrapped with `mise exec`.
        Assert.Equal("/usr/local/bin/mise", app.Resource.Command);
        var args = await app.Resource.GetArgumentListAsync();
        Assert.Equal(["exec", "--", "node", "server.js"], args);
    }

    [Fact]
    public async Task WithMiseEnvironmentLeavesExplicitCommandPathsUntouched()
    {
        using var scope = new MiseResolverHookScope(findCommandOnPath: _ => "/usr/local/bin/mise");

        var builder = DistributedApplication.CreateBuilder();
        var mise = builder.AddMise("mise", s_testConfigDirectory);
        var app = builder.AddExecutable("app", "/usr/bin/python3", s_testConfigDirectory, "main.py")
            .WithMiseEnvironment(mise);

        using var distributedApp = builder.Build();
        await MiseTestHelpers.RunMiseBeforeStartAsync(distributedApp);

        Assert.Equal("/usr/bin/python3", app.Resource.Command);
        var args = await app.Resource.GetArgumentListAsync();
        Assert.Equal(["main.py"], args);
    }

    [Fact]
    public async Task WithMiseExecRewritesCommandAndPrependsArgs()
    {
        using var scope = new MiseResolverHookScope(findCommandOnPath: _ => "/usr/local/bin/mise");

        var builder = DistributedApplication.CreateBuilder();
        var mise = builder.AddMise("mise", s_testConfigDirectory);
        var app = builder.AddExecutable("app", "node", s_testConfigDirectory, "server.js")
            .WithMiseExec(mise);

        using var distributedApp = builder.Build();
        await MiseTestHelpers.RunMiseBeforeStartAsync(distributedApp);

        Assert.Equal("/usr/local/bin/mise", app.Resource.Command);
        var args = await app.Resource.GetArgumentListAsync();
        Assert.Equal(["exec", "--", "node", "server.js"], args);
    }

    [Fact]
    public async Task WithMiseExecAddsChangeDirectoryWhenWorkingDirectoryIsOutsideConfigDirectory()
    {
        using var scope = new MiseResolverHookScope(findCommandOnPath: _ => "/usr/local/bin/mise");

        var builder = DistributedApplication.CreateBuilder();
        var configDirectory = Path.Combine(s_testConfigDirectory, "mise-config");
        Directory.CreateDirectory(configDirectory);
        var workingDirectory = Path.Combine(s_testConfigDirectory, "elsewhere");
        Directory.CreateDirectory(workingDirectory);

        var mise = builder.AddMise("mise", configDirectory);
        var app = builder.AddExecutable("app", "node", workingDirectory, "server.js")
            .WithMiseExec(mise);

        using var distributedApp = builder.Build();
        await MiseTestHelpers.RunMiseBeforeStartAsync(distributedApp);

        var args = await app.Resource.GetArgumentListAsync();
        Assert.Equal(["-C", mise.Resource.ConfigDirectory, "exec", "--", "node", "server.js"], args);
    }

    [Fact]
    public async Task BindingPropagatesToChildSetupResources()
    {
        // Mirrors the JavaScript integration shape: a hidden `{name}-installer` child running
        // `npm install` with a parent relationship to the app resource.
        using var scope = new MiseResolverHookScope(
            findCommandOnPath: _ => "/usr/local/bin/mise",
            runMiseEnvJson: (_, _, _) => Task.FromResult("""{"PATH": "/mise/installs/node/22/bin:/usr/bin"}"""));

        var builder = DistributedApplication.CreateBuilder();
        var mise = builder.AddMise("mise", s_testConfigDirectory);
        var app = builder.AddExecutable("app", "npm", s_testConfigDirectory, "run", "dev");
        var installer = builder.AddExecutable("app-installer", "npm", s_testConfigDirectory, "install")
            .WithParentRelationship(app);
        app.WaitForCompletion(installer);
        app.WithMiseEnvironment(mise);

        using var distributedApp = builder.Build();
        await MiseTestHelpers.RunMiseBeforeStartAsync(distributedApp);

        // The installer child is bound, waits for mise install, and is wrapped with mise exec.
        Assert.Equal("/usr/local/bin/mise", installer.Resource.Command);
        var installerArgs = await installer.Resource.GetArgumentListAsync();
        Assert.Equal(["exec", "--", "npm", "install"], installerArgs);
        Assert.Contains(installer.Resource.Annotations.OfType<WaitAnnotation>(),
            w => ReferenceEquals(w.Resource, mise.Resource) && w.WaitType == WaitType.WaitForCompletion);

        var installerEnv = await installer.Resource.GetEnvironmentVariablesAsync();
        Assert.Equal("1", installerEnv["MISE_YES"]);
    }

    [Fact]
    public async Task BindingPropagatesToColocatedWaitTargets()
    {
        // Mirrors the Go integration shape: a `{name}-mod-tidy` sibling (no parent relationship)
        // sharing the app's working directory that the app waits on.
        using var scope = new MiseResolverHookScope(findCommandOnPath: _ => "/usr/local/bin/mise");

        var builder = DistributedApplication.CreateBuilder();
        var mise = builder.AddMise("mise", s_testConfigDirectory);
        var app = builder.AddExecutable("api", "go", s_testConfigDirectory, "run", ".");
        var tidy = builder.AddExecutable("api-mod-tidy", "go", s_testConfigDirectory, "mod", "tidy");
        app.WaitForCompletion(tidy);

        var otherDirectory = Path.Combine(s_testConfigDirectory, "elsewhere-go");
        Directory.CreateDirectory(otherDirectory);
        var unrelated = builder.AddExecutable("unrelated", "go", otherDirectory, "test");
        app.WaitForCompletion(unrelated);

        app.WithMiseEnvironment(mise);

        using var distributedApp = builder.Build();
        await MiseTestHelpers.RunMiseBeforeStartAsync(distributedApp);

        // The colocated wait target is wrapped and waits on mise.
        Assert.Equal("/usr/local/bin/mise", tidy.Resource.Command);
        var tidyArgs = await tidy.Resource.GetArgumentListAsync();
        Assert.Equal(["exec", "--", "go", "mod", "tidy"], tidyArgs);
        Assert.Contains(tidy.Resource.Annotations.OfType<WaitAnnotation>(),
            w => ReferenceEquals(w.Resource, mise.Resource));

        // A wait target in a different directory is unrelated and untouched.
        Assert.Equal("go", unrelated.Resource.Command);
        Assert.DoesNotContain(unrelated.Resource.Annotations.OfType<WaitAnnotation>(),
            w => ReferenceEquals(w.Resource, mise.Resource));
    }

    [Fact]
    public async Task AddMisePrependsShimsDirectoryToProcessPath()
    {
        // Project resources are launched by the orchestrator with a hardcoded `dotnet` command
        // resolved against the app host's PATH; the shims directory makes that resolve to the
        // mise-managed SDK.
        using var scope = new MiseResolverHookScope(findCommandOnPath: _ => "/usr/local/bin/mise");

        var originalPath = Environment.GetEnvironmentVariable("PATH");
        var originalDataDir = Environment.GetEnvironmentVariable("MISE_DATA_DIR");
        var dataDir = Path.Combine(s_testConfigDirectory, "mise-data");
        try
        {
            Environment.SetEnvironmentVariable("MISE_DATA_DIR", dataDir);

            var builder = DistributedApplication.CreateBuilder();
            builder.AddMise("mise", s_testConfigDirectory);

            using var app = builder.Build();
            await MiseTestHelpers.RunMiseBeforeStartAsync(app);

            var expectedShims = Path.Combine(dataDir, "shims");
            Assert.StartsWith(expectedShims + Path.PathSeparator, Environment.GetEnvironmentVariable("PATH"));

            // Idempotent: a second pass does not duplicate the entry.
            await MiseTestHelpers.RunMiseBeforeStartAsync(app);
            var entries = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator);
            Assert.Single(entries, e => e == expectedShims);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Environment.SetEnvironmentVariable("MISE_DATA_DIR", originalDataDir);
        }
    }
}
