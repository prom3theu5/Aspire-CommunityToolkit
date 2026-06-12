# CommunityToolkit.Aspire.Hosting.Mise library

Provides extension methods and resource definitions for an Aspire app host to configure a [mise](https://mise.jdx.dev) (mise-en-place) environment: installing the dev tools declared in `mise.toml` before resources start, injecting the mise-resolved environment into resources, running mise tasks, and wrapping executables with `mise exec`.

Mise is strictly a local development concern. None of the resources added by this package appear in deployment artifacts (manifest, Docker Compose, Kubernetes, Azure).

## Prerequisites

The mise CLI must already be installed and on the PATH — this package never installs mise itself. Install it with:

- Windows: `winget install jdx.mise`
- macOS: `brew install mise`
- Linux: your distribution's package manager, or see <https://mise.run>

## Getting started

### Install the package

In your app host project, install the package using the following command:

```dotnetcli
dotnet add package CommunityToolkit.Aspire.Hosting.Mise
```

### Example usage

Point `AddMise` at a directory containing a `mise.toml` (or `.tool-versions`) — typically the repository root so all app directories inherit it:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

// Runs `mise install` for ../mise.toml before dependents start.
var mise = builder.AddMise("mise", "../");

// Inject the mise environment (tool paths, [env] variables) into any resource type.
builder.AddExecutable("web", "node", "../app", "server.js")
    .WithMiseEnvironment(mise);

builder.AddProject<Projects.Api>("api")
    .WithMiseEnvironment(mise);          // runs on the mise-managed dotnet via mise shims

// Or launch through `mise exec` explicitly instead of environment injection.
builder.AddExecutable("worker", "node", "../app", "worker.js")
    .WithMiseExec(mise);

// Run a task from mise.toml as a resource (after `mise install` completes).
var build = mise.AddMiseTask("build");

builder.Build().Run();
```

The configuration directory is trusted non-interactively (`MISE_TRUSTED_CONFIG_PATHS`, `MISE_YES=1`), so the app host never blocks on a mise trust prompt.

### How commands are resolved

The orchestrator resolves a bare executable command (`node`, `npm`, `go`, ...) against the app host's own PATH, not the injected environment. `WithMiseEnvironment` therefore wraps such launches with `mise exec` so the command resolves to the mise-managed tool after `mise install` completes; commands given as explicit paths are left untouched. The binding also propagates to setup resources other integrations create — child resources such as JavaScript package installers, and executables the resource waits on that share its working directory, such as Go's `go mod tidy` — so those steps run with mise-managed tools too.

Project resources are launched by the orchestrator with a hardcoded `dotnet` command that the application model cannot rewrite. To support a mise-managed .NET SDK (`dotnet = "10"` in mise.toml), `AddMise` prepends the mise shims directory to the app host's PATH; commands are resolved when a resource is spawned — after its wait on `mise install` completes — so the `dotnet` shim exists by then and execs the mise-managed SDK. Tools not managed by mise have no shim and keep resolving to the host installation. Note that when a project is launched by an IDE debug session (rather than as a process), the IDE controls the SDK used.

### Dashboard commands

The mise resource exposes two commands in the dashboard:

- **Reinstall tools** — re-runs `mise install` and refreshes the cached environment.
- **Refresh environment** — clears the cached `mise env` result; mise-bound resources pick up the fresh environment on their next restart.

## Additional Information

https://learn.microsoft.com/dotnet/aspire/community-toolkit/overview

## Feedback & contributing

https://github.com/CommunityToolkit/Aspire
