using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
using CommunityToolkit.Aspire.Hosting.Mise;
using CommunityToolkit.Aspire.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting;

/// <summary>
/// Provides extension methods for adding mise (mise-en-place) environments to an
/// <see cref="IDistributedApplicationBuilder"/>.
/// </summary>
public static class MiseHostingExtensions
{
    private const string MiseGettingStartedUrl = "https://mise.jdx.dev/getting-started.html";

    /// <summary>
    /// Adds a mise environment to the application model. In run mode the resource runs
    /// <c>mise install</c> in <paramref name="configDirectory"/> before any resource that
    /// depends on it starts. The mise CLI must already be installed and available on the PATH;
    /// it is never installed by Aspire.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/> to add the resource to.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="configDirectory">
    /// The directory containing the mise configuration (<c>mise.toml</c>, <c>mise.local.toml</c> or
    /// <c>.tool-versions</c>), relative to the app host directory or absolute.
    /// </param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// Mise is a local development concern: the resource is excluded from the manifest and never
    /// appears in deployment artifacts. The configuration directory is trusted non-interactively
    /// via <c>MISE_TRUSTED_CONFIG_PATHS</c> and <c>MISE_YES</c>.
    /// </remarks>
#pragma warning disable ASPIREATS001 // AspireExport is experimental
    [AspireExport]
#pragma warning restore ASPIREATS001
    public static IResourceBuilder<MiseResource> AddMise(this IDistributedApplicationBuilder builder, [ResourceName] string name, string configDirectory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentException.ThrowIfNullOrEmpty(configDirectory);

        configDirectory = PathNormalizer.NormalizePathForCurrentPlatform(Path.Combine(builder.AppHostDirectory, configDirectory));

        var resource = new MiseResource(name, configDirectory);
        var miseBuilder = builder.AddResource(resource)
            .WithIconName("Toolbox")
            .ExcludeFromManifest()
            .WithEnvironment("MISE_YES", "1")
            .WithEnvironment("MISE_TRUSTED_CONFIG_PATHS", configDirectory);

        if (builder.ExecutionContext.IsRunMode)
        {
            // The eventing subscriber performs all command rewriting after every other
            // integration's BeforeStartEvent handlers have run, so finalized commands
            // (npm installers, go mod siblings, ...) are observed.
            builder.Services.TryAddEventingSubscriber<MiseEventingSubscriber>();

            miseBuilder.WithRequiredCommand("mise", helpLink: MiseGettingStartedUrl);
            AddDashboardCommands(miseBuilder);
        }

        return miseBuilder;
    }

    /// <summary>
    /// Injects the environment resolved by the mise environment (<c>mise env</c>: tool paths on PATH
    /// and <c>[env]</c> variables) into the resource, and makes the resource wait for
    /// <c>mise install</c> to complete before starting.
    /// </summary>
    /// <typeparam name="T">The type of the resource.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="mise">The mise environment resource builder.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// <para>
    /// The orchestrator resolves an executable's command against the app host's own PATH, not the
    /// injected environment. For an <see cref="ExecutableResource"/> whose command is a bare name
    /// (e.g. <c>node</c>), the launch is therefore additionally wrapped with <c>mise exec</c> so the
    /// command resolves to the mise-managed tool. Commands given as explicit paths are left untouched.
    /// </para>
    /// <para>
    /// The binding is also propagated to setup resources created for this resource by other
    /// integrations — child resources such as JavaScript package installers, and executables this
    /// resource waits on that share its working directory, such as Go's <c>go mod tidy</c> — so
    /// those steps run with mise-managed tools after <c>mise install</c> completes.
    /// </para>
    /// </remarks>
#pragma warning disable ASPIREATS001 // AspireExport is experimental
#pragma warning disable ASPIREEXPORT009 // Name already contains the integration name; collision is not possible
    [AspireExport]
#pragma warning restore ASPIREEXPORT009
#pragma warning restore ASPIREATS001
    public static IResourceBuilder<T> WithMiseEnvironment<T>(this IResourceBuilder<T> builder, IResourceBuilder<MiseResource> mise)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(mise);

