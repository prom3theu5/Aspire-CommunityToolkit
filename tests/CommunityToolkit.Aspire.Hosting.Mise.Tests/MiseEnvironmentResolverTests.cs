using Aspire.Hosting;
using CommunityToolkit.Aspire.Hosting.Mise;
using CommunityToolkit.Aspire.Testing;

namespace CommunityToolkit.Aspire.Hosting.Mise.Tests;

[Collection("MiseResolverHooks")]
public class MiseEnvironmentResolverTests
{
    private const string CannedEnvJson =
        """
        {
          "PATH": "/home/user/.local/share/mise/installs/node/22.0.0/bin:/usr/bin",
          "FOO": "bar",
          "NUMBER": 42
        }
        """;

    [Fact]
    public async Task GetEnvironmentAsyncParsesStringValuesOnly()
    {
        using var scope = new MiseResolverHookScope(runMiseEnvJson: (_, _, _) => Task.FromResult(CannedEnvJson));

        var env = await MiseEnvironmentResolver.GetEnvironmentAsync("mise", "/src/app", CancellationToken.None);

        Assert.Equal("/home/user/.local/share/mise/installs/node/22.0.0/bin:/usr/bin", env["PATH"]);
        Assert.Equal("bar", env["FOO"]);
        Assert.DoesNotContain("NUMBER", env.Keys);
    }

    [Fact]
    public async Task MiseEnvironmentIsInjectedAndResolvedOnceAcrossConsumers()
    {
        var invocations = 0;
        using var scope = new MiseResolverHookScope(
            findCommandOnPath: _ => "/usr/local/bin/mise",
            runMiseEnvJson: (_, _, _) =>
            {
                Interlocked.Increment(ref invocations);
                return Task.FromResult(CannedEnvJson);
            });

        var builder = DistributedApplication.CreateBuilder();
        var mise = builder.AddMise("mise", AppContext.BaseDirectory);
        var app1 = builder.AddExecutable("app1", "node", AppContext.BaseDirectory).WithMiseEnvironment(mise);
        var app2 = builder.AddExecutable("app2", "node", AppContext.BaseDirectory).WithMiseEnvironment(mise);

        var env1 = await app1.Resource.GetEnvironmentVariablesAsync();
        var env2 = await app2.Resource.GetEnvironmentVariablesAsync();

        Assert.Equal("bar", env1["FOO"]);
        Assert.Equal("bar", env2["FOO"]);
        Assert.StartsWith("/home/user/.local/share/mise/installs", env1["PATH"]);
        Assert.Equal(1, invocations);
    }

    [Fact]
    public async Task InvalidatingCacheResolvesAgain()
    {
        var invocations = 0;
        using var scope = new MiseResolverHookScope(runMiseEnvJson: (_, _, _) =>
        {
            Interlocked.Increment(ref invocations);
            return Task.FromResult(CannedEnvJson);
        });

        var mise = new MiseResource("mise", "/src/app");

        await mise.GetMiseEnvironmentAsync(CancellationToken.None);
        await mise.GetMiseEnvironmentAsync(CancellationToken.None);
        Assert.Equal(1, invocations);

        mise.InvalidateEnvironmentCache();
        await mise.GetMiseEnvironmentAsync(CancellationToken.None);
        Assert.Equal(2, invocations);
    }

    [Fact]
    public async Task FailedResolutionIsEvictedAndRetried()
    {
        var invocations = 0;
        using var scope = new MiseResolverHookScope(runMiseEnvJson: (_, _, _) =>
        {
            return Interlocked.Increment(ref invocations) == 1
                ? Task.FromException<string>(new DistributedApplicationException("mise env failed"))
                : Task.FromResult(CannedEnvJson);
        });

        var mise = new MiseResource("mise", "/src/app");

        await Assert.ThrowsAsync<DistributedApplicationException>(() => mise.GetMiseEnvironmentAsync(CancellationToken.None));

        var env = await mise.GetMiseEnvironmentAsync(CancellationToken.None);
        Assert.Equal("bar", env["FOO"]);
        Assert.Equal(2, invocations);
    }

    [RequiresMiseFact]
    public async Task ResolveEnvironmentFromRealMise()
    {
        var directory = Directory.CreateTempSubdirectory("aspire-mise-test");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "mise.toml"),
                """
                [env]
                FOO = "bar"
                """);

            var misePath = MiseEnvironmentResolver.FindMiseExecutable();
            Assert.NotNull(misePath);

            var env = await MiseEnvironmentResolver.GetEnvironmentAsync(misePath, directory.FullName, CancellationToken.None);

            Assert.Equal("bar", env["FOO"]);
            Assert.True(env.ContainsKey("PATH"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
