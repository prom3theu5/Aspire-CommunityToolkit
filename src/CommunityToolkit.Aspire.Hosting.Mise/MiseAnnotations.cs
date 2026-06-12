using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;

namespace CommunityToolkit.Aspire.Hosting.Mise;

/// <summary>
/// Marks a resource as having its environment provided by a mise environment resource.
/// Used for idempotency, for mutual exclusion with exec wrapping, and to discover
/// mise-bound resources (e.g. by the refresh-environment dashboard command).
/// </summary>
[DebuggerDisplay("Type = {GetType().Name,nq}, Mise = {Mise.Name}")]
internal sealed class MiseEnvironmentAnnotation(MiseResource mise) : IResourceAnnotation
{
    /// <summary>
    /// The mise environment resource providing the environment.
    /// </summary>
    public MiseResource Mise { get; } = mise;
}

/// <summary>
/// Marks an executable resource as wrapped by <c>mise exec</c>. Used for idempotency and for
/// mutual exclusion with environment injection. <see cref="OriginalCommand"/> records the
/// command in effect when the rewrite happens so the resource launches as
/// <c>mise exec -- &lt;original command&gt; &lt;args&gt;</c>.
/// </summary>
[DebuggerDisplay("Type = {GetType().Name,nq}, Mise = {Mise.Name}")]
internal sealed class MiseExecAnnotation(MiseResource mise) : IResourceAnnotation
{
    /// <summary>
    /// The mise environment resource the command runs in.
    /// </summary>
    public MiseResource Mise { get; } = mise;

    /// <summary>
    /// The resource's command before it was rewritten to mise. Captured when the
    /// application starts, after all user configuration has been applied.
    /// </summary>
    public string? OriginalCommand { get; set; }
}

/// <summary>
/// Describes the mise task a <see cref="MiseTaskResource"/> runs:
/// <c>mise run &lt;TaskName&gt; &lt;Args&gt;</c>.
/// </summary>
[DebuggerDisplay("Type = {GetType().Name,nq}, Task = {TaskName}")]
internal sealed class MiseTaskAnnotation(string taskName, string[] args) : IResourceAnnotation
{
    /// <summary>
    /// The name of the task as defined in the mise configuration.
    /// </summary>
    public string TaskName { get; } = taskName;

    /// <summary>
    /// Additional arguments passed to the task after <c>--</c>.
    /// </summary>
    public string[] Args { get; } = args;
}

/// <summary>
/// Records the mise environment resource a consumer depends on for tool installation,
/// mirroring the installer annotations used by the JavaScript and Python integrations.
/// </summary>
[DebuggerDisplay("Type = {GetType().Name,nq}, Resource = {Resource.Name}")]
internal sealed class MiseInstallerAnnotation(MiseResource resource) : IResourceAnnotation
{
    /// <summary>
    /// The mise environment resource that installs tools for the annotated resource.
    /// </summary>
    public MiseResource Resource { get; } = resource;
}
