using CommunityToolkit.Aspire.Hosting.Mise;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A resource that represents a mise (mise-en-place) environment rooted at a directory containing
/// a mise configuration file (<c>mise.toml</c>, <c>mise.local.toml</c> or <c>.tool-versions</c>).
/// </summary>
/// <remarks>
/// In run mode the resource executes <c>mise install</c> in the configuration directory and finishes
/// once all tools are installed. Resources configured with <c>WithMiseEnvironment</c> or
/// <c>WithMiseExec</c> wait for this resource to complete before starting. The resource is never
/// included in deployment artifacts; mise is a local development concern only.
/// </remarks>
/// <param name="name">The name of the resource.</param>
/// <param name="configDirectory">The fully qualified path to the directory containing the mise configuration.</param>
public class MiseResource(string name, string configDirectory)
    : ExecutableResource(name, "mise", configDirectory)
{
    private readonly object _environmentLock = new();
    private Task<Dictionary<string, string>>? _environment;

    /// <summary>
    /// Gets the fully qualified path to the directory containing the mise configuration.
    /// </summary>
    public string ConfigDirectory { get; } = configDirectory;

    /// <summary>
    /// The resolved full path to the mise executable. Set once at application start.
    /// </summary>
    internal string? MiseExecutablePath { get; set; }

    /// <summary>
    /// Resolves the environment produced by <c>mise env --json</c> for <see cref="ConfigDirectory"/>.
    /// The result is cached so that any number of consuming resources spawn a single mise process.
    /// A faulted or canceled resolution is evicted so the next caller retries.
    /// </summary>
    internal async Task<Dictionary<string, string>> GetMiseEnvironmentAsync(CancellationToken cancellationToken)
    {
        Task<Dictionary<string, string>> environmentTask;
        lock (_environmentLock)
        {
            environmentTask = _environment ??= MiseEnvironmentResolver.GetEnvironmentAsync(
                MiseExecutablePath ?? Command, ConfigDirectory, CancellationToken.None);
        }

        try
        {
            return await environmentTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (environmentTask.IsFaulted || environmentTask.IsCanceled)
        {
            lock (_environmentLock)
            {
                if (ReferenceEquals(_environment, environmentTask))
                {
                    _environment = null;
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Drops the cached <c>mise env</c> result so consuming resources pick up a freshly
    /// resolved environment the next time they start.
    /// </summary>
    internal void InvalidateEnvironmentCache()
    {
        lock (_environmentLock)
        {
            _environment = null;
        }
    }
}
