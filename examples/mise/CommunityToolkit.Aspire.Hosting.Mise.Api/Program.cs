var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () =>
{
    var greeting = Environment.GetEnvironmentVariable("GREETING") ?? "GREETING was not set";
    return $"csharp: {greeting} (.NET {Environment.Version})";
});

app.Run();
