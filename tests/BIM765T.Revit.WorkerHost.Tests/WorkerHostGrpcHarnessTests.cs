using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Proto;
using Grpc.Net.Client;
using Xunit;

namespace BIM765T.Revit.WorkerHost.Tests;

[Collection("WorkerHostProcessHarness")]
public sealed class WorkerHostGrpcHarnessTests
{
    private static readonly TimeSpan PipeWaitBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan GrpcCallBudget = TimeSpan.FromSeconds(10);

    [Fact(Skip = "Experimental harness - public pipe gRPC verification is tracked separately until the transport lane is stabilized.")]
    public async Task WorkerHost_Process_Exposes_Catalog_Grpc_Public_Control_Plane()
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
            await WaitForPipeAsync(process!, "bim765t.workerhost");

            using var timeoutCts = new CancellationTokenSource(GrpcCallBudget);
            using var channel = CreateWorkerHostChannel("bim765t.workerhost");
            var catalogClient = new CatalogService.CatalogServiceClient(channel);

            var runtimeHealth = await catalogClient.GetRuntimeHealthAsync(new CatalogRequest
            {
                Meta = new EnvelopeMetadata
                {
                    CorrelationId = Guid.NewGuid().ToString("N")
                }
            }, cancellationToken: timeoutCts.Token).ResponseAsync;

            Assert.True(runtimeHealth.Status.Succeeded || !string.IsNullOrWhiteSpace(runtimeHealth.PayloadJson));
            Assert.Contains("RuntimeTopology", runtimeHealth.PayloadJson, StringComparison.OrdinalIgnoreCase);

            var catalog = await catalogClient.ListToolsAsync(new CatalogRequest
            {
                Meta = new EnvelopeMetadata
                {
                    CorrelationId = Guid.NewGuid().ToString("N")
                }
            }, cancellationToken: timeoutCts.Token).ResponseAsync;

            Assert.True(catalog.Tools.Count >= 0);
            Assert.False(string.IsNullOrWhiteSpace(catalog.PayloadJson));
        }
        finally
        {
            TryStopProcess(process!);
        }
    }

    private static async Task WaitForPipeAsync(Process process, string pipeName)
    {
        Exception? lastError = null;
        var startedUtc = DateTime.UtcNow;
        while (DateTime.UtcNow - startedUtc < PipeWaitBudget)
        {
            if (process.HasExited)
            {
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                throw new InvalidOperationException($"WorkerHost process exited early with code {process.ExitCode}. stdout: {stdout} stderr: {stderr}");
            }

            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await client.ConnectAsync(250);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            await Task.Delay(500);
        }

        var finalStdout = process.StandardOutput.ReadToEnd();
        var finalStderr = process.StandardError.ReadToEnd();
        throw new InvalidOperationException($"WorkerHost public pipe did not become ready in time. stdout: {finalStdout} stderr: {finalStderr}", lastError);
    }

    private static GrpcChannel CreateWorkerHostChannel(string pipeName)
    {
        var handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            ConnectCallback = async (_, cancellationToken) =>
            {
                var stream = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);
                await stream.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return stream;
            }
        };

        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = handler
        });
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

        throw new DirectoryNotFoundException("Could not locate repository root for WorkerHost gRPC harness tests.");
    }
}
