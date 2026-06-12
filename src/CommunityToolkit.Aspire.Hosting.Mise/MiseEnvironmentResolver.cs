using System.Diagnostics;
using System.Text.Json;
using Aspire.Hosting;

namespace CommunityToolkit.Aspire.Hosting.Mise;

/// <summary>
/// Locates the mise executable and resolves the environment mise produces for a configuration
/// directory by running <c>mise env --json</c>.
/// </summary>
internal static class MiseEnvironmentResolver
{
    // Test hooks: unit tests swap these (via InternalsVisibleTo) to avoid spawning processes.
    internal static Func<string, string?> FindCommandOnPath { get; set; } = FindCommandOnPathCore;
    internal static Func<string, string, CancellationToken, Task<string>> RunMiseEnvJson { get; set; } = RunMiseEnvJsonCoreAsync;

    /// <summary>
    /// Finds the full path to the mise executable on PATH (PATHEXT-aware on Windows), or null.
    /// </summary>
    public static string? FindMiseExecutable() => FindCommandOnPath("mise");

    /// <summary>
    /// Runs <c>mise env --json</c> in <paramref name="configDirectory"/> and returns the resolved
    /// environment variables, including the complete PATH with mise tool directories prepended.
    /// </summary>
    public static async Task<Dictionary<string, string>> GetEnvironmentAsync(string miseExecutable, string configDirectory, CancellationToken cancellationToken)
    {
        var json = await RunMiseEnvJson(miseExecutable, configDirectory, cancellationToken).ConfigureAwait(false);

        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        using var document = JsonDocument.Parse(json);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                environment[property.Name] = property.Value.GetString()!;
            }
        }

        return environment;
    }

    private static string? FindCommandOnPathCore(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var extensions = OperatingSystem.IsWindows()
            ? Environment.GetEnvironmentVariable("PATHEXT")?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [".EXE", ".CMD", ".BAT"]
            : [string.Empty];

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory, command + extension.ToLowerInvariant());
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static async Task<string> RunMiseEnvJsonCoreAsync(string miseExecutable, string configDirectory, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(miseExecutable)
        {
            WorkingDirectory = configDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("env");
        startInfo.ArgumentList.Add("--json");
        startInfo.Environment["MISE_YES"] = "1";
        startInfo.Environment["MISE_TRUSTED_CONFIG_PATHS"] = configDirectory;

        using var process = Process.Start(startInfo)
            ?? throw new DistributedApplicationException($"Failed to start '{miseExecutable} env --json'.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new DistributedApplicationException(
                $"'mise env --json' in '{configDirectory}' failed with exit code {process.ExitCode}: {stderr.Trim()}");
        }

        return stdout;
    }
}
