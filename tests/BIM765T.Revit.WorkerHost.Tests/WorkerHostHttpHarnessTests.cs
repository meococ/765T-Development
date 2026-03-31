using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

[Collection("WorkerHostProcessHarness")]
public sealed class WorkerHostHttpHarnessTests
{
    private static readonly TimeSpan HttpWaitBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan HttpCallBudget = TimeSpan.FromSeconds(20);

    [Fact(Timeout = 30000)]
    public async Task WorkerHost_Process_Exposes_Status_Endpoint_And_Runtime_Topology()
    {
        var repoRoot = FindRepoRoot();
        var exePath = WorkerHostProcessHarnessSupport.PrepareIsolatedExePath(repoRoot);
        Assert.True(File.Exists(exePath), $"WorkerHost executable not found at {exePath}");

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        try
        {
            using var timeoutCts = new CancellationTokenSource(HttpCallBudget);
            using var http = new HttpClient
            {
                BaseAddress = new Uri("http://127.0.0.1:50765/"),
                Timeout = HttpCallBudget
            };
            await WaitForWorkerHostAsync(process!, http);

            var statusJson = await http.GetStringAsync("api/external-ai/status", timeoutCts.Token);
            using var statusDoc = JsonDocument.Parse(statusJson);
            var health = GetPropertyIgnoreCase(statusDoc.RootElement, "Health");
            var topology = GetPropertyIgnoreCase(health, "RuntimeTopology");
            Assert.Equal("workerhost_public_control_plane + revit_private_kernel", topology.GetString());
            Assert.False(string.IsNullOrWhiteSpace(GetPropertyIgnoreCase(health, "ReadinessSummary").GetString()));
        }
        finally
        {
            TryStopProcess(process!);
        }
    }

    private static JsonElement GetPropertyIgnoreCase(JsonElement element, string propertyName)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value;
            }
        }

        throw new InvalidOperationException($"Missing JSON property '{propertyName}'.");
    }

    private static async Task WaitForWorkerHostAsync(Process process, HttpClient client)
    {
        Exception? lastError = null;
        var startedUtc = DateTime.UtcNow;
        while (DateTime.UtcNow - startedUtc < HttpWaitBudget)
        {
            if (process.HasExited)
            {
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                throw new InvalidOperationException($"WorkerHost process exited early with code {process.ExitCode}. stdout: {stdout} stderr: {stderr}");
            }

            try
            {
                using var response = await client.GetAsync("");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(500);
        }

        var finalStdout = process.StandardOutput.ReadToEnd();
        var finalStderr = process.StandardError.ReadToEnd();
        throw new InvalidOperationException($"WorkerHost HTTP endpoint did not become ready in time. stdout: {finalStdout} stderr: {finalStderr}", lastError);
    }

    private static void TryStopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10000);
            }
        }
        catch
        {
        }
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

        throw new DirectoryNotFoundException("Could not locate repository root for WorkerHost HTTP harness tests.");
    }
}