        if (builder.Resource.TryGetLastAnnotation<MiseEnvironmentAnnotation>(out var existing))
        {
            if (!ReferenceEquals(existing.Mise, mise.Resource))
            {
                throw new InvalidOperationException(
                    $"Resource '{builder.Resource.Name}' is already bound to mise environment '{existing.Mise.Name}'.");
            }

            return builder;
        }

        if (builder.Resource.TryGetLastAnnotation<MiseExecAnnotation>(out _))
        {
            throw new InvalidOperationException(
                $"Resource '{builder.Resource.Name}' is already configured with WithMiseExec. WithMiseEnvironment and WithMiseExec are mutually exclusive.");
        }

        builder.WithAnnotation(new MiseEnvironmentAnnotation(mise.Resource));
        builder.WithAnnotation(new MiseInstallerAnnotation(mise.Resource));

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            builder.WaitForCompletion(mise);

            // The mise config must be trusted non-interactively by any mise invocation the
            // resource (or its subprocesses) makes; `mise env` output does not include these.
            builder.WithEnvironment("MISE_YES", "1");
            builder.WithEnvironment("MISE_TRUSTED_CONFIG_PATHS", mise.Resource.ConfigDirectory);
        }

        builder.WithEnvironment(async context =>
        {
            if (!context.ExecutionContext.IsRunMode)
            {
                return;
            }

            var environment = await mise.Resource.GetMiseEnvironmentAsync(context.CancellationToken).ConfigureAwait(false);
            foreach (var (key, value) in environment)
            {
                SetEnvironmentVariable(context.EnvironmentVariables, key, value);
            }
        });

        return builder;
    }

    /// <summary>
    /// Adds a mise task (defined in the mise configuration) to the application model, executed via
    /// <c>mise run</c> in the environment's configuration directory after <c>mise install</c> completes.
    /// </summary>
    /// <param name="builder">The mise environment resource builder.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="taskName">The mise task to run. Defaults to <paramref name="name"/>.</param>
    /// <param name="args">Additional arguments passed to the task after <c>--</c>.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
#pragma warning disable ASPIREATS001 // AspireExport is experimental
    [AspireExport]
#pragma warning restore ASPIREATS001
    public static IResourceBuilder<MiseTaskResource> AddMiseTask(this IResourceBuilder<MiseResource> builder, [ResourceName] string name, string? taskName = null, params string[] args)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(args);

        var task = new MiseTaskResource(name, builder.Resource);
        var taskBuilder = builder.ApplicationBuilder.AddResource(task)
            .WithAnnotation(new MiseTaskAnnotation(taskName ?? name, args))
            .WithParentRelationship(builder)
            .ExcludeFromManifest()
            .WithEnvironment("MISE_YES", "1")
            .WithEnvironment("MISE_TRUSTED_CONFIG_PATHS", builder.Resource.ConfigDirectory);

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            taskBuilder.WaitForCompletion(builder);
        }

        return taskBuilder;
    }

    /// <summary>
    /// Rewrites the executable so it launches through <c>mise exec</c>, guaranteeing the resolved
    /// tool versions and environment without modifying the resource's environment variables, and
    /// makes the resource wait for <c>mise install</c> to complete before starting.
    /// </summary>
    /// <typeparam name="T">The type of the executable resource.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <param name="mise">The mise environment resource builder.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
#pragma warning disable ASPIREATS001 // AspireExport is experimental
#pragma warning disable ASPIREEXPORT009 // Name already contains the integration name; collision is not possible
    [AspireExport]
