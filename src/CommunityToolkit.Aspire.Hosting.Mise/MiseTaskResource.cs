namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A resource that represents a mise task (defined in <c>mise.toml</c> or as a file task)
/// executed via <c>mise run</c> in the parent environment's configuration directory.
/// </summary>
/// <remarks>
/// The task waits for the parent environment's <c>mise install</c> to complete before running,
/// so the resource intentionally does not implement <see cref="IResourceWithParent{T}"/>
/// (a resource cannot wait for its <c>IResourceWithParent</c> parent); the dashboard nesting
/// comes from the parent relationship annotation instead.
/// </remarks>
/// <param name="name">The name of the resource.</param>
/// <param name="parent">The parent mise environment resource.</param>
public class MiseTaskResource(string name, MiseResource parent)
    : ExecutableResource(name, "mise", parent.ConfigDirectory)
{
    /// <summary>
    /// Gets the parent mise environment resource.
    /// </summary>
    public MiseResource Parent { get; } = parent;
}
