using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

[Collection("WorkerHostProcessHarness")]
public sealed class WorkerHostProcessHarnessTests
{
    [Fact]
    public void WorkerHost_HealthJson_Mode_Returns_ProcessLevel_Runtime_Report()
    {
        var repoRoot = FindRepoRoot();
        var projectPath = Path.Combine(repoRoot, "src", "BIM765T.Revit.WorkerHost", "BIM765T.Revit.WorkerHost.csproj");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\" -c Release -- --health-json",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var stdout = process!.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(120000), "WorkerHost --health-json did not exit in time.");
        Assert.True(process.ExitCode == 0 || process.ExitCode == 1, $"Unexpected exit code {process.ExitCode}. stderr: {stderr}");
        Assert.False(string.IsNullOrWhiteSpace(stdout));

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("GeneratedUtc", out _));
        Assert.True(root.TryGetProperty("StateRootPath", out _));
        Assert.True(root.TryGetProperty("EventStorePath", out _));
        Assert.True(root.TryGetProperty("RuntimeTopology", out var topology));
        Assert.Equal("workerhost_public_control_plane + revit_private_kernel", topology.GetString());
        Assert.True(root.TryGetProperty("StandaloneChatReady", out _));
        Assert.True(root.TryGetProperty("LiveRevitReady", out _));
        Assert.True(root.TryGetProperty("ReadinessSummary", out var readiness));
        Assert.False(string.IsNullOrWhiteSpace(readiness.GetString()));
    }

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        var directory = new DirectoryInfo(current);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "BIM765T.Revit.Agent.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root for WorkerHost process harness tests.");
    }
}