#pragma warning restore ASPIREEXPORT009
#pragma warning restore ASPIREATS001
    public static IResourceBuilder<T> WithMiseExec<T>(this IResourceBuilder<T> builder, IResourceBuilder<MiseResource> mise)
        where T : ExecutableResource
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(mise);

        if (builder.Resource.TryGetLastAnnotation<MiseExecAnnotation>(out var existing))
        {
            if (!ReferenceEquals(existing.Mise, mise.Resource))
            {
                throw new InvalidOperationException(
                    $"Resource '{builder.Resource.Name}' is already bound to mise environment '{existing.Mise.Name}'.");
            }

            return builder;
        }

        if (builder.Resource.TryGetLastAnnotation<MiseEnvironmentAnnotation>(out _))
        {
            throw new InvalidOperationException(
                $"Resource '{builder.Resource.Name}' is already configured with WithMiseEnvironment. WithMiseEnvironment and WithMiseExec are mutually exclusive.");
        }

        builder.WithAnnotation(new MiseExecAnnotation(mise.Resource));
        builder.WithAnnotation(new MiseInstallerAnnotation(mise.Resource));

        if (builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            builder.WaitForCompletion(mise);
            builder.WithEnvironment("MISE_YES", "1");
            builder.WithEnvironment("MISE_TRUSTED_CONFIG_PATHS", mise.Resource.ConfigDirectory);
        }

        return builder;
    }

    private static void AddDashboardCommands(IResourceBuilder<MiseResource> builder)
    {
        var resource = builder.Resource;

        builder.WithCommand(
            name: "reinstall-tools",
            displayName: "Reinstall tools",
            executeCommand: async context =>
            {
                resource.InvalidateEnvironmentCache();
                var commandService = context.ServiceProvider.GetRequiredService<ResourceCommandService>();
                return await commandService.ExecuteCommandAsync(resource, KnownResourceCommands.RestartCommand, context.CancellationToken).ConfigureAwait(false);
            },
            commandOptions: new CommandOptions
            {
                Description = "Re-runs 'mise install' and refreshes the cached mise environment.",
                ConfirmationMessage = "Re-run 'mise install' for this environment?",
                IconName = "ArrowSync",
            });

        builder.WithCommand(
            name: "refresh-environment",
            displayName: "Refresh environment",
            executeCommand: context =>
            {
                resource.InvalidateEnvironmentCache();

                var appModel = context.ServiceProvider.GetRequiredService<DistributedApplicationModel>();
                var boundResources = appModel.Resources
                    .Where(r => r.TryGetLastAnnotation<MiseEnvironmentAnnotation>(out var a) && ReferenceEquals(a.Mise, resource))
                    .Select(r => r.Name)
                    .ToArray();

                var message = boundResources.Length > 0
                    ? $"Environment refreshed. Restart {string.Join(", ", boundResources)} to pick up changes."
                    : "Environment refreshed.";
                return Task.FromResult(CommandResults.Success(message, string.Empty));
            },
            commandOptions: new CommandOptions
            {
                Description = "Clears the cached 'mise env' result so resources pick up a fresh environment on their next restart.",
                IconName = "ArrowClockwise",
            });
    }

    internal static string EnsureMiseExecutable(MiseResource mise)
    {
        if (mise.MiseExecutablePath is { } resolved)
        {
            return resolved;
        }

        var misePath = MiseEnvironmentResolver.FindMiseExecutable()
            ?? throw new DistributedApplicationException(MiseNotFoundMessage());
        mise.MiseExecutablePath = misePath;
        return misePath;
    }

    private static string MiseNotFoundMessage()
    {
        var install = OperatingSystem.IsWindows()
            ? "winget install jdx.mise"
            : OperatingSystem.IsMacOS()
                ? "brew install mise"
                : "your distribution's package manager (see https://mise.run)";

        return $"mise was not found on PATH. Install it with {install} and restart the app host. See {MiseGettingStartedUrl} for details.";
    }

    internal static void SetEnvironmentVariable(IDictionary<string, object> environmentVariables, string key, string value)
    {
        // On Windows environment variable names are case-insensitive; reuse the existing key
        // (e.g. "Path" vs "PATH") so we override rather than introduce a duplicate.
        var targetKey = key;
        if (OperatingSystem.IsWindows() && !environmentVariables.ContainsKey(key))
        {
            targetKey = environmentVariables.Keys.FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) ?? key;
        }

        environmentVariables[targetKey] = value;
    }

    internal static bool IsBareCommand(string command) =>
        !command.Contains('/') && !command.Contains('\\');
}
