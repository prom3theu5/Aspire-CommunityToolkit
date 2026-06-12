using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Logging;

namespace CommunityToolkit.Aspire.Hosting.Mise;

/// <summary>
/// Configures all mise-related resources at application start. Eventing subscribers register their
/// subscriptions immediately before <c>BeforeStartEvent</c> is published — after every builder-time
/// subscription — so by the time this handler runs, other integrations (JavaScript, Go, Python, ...)
/// have finalized their resources' commands, including hidden setup resources like npm installers
/// and <c>go mod tidy</c> siblings. That makes this the only safe point to rewrite commands to
/// launch through mise.
/// </summary>
internal sealed class MiseEventingSubscriber(ILogger<MiseEventingSubscriber> logger) : IDistributedApplicationEventingSubscriber
{
    // Mirrors Aspire's internal KnownRelationshipTypes constants.
    private static class MiseRelationshipTypes
    {
        public const string Parent = "Parent";
        public const string WaitFor = "WaitFor";
    }

    public Task SubscribeAsync(IDistributedApplicationEventing eventing, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
    {
        if (executionContext.IsRunMode)
        {
            eventing.Subscribe<BeforeStartEvent>((@event, ct) => OnBeforeStartAsync(@event.Model, ct));
        }

        return Task.CompletedTask;
    }

    internal Task OnBeforeStartAsync(DistributedApplicationModel appModel, CancellationToken _)
    {
        if (appModel.Resources.OfType<MiseResource>().Any())
        {
            PrependMiseShimsToProcessPath();
        }

        // Resolve the mise executable and turn each environment resource into `mise install`.
        foreach (var mise in appModel.Resources.OfType<MiseResource>())
        {
            WarnIfNoConfigFile(mise);

            var misePath = MiseHostingExtensions.EnsureMiseExecutable(mise);
            SetCommand(mise, misePath);
            mise.Annotations.Add(new CommandLineArgsCallbackAnnotation(context =>
            {
                context.Args.Add("install");
                return Task.CompletedTask;
            }));
        }

        // Turn each task resource into `mise run <task> [-- args]`.
        foreach (var task in appModel.Resources.OfType<MiseTaskResource>())
        {
            var taskAnnotation = task.Annotations.OfType<MiseTaskAnnotation>().Last();
            SetCommand(task, MiseHostingExtensions.EnsureMiseExecutable(task.Parent));
            task.Annotations.Add(new CommandLineArgsCallbackAnnotation(context =>
            {
                context.Args.Add("run");
                context.Args.Add(taskAnnotation.TaskName);
                if (taskAnnotation.Args.Length > 0)
                {
                    context.Args.Add("--");
                    foreach (var arg in taskAnnotation.Args)
                    {
                        context.Args.Add(arg);
                    }
                }

                return Task.CompletedTask;
            }));
        }

        // Propagate the mise binding to setup resources other integrations created for the bound
        // resource (e.g. the JavaScript `{name}-installer` child running `npm install`, or Go's
        // `{name}-mod-tidy` sibling). Without this they would run their tools from the app host's
        // PATH, before `mise install` has completed.
        foreach (var resource in appModel.Resources.ToList())
        {
            var mise = GetBoundMise(resource);
            if (mise is null || resource is MiseResource or MiseTaskResource)
            {
                continue;
            }

            foreach (var setup in FindSetupResources(appModel, resource))
            {
                PropagateBinding(setup, mise);
            }
        }

        // Rewrite bound executables to launch through `mise exec` so their commands resolve to
        // mise-managed tools: always for WithMiseExec, and for bare command names (which the
        // orchestrator would otherwise resolve against the app host's PATH) with WithMiseEnvironment.
        foreach (var executable in appModel.Resources.OfType<ExecutableResource>())
        {
            if (executable is MiseResource or MiseTaskResource)
            {
                continue;
            }

            if (executable.TryGetLastAnnotation<MiseExecAnnotation>(out var execAnnotation))
            {
                WrapWithMiseExec(executable, execAnnotation.Mise, execAnnotation);
            }
            else if (executable.TryGetLastAnnotation<MiseEnvironmentAnnotation>(out var environmentAnnotation) &&
                MiseHostingExtensions.IsBareCommand(executable.Command))
            {
                WrapWithMiseExec(executable, environmentAnnotation.Mise, annotation: null);
            }
        }

        return Task.CompletedTask;
    }

    private static MiseResource? GetBoundMise(IResource resource)
    {
        if (resource.TryGetLastAnnotation<MiseEnvironmentAnnotation>(out var environmentAnnotation))
        {
            return environmentAnnotation.Mise;
        }

        if (resource.TryGetLastAnnotation<MiseExecAnnotation>(out var execAnnotation))
        {
            return execAnnotation.Mise;
        }

        return null;
    }

    /// <summary>
    /// Finds the setup executables belonging to <paramref name="resource"/>: resources that declare
    /// it as their parent (JavaScript installers), and executables it waits on that share its
    /// working directory (Go's mod tidy/vendor/download/vet siblings). Wait targets in other
    /// directories are unrelated resources and are left untouched.
    /// </summary>
    private static IEnumerable<ExecutableResource> FindSetupResources(DistributedApplicationModel appModel, IResource resource)
    {
        var waitTargets = resource.Annotations.OfType<WaitAnnotation>().Select(w => w.Resource).ToHashSet();

        foreach (var candidate in appModel.Resources.OfType<ExecutableResource>())
        {
            if (ReferenceEquals(candidate, resource) || candidate is MiseResource or MiseTaskResource)
            {
                continue;
            }

            var isChild = candidate.Annotations.OfType<ResourceRelationshipAnnotation>()
                .Any(r => r.Type == MiseRelationshipTypes.Parent && ReferenceEquals(r.Resource, resource));
            var isColocatedWaitTarget = waitTargets.Contains(candidate) &&
                resource is ExecutableResource owner &&
                string.Equals(candidate.WorkingDirectory, owner.WorkingDirectory, StringComparison.Ordinal);

            if (isChild || isColocatedWaitTarget)
            {
                yield return candidate;
            }
        }
    }

    private void PropagateBinding(ExecutableResource setup, MiseResource mise)
    {
        if (setup.TryGetLastAnnotation<MiseEnvironmentAnnotation>(out _) ||
            setup.TryGetLastAnnotation<MiseExecAnnotation>(out _))
        {
            return;
        }

        logger.LogDebug("Binding setup resource '{Setup}' to mise environment '{Mise}'.", setup.Name, mise.Name);

        setup.Annotations.Add(new MiseEnvironmentAnnotation(mise));
        setup.Annotations.Add(new MiseInstallerAnnotation(mise));
        setup.Annotations.Add(new EnvironmentCallbackAnnotation(async context =>
        {
            context.EnvironmentVariables["MISE_YES"] = "1";
            context.EnvironmentVariables["MISE_TRUSTED_CONFIG_PATHS"] = mise.ConfigDirectory;

            var environment = await mise.GetMiseEnvironmentAsync(context.CancellationToken).ConfigureAwait(false);
            foreach (var (key, value) in environment)
            {
                MiseHostingExtensions.SetEnvironmentVariable(context.EnvironmentVariables, key, value);
            }
        }));

        // The setup step must not run before the tools it uses are installed.
        if (!setup.Annotations.OfType<WaitAnnotation>().Any(w => ReferenceEquals(w.Resource, mise)))
        {
            setup.Annotations.Add(new WaitAnnotation(mise, WaitType.WaitForCompletion));
            setup.Annotations.Add(new ResourceRelationshipAnnotation(mise, MiseRelationshipTypes.WaitFor));
        }
    }

    /// <summary>
    /// Rewrites the executable to launch as <c>mise [-C dir] exec -- &lt;original command&gt; &lt;args&gt;</c>.
    /// </summary>
    private void WrapWithMiseExec(ExecutableResource executable, MiseResource mise, MiseExecAnnotation? annotation)
    {
        var misePath = MiseHostingExtensions.EnsureMiseExecutable(mise);
        if (string.Equals(executable.Command, misePath, StringComparison.Ordinal))
        {
            return;
        }

        var originalCommand = executable.Command;
        annotation?.OriginalCommand = originalCommand;
        SetCommand(executable, misePath);

        var changeDirectory = !IsPathContainedIn(executable.WorkingDirectory, mise.ConfigDirectory);
        if (changeDirectory)
        {
            logger.LogWarning(
                "Resource '{Resource}' runs through 'mise exec' but its working directory '{WorkingDirectory}' is outside the mise configuration directory '{ConfigDirectory}'. " +
                "'mise -C' will be used, which also changes the process working directory.",
                executable.Name, executable.WorkingDirectory, mise.ConfigDirectory);
        }

        // Registered last so it runs after all other arg callbacks; insert the wrapper prefix so
        // the final command line is: mise [-C dir] exec -- <command> <args>.
        executable.Annotations.Add(new CommandLineArgsCallbackAnnotation(context =>
        {
            var prefix = new List<object>();
            if (changeDirectory)
            {
                prefix.Add("-C");
                prefix.Add(mise.ConfigDirectory);
            }
            prefix.Add("exec");
            prefix.Add("--");
            prefix.Add(originalCommand);

            for (var i = 0; i < prefix.Count; i++)
            {
                context.Args.Insert(i, prefix[i]);
            }

            return Task.CompletedTask;
        }));
    }

    /// <summary>
    /// Prepends the mise shims directory to the app host process PATH. The orchestrator inherits
    /// the app host's environment and resolves commands against it at spawn time — including the
    /// hardcoded <c>dotnet</c> used to launch project resources, which the application model
    /// cannot rewrite. Shims are stable paths that mise (re)generates during <c>mise install</c>,
    /// and a bound project waits for the install to complete before it is spawned, so by the time
    /// resolution happens the shim execs the mise-managed tool. Tools not managed by mise have no
    /// shim and keep resolving to the host installation.
    /// </summary>
    private void PrependMiseShimsToProcessPath()
    {
        var shimsDirectory = Path.Combine(GetMiseDataDirectory(), "shims");
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (path.Split(Path.PathSeparator).Any(entry => string.Equals(entry, shimsDirectory, comparison)))
        {
            return;
        }

        Environment.SetEnvironmentVariable("PATH", shimsDirectory + Path.PathSeparator + path);
        logger.LogInformation(
            "Added the mise shims directory '{ShimsDirectory}' to the app host PATH so orchestrator-launched commands (including 'dotnet' for project resources) resolve to mise-managed tools.",
            shimsDirectory);
    }

    private static string GetMiseDataDirectory()
    {
        if (Environment.GetEnvironmentVariable("MISE_DATA_DIR") is { Length: > 0 } dataDir)
        {
            return dataDir;
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "mise");
        }

        if (Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdgDataHome)
        {
            return Path.Combine(xdgDataHome, "mise");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "mise");
    }

    private static void SetCommand(ExecutableResource executable, string command)
    {
        executable.Annotations.OfType<ExecutableAnnotation>().Last().Command = command;
    }

    private void WarnIfNoConfigFile(MiseResource resource)
    {
        string[] configFileNames = ["mise.toml", "mise.local.toml", ".mise.toml", ".tool-versions"];
        if (!configFileNames.Any(f => File.Exists(Path.Combine(resource.ConfigDirectory, f))))
        {
            logger.LogWarning(
                "No mise configuration file (mise.toml, mise.local.toml or .tool-versions) was found in '{ConfigDirectory}'. " +
                "mise resolves configuration from parent directories, which may not be what you intend.",
                resource.ConfigDirectory);
        }
    }

    private static bool IsPathContainedIn(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "."
            || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative));
    }
}
