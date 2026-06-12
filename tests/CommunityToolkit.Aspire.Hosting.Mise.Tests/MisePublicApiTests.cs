using Aspire.Hosting;

namespace CommunityToolkit.Aspire.Hosting.Mise.Tests;

public class MisePublicApiTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CtorMiseResourceShouldThrowWhenNameIsNullOrEmpty(bool isNull)
    {
        var name = isNull ? null! : string.Empty;

        var action = () => new MiseResource(name, "/src/app");

        var exception = isNull
            ? Assert.Throws<ArgumentNullException>(action)
            : Assert.Throws<ArgumentException>(action);
        Assert.Equal(nameof(name), exception.ParamName);
    }

    [Fact]
    public void CtorMiseResourceShouldThrowWhenConfigDirectoryIsNull()
    {
        var action = () => new MiseResource("mise", configDirectory: null!);

        // ExecutableResource validates the working directory argument.
        Assert.Throws<ArgumentNullException>(action);
    }

    [Fact]
    public void AddMiseShouldThrowWhenBuilderIsNull()
    {
        IDistributedApplicationBuilder builder = null!;

        var action = () => builder.AddMise("mise", "/src/app");

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public void AddMiseShouldThrowWhenNameIsNull()
    {
        var builder = DistributedApplication.CreateBuilder();

        var action = () => builder.AddMise(null!, "/src/app");

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddMiseShouldThrowWhenConfigDirectoryIsNullOrEmpty(bool isNull)
    {
        var builder = DistributedApplication.CreateBuilder();
        var configDirectory = isNull ? null! : string.Empty;

        var action = () => builder.AddMise("mise", configDirectory);

        var exception = isNull
            ? Assert.Throws<ArgumentNullException>(action)
            : Assert.Throws<ArgumentException>(action);
        Assert.Equal(nameof(configDirectory), exception.ParamName);
    }

    [Fact]
    public void WithMiseEnvironmentShouldThrowWhenBuilderIsNull()
    {
        IResourceBuilder<ExecutableResource> builder = null!;
        var appBuilder = DistributedApplication.CreateBuilder();
        var mise = appBuilder.AddMise("mise", "/src/app");

        var action = () => builder.WithMiseEnvironment(mise);

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public void WithMiseEnvironmentShouldThrowWhenMiseIsNull()
    {
        var builder = DistributedApplication.CreateBuilder();
        var executable = builder.AddExecutable("app", "echo", "/src/app");

        var action = () => executable.WithMiseEnvironment(null!);

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal("mise", exception.ParamName);
    }

    [Fact]
    public void AddMiseTaskShouldThrowWhenBuilderIsNull()
    {
        IResourceBuilder<MiseResource> builder = null!;

        var action = () => builder.AddMiseTask("build");

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public void AddMiseTaskShouldThrowWhenNameIsNull()
    {
        var builder = DistributedApplication.CreateBuilder();
        var mise = builder.AddMise("mise", "/src/app");

        var action = () => mise.AddMiseTask(null!);

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void WithMiseExecShouldThrowWhenBuilderIsNull()
    {
        IResourceBuilder<ExecutableResource> builder = null!;
        var appBuilder = DistributedApplication.CreateBuilder();
        var mise = appBuilder.AddMise("mise", "/src/app");

        var action = () => builder.WithMiseExec(mise);

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal("builder", exception.ParamName);
    }

    [Fact]
    public void WithMiseExecShouldThrowWhenMiseIsNull()
    {
        var builder = DistributedApplication.CreateBuilder();
        var executable = builder.AddExecutable("app", "echo", "/src/app");

        var action = () => executable.WithMiseExec(null!);

        var exception = Assert.Throws<ArgumentNullException>(action);
        Assert.Equal("mise", exception.ParamName);
    }
}
