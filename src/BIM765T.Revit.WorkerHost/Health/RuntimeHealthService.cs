using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Proto;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Eventing;
using BIM765T.Revit.WorkerHost.Kernel;
using Grpc.Net.Client;
using StatusCodes = BIM765T.Revit.Contracts.Common.StatusCodes;
namespace BIM765T.Revit.WorkerHost.Health;
internal sealed class RuntimeHealthService
{
    private readonly WorkerHostSettings _settings;
    private readonly SqliteMissionEventStore _store;
    private readonly IKernelClient _kernelClient;
    private readonly IHttpClientFactory _httpClientFactory;
    public RuntimeHealthService(
        WorkerHostSettings settings,
        SqliteMissionEventStore store,
        IKernelClient kernelClient,
        IHttpClientFactory httpClientFactory)
    {
        _settings = settings;
        _store = store;
        _kernelClient = kernelClient;
        _httpClientFactory = httpClientFactory;
    }
    public async Task<RuntimeHealthReport> CollectAsync(bool probePublicControlPlane, CancellationToken cancellationToken)
    {
        var report = new RuntimeHealthReport
        {
            GeneratedUtc = DateTime.UtcNow.ToString("O"),
            PublicPipeName = _settings.PublicPipeName,
            KernelPipeName = _settings.KernelPipeName,
            StateRootPath = _settings.StateRootPath,
            EventStorePath = _settings.EventStorePath,
            LegacyStateRootPath = _settings.LegacyStateRootPath,
            LegacyState = GetLegacyStateInventory(),
            SemanticNamespaces = _settings.GetSemanticNamespaces().ToList()
        };
        var storeReady = false;
        try
        {
            report.Store = await _store.GetStatisticsAsync(cancellationToken).ConfigureAwait(false);
            storeReady = true;
        }
        catch (Exception ex)
        {
            report.Diagnostics.Add("SQLite event store probe failed: " + ex.Message);
        }
        report.Qdrant = await ProbeQdrantAsync(cancellationToken).ConfigureAwait(false);
        report.SemanticMode = report.Qdrant.Reachable ? "vector_available_non_semantic" : "lexical_only";
        report.Kernel = await ProbeKernelAsync(cancellationToken).ConfigureAwait(false);
        report.PublicControlPlane = probePublicControlPlane
            ? await ProbePublicControlPlaneAsync(cancellationToken).ConfigureAwait(false)
            : new DependencyHealth
            {
                Name = "workerhost_public_control_plane",
                Reachable = false,
                StatusCode = "SKIPPED",
                Summary = "Public named-pipe probe was skipped."
            };
        var kernelRequiredButUnavailable = !report.Kernel.Reachable;
        PopulateCatalogStaleness(report);
        // Qdrant being unreachable degrades search quality but the system remains fully functional
        // via the SQLite lexical fallback - it must NOT block the Ready signal.
        report.Degraded = kernelRequiredButUnavailable
            || !report.Qdrant.Reachable
            || report.Store.DeadLetterOutboxCount > 0
            || report.RuntimeLooksStale;
        report.Ready = storeReady && (!probePublicControlPlane || report.PublicControlPlane.Reachable);
        if (report.Qdrant.Reachable)
        {
            report.Diagnostics.Add("Qdrant reachable, but the current embedding lane is hash-based / non-semantic.");
            report.Qdrant.Diagnostics.Add("qdrant_mode: vector_available_non_semantic");
        }
        else
        {
            report.Diagnostics.Add("Qdrant unavailable; lexical SQLite fallback remains active.");
            report.Diagnostics.Add("qdrant_mode: hash_fallback_non_semantic - search results are lexical-hash-based, not semantic. Deploy Qdrant and a real embedding client for production quality.");
            report.Qdrant.Diagnostics.Add("qdrant_mode: hash_fallback_non_semantic");
        }
        if (!report.Kernel.Reachable)
        {
            report.Diagnostics.Add("Revit kernel unavailable; WorkerHost can answer memory/catalog calls but cannot execute live Revit work.");
        }
        if (report.RuntimeLooksStale)
        {
            report.Diagnostics.Add($"Revit runtime catalog appears stale: runtime has {report.RuntimeToolCount} tool(s) while source declares {report.SourceToolCount}.");
            if (report.StaleReasons.Count > 0)
            {
                report.Diagnostics.AddRange(report.StaleReasons.Select(reason => "runtime_stale: " + reason));
            }
        }
        if (report.Store.BackoffPendingOutboxCount > 0)
        {
            report.Diagnostics.Add($"Outbox has {report.Store.BackoffPendingOutboxCount} pending item(s) waiting for retry backoff.");
        }
        if (report.Store.DeadLetterOutboxCount > 0)
        {
            report.Diagnostics.Add($"Outbox has {report.Store.DeadLetterOutboxCount} dead-letter item(s) that require operator review.");
        }
        if (probePublicControlPlane && !report.PublicControlPlane.Reachable)
        {
            report.Diagnostics.Add("Public gRPC named pipe was not reachable from the probe process.");
        }
        return report;
    }
    private LegacyStateInventory GetLegacyStateInventory()
    {
        var paths = new CopilotStatePaths(_settings.LegacyStateRootPath);
        return new LegacyStateInventory
        {
            RootPath = paths.RootPath,
            TaskRunFileCount = CountJsonFiles(paths.TaskRunsPath),
            PromotionFileCount = CountJsonFiles(paths.MemoryPromotionsPath),
            EpisodeFileCount = CountJsonFiles(paths.WorkerEpisodesPath),
            QueueItemFileCount = CountJsonFiles(paths.TaskQueuePath)
        };
    }
    private async Task<DependencyHealth> ProbeQdrantAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(3);
        DependencyHealth? lastFailure = null;
        foreach (var endpoint in new[] { "readyz", "healthz", "livez" })
        {
            try
            {
                using var response = await client.GetAsync(BuildQdrantHealthUri(endpoint), cancellationToken).ConfigureAwait(false);
                var summary = $"{endpoint} => {(int)response.StatusCode} {response.ReasonPhrase}";
                if (response.IsSuccessStatusCode)
                {
                    return new DependencyHealth
                    {
                        Name = "qdrant",
                        Reachable = true,
                        StatusCode = response.StatusCode.ToString(),
                        Summary = summary
                    };
                }
                lastFailure = new DependencyHealth
                {
                    Name = "qdrant",
                    Reachable = false,
                    StatusCode = response.StatusCode.ToString(),
                    Summary = summary
                };
            }
            catch (Exception ex)
            {
                lastFailure = new DependencyHealth
                {
                    Name = "qdrant",
                    Reachable = false,
                    StatusCode = StatusCodes.BridgeUnavailable,
                    Summary = $"Qdrant local companion is not reachable via {endpoint}.",
                    Diagnostics = { ex.Message }
                };
            }
        }
        return lastFailure ?? new DependencyHealth
        {
            Name = "qdrant",
            Reachable = false,
            StatusCode = StatusCodes.BridgeUnavailable,
            Summary = "Qdrant local companion is not reachable."
        };
    }
    private void PopulateCatalogStaleness(RuntimeHealthReport report)
    {
        report.SourceToolCount = CountSourceTools();
        report.RuntimeToolCount = TryGetRuntimeToolCount(report.Kernel.PayloadJson);
        report.RuntimeLooksStale = report.Kernel.Reachable
            && report.SourceToolCount > 0
            && report.RuntimeToolCount >= 0
            && report.RuntimeToolCount < report.SourceToolCount;
        if (report.RuntimeLooksStale)
        {
            report.StaleReasons.Add($"RuntimeToolCount({report.RuntimeToolCount}) < SourceToolCount({report.SourceToolCount})");
        }
    }
    private static int CountSourceTools()
    {
        return typeof(ToolNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Count(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string));
    }
    private static int TryGetRuntimeToolCount(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return -1;
        }
        try
        {
            var response = JsonUtil.Deserialize<SessionRuntimeHealthResponse>(payloadJson);
            return response.ToolCount;
        }
        catch
        {
            return -1;
        }
    }
    private async Task<DependencyHealth> ProbeKernelAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var result = await _kernelClient.InvokeAsync(new KernelToolRequest
            {
                MissionId = "health-" + Guid.NewGuid().ToString("N"),
                CorrelationId = Guid.NewGuid().ToString("N"),
                CausationId = "workerhost.health",
                ActorId = "BIM765T.Revit.WorkerHost",
                RequestedAtUtc = DateTime.UtcNow.ToString("O"),
                TimeoutMs = 10_000,
                CancellationTokenId = "runtime-health",
                ToolName = ToolNames.SessionGetRuntimeHealth,
                PayloadJson = "{}",
                Caller = "BIM765T.Revit.WorkerHost.Health",
                SessionId = "workerhost-health",
                DryRun = true
            }, timeoutCts.Token).ConfigureAwait(false);
            return new DependencyHealth
            {
                Name = "revit_kernel",
                Reachable = result.Succeeded,
                StatusCode = string.IsNullOrWhiteSpace(result.StatusCode) ? StatusCodes.Ok : result.StatusCode,
                Summary = result.Succeeded ? "Revit kernel responded to runtime health probe." : "Revit kernel returned a degraded response.",
                PayloadJson = TryNormalizePayload(result.PayloadJson),
                Diagnostics = result.Diagnostics?.ToList() ?? new System.Collections.Generic.List<string>()
            };
        }
        catch (Exception ex)
        {
            return new DependencyHealth
            {
                Name = "revit_kernel",
                Reachable = false,
                StatusCode = StatusCodes.RevitUnavailable,
                Summary = "Revit kernel pipe is unavailable.",
                Diagnostics = { ex.Message }
            };
        }
    }
    private async Task<DependencyHealth> ProbePublicControlPlaneAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var channel = CreateWorkerHostChannel(_settings.PublicPipeName);
            var client = new CatalogService.CatalogServiceClient(channel);
            var reply = await client.GetCapabilitiesAsync(new CatalogRequest
            {
                Meta = new EnvelopeMetadata
                {
                    CorrelationId = Guid.NewGuid().ToString("N"),
                    ActorId = "BIM765T.Revit.WorkerHost.Health",
                    RequestedAtUtc = DateTime.UtcNow.ToString("O"),
                    TimeoutMs = 5000
                }
            }, cancellationToken: timeoutCts.Token).ResponseAsync.ConfigureAwait(false);
            return new DependencyHealth
            {
                Name = "workerhost_public_control_plane",
                Reachable = true,
                StatusCode = reply.Status?.StatusCode ?? StatusCodes.BridgeUnavailable,
                Summary = "WorkerHost gRPC named pipe responded.",
                PayloadJson = reply.PayloadJson ?? string.Empty,
                Diagnostics = reply.Status?.Diagnostics.ToList() ?? new System.Collections.Generic.List<string>()
            };
        }
        catch (Exception ex)
        {
            return new DependencyHealth
            {
                Name = "workerhost_public_control_plane",
                Reachable = false,
                StatusCode = StatusCodes.BridgeUnavailable,
                Summary = "WorkerHost gRPC named pipe is not reachable.",
                Diagnostics = { ex.Message }
            };
        }
    }
    private Uri BuildQdrantHealthUri(string endpoint)
    {
        var baseUri = _settings.QdrantUrl.EndsWith('/')
            ? _settings.QdrantUrl
            : _settings.QdrantUrl + "/";
        return new Uri(new Uri(baseUri), endpoint);
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
                    PipeOptions.Asynchronous,
                    TokenImpersonationLevel.Impersonation);
                await stream.ConnectAsync(cancellationToken).ConfigureAwait(false);
                return stream;
            }
        };
        return GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = handler
        });
    }
    private static int CountJsonFiles(string path)
    {
        return Directory.Exists(path)
            ? Directory.GetFiles(path, "*.json", SearchOption.TopDirectoryOnly).Length
            : 0;
    }
    private static string TryNormalizePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return string.Empty;
        }
        try
        {
            var response = JsonUtil.Deserialize<SessionRuntimeHealthResponse>(payloadJson);
            return JsonUtil.Serialize(response);
        }
        catch
        {
            return payloadJson;
        }
    }
}
