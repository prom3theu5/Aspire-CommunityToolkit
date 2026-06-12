// The mise environment is rooted at the example directory (mise.toml declares node 22, go 1.24
// and dotnet 10); every app directory below it inherits the config. `mise install` runs before
// any dependent resource starts. mise itself must already be installed and on the PATH.
var builder = DistributedApplication.CreateBuilder(args);

var mise = builder.AddMise("mise", "../");

// Runs `mise run show-versions` after `mise install` completes.
mise.AddMiseTask("show-versions");

// Plain executable: gets the mise-resolved environment, and since `node` is a bare command it is
// launched through `mise exec` so it resolves to the mise-managed node.
builder.AddExecutable("web", "node", "../app", "server.js")
    .WithMiseEnvironment(mise)
    .WithHttpEndpoint(env: "PORT")
    .WithHttpHealthCheck("/");

// Same app, explicitly launched through `mise exec`.
builder.AddExecutable("web-exec", "node", "../app", "server.js")
    .WithMiseExec(mise)
    .WithHttpEndpoint(env: "PORT")
    .WithHttpHealthCheck("/");

// JavaScript app: the hidden npm installer child is automatically bound to mise too, so
// `npm install` runs with the mise-managed node after `mise install` completes.
#pragma warning disable ASPIREJAVASCRIPT001
builder.AddJavaScriptApp("frontend", "../jsapp")
    .WithMiseEnvironment(mise)
    .WithHttpEndpoint(env: "PORT")
    .WithHttpHealthCheck("/");
#pragma warning restore ASPIREJAVASCRIPT001

// Go app: the `go mod tidy` sibling is automatically bound to mise too, so it runs with the
// mise-managed go toolchain after `mise install` completes.
builder.AddGoApp("goapi", "../goapp")
    .WithModTidy()
    .WithMiseEnvironment(mise)
    .WithHttpEndpoint(env: "PORT")
    .WithHttpHealthCheck("/");

// C# project: launched via the mise shims `dotnet` (the mise-managed SDK), with the mise
// environment (GREETING, tool paths) injected.
builder.AddProject<Projects.CommunityToolkit_Aspire_Hosting_Mise_Api>("api")
    .WithMiseEnvironment(mise)
    .WithHttpHealthCheck("/");

builder.Build().Run();
