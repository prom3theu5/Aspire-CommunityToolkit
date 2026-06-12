using Aspire.Hosting;
using Aspire.Hosting.Lifecycle;
using CommunityToolkit.Aspire.Hosting.Mise;

namespace CommunityToolkit.Aspire.Hosting.Mise.Tests;

/// <summary>
/// Test classes that swap the static <see cref="MiseEnvironmentResolver"/> hooks or mutate
/// process-level environment variables belong to this collection so they never run in parallel
/// with each other.
/// </summary>
[CollectionDefinition("MiseResolverHooks", DisableParallelization = true)]
public sealed class MiseResolverHooksCollection;

/// <summary>
/// Swaps the <see cref="MiseEnvironmentResolver"/> process hooks for the lifetime of a test
/// and restores the originals on dispose.
/// </summary>
internal sealed class MiseResolverHookScope : IDisposable
{
    private readonly Func<string, string?> _originalFind = MiseEnvironmentResolver.FindCommandOnPath;
    private readonly Func<string, string, CancellationToken, Task<string>> _originalRun = MiseEnvironmentResolver.RunMiseEnvJson;

    public MiseResolverHookScope(
        Func<string, string?>? findCommandOnPath = null,
        Func<string, string, CancellationToken, Task<string>>? runMiseEnvJson = null)
    {
        if (findCommandOnPath is not null)
        {
            MiseEnvironmentResolver.FindCommandOnPath = findCommandOnPath;
        }

        if (runMiseEnvJson is not null)
        {
            MiseEnvironmentResolver.RunMiseEnvJson = runMiseEnvJson;
        }
    }

    public void Dispose()
    {
        MiseEnvironmentResolver.FindCommandOnPath = _originalFind;
        MiseEnvironmentResolver.RunMiseEnvJson = _originalRun;
    }
}

internal static class MiseTestHelpers
{
    /// <summary>
    /// Runs the mise before-start configuration the way the app host would: at start, after all
    /// other integrations' BeforeStartEvent handlers. Invoked directly on the subscriber so unit
    /// tests do not trip Aspire's DCP infrastructure handlers, which throw in environments
    /// without the Aspire tooling configured.
    /// </summary>
    public static async Task RunMiseBeforeStartAsync(DistributedApplication app)
    {
        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        foreach (var subscriber in app.Services
            .GetServices<IDistributedApplicationEventingSubscriber>()
            .OfType<MiseEventingSubscriber>())
        {
            await subscriber.OnBeforeStartAsync(appModel, CancellationToken.None);
        }
    }
}

/// <summary>
/// A fact that only runs when the mise CLI is installed on the machine.
/// </summary>
public sealed class RequiresMiseFactAttribute : FactAttribute
{
    public RequiresMiseFactAttribute(
        [System.Runtime.CompilerServices.CallerFilePath] string? sourceFilePath = null,
        [System.Runtime.CompilerServices.CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (MiseEnvironmentResolver.FindMiseExecutable() is null)
        {
            Skip = "mise is not installed on this machine.";
        }
    }
}
