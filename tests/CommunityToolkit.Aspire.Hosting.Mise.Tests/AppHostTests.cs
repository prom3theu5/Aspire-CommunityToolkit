using CommunityToolkit.Aspire.Testing;

namespace CommunityToolkit.Aspire.Hosting.Mise.Tests;

public class AppHostTests(AspireIntegrationTestFixture<Projects.CommunityToolkit_Aspire_Hosting_Mise_AppHost> fixture)
    : IClassFixture<AspireIntegrationTestFixture<Projects.CommunityToolkit_Aspire_Hosting_Mise_AppHost>>
{
    [RequiresMiseFact]
    public async Task MiseInstallRunsToCompletion()
    {
        await fixture.ResourceNotificationService
            .WaitForResourceAsync("mise", KnownResourceStates.Finished)
            .WaitAsync(TimeSpan.FromMinutes(5));
    }

    [RequiresMiseFact]
    public async Task MiseTaskRunsToCompletion()
    {
        await fixture.ResourceNotificationService
            .WaitForResourceAsync("show-versions", KnownResourceStates.Finished)
            .WaitAsync(TimeSpan.FromMinutes(5));
    }

    [RequiresMiseFact]
    public async Task ResourceWithMiseEnvironmentRespondsWithMiseValues()
    {
        var httpClient = fixture.CreateHttpClient("web");

        await fixture.ResourceNotificationService
            .WaitForResourceHealthyAsync("web")
            .WaitAsync(TimeSpan.FromMinutes(5));

        var body = await httpClient.GetStringAsync("/");

        Assert.StartsWith("Hello from mise!", body);
        Assert.Contains("node v22.", body);
    }

    [RequiresMiseFact]
    public async Task ResourceWithMiseExecRespondsWithMiseValues()
    {
        var httpClient = fixture.CreateHttpClient("web-exec");

        await fixture.ResourceNotificationService
            .WaitForResourceHealthyAsync("web-exec")
            .WaitAsync(TimeSpan.FromMinutes(5));

        var body = await httpClient.GetStringAsync("/");

        Assert.StartsWith("Hello from mise!", body);
        Assert.Contains("node v22.", body);
    }

    [RequiresMiseFact]
    public async Task JavaScriptAppAndItsNpmInstallerUseMiseNode()
    {
        // The hidden npm installer child is bound to mise automatically and must complete first.
        await fixture.ResourceNotificationService
            .WaitForResourceAsync("frontend-installer", KnownResourceStates.Finished)
            .WaitAsync(TimeSpan.FromMinutes(5));

        var httpClient = fixture.CreateHttpClient("frontend");

        await fixture.ResourceNotificationService
            .WaitForResourceHealthyAsync("frontend")
            .WaitAsync(TimeSpan.FromMinutes(5));

        var body = await httpClient.GetStringAsync("/");

        Assert.Contains("js: Hello from mise!", body);
        Assert.Contains("node v22.", body);
    }

    [RequiresMiseFact]
    public async Task GoAppAndItsModTidySiblingUseMiseGo()
    {
        // The `go mod tidy` sibling is bound to mise automatically and must complete first.
        await fixture.ResourceNotificationService
            .WaitForResourceAsync("goapi-mod-tidy", KnownResourceStates.Finished)
            .WaitAsync(TimeSpan.FromMinutes(5));

        var httpClient = fixture.CreateHttpClient("goapi");

        await fixture.ResourceNotificationService
            .WaitForResourceHealthyAsync("goapi")
            .WaitAsync(TimeSpan.FromMinutes(10));

        var body = await httpClient.GetStringAsync("/");

        Assert.StartsWith("go: Hello from mise!", body);
        Assert.Contains("go1.24", body);
    }

    [RequiresMiseFact]
    public async Task ProjectResourceUsesMiseDotnetAndEnvironment()
    {
        var httpClient = fixture.CreateHttpClient("api");

        await fixture.ResourceNotificationService
            .WaitForResourceHealthyAsync("api")
            .WaitAsync(TimeSpan.FromMinutes(10));

        var body = await httpClient.GetStringAsync("/");

        Assert.StartsWith("csharp: Hello from mise!", body);
        Assert.Contains(".NET 10.", body);
    }
}
