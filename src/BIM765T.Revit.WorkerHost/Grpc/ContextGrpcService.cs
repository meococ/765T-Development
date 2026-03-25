using System;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Proto;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.WorkerHost.Memory;
using BIM765T.Revit.WorkerHost.Routing;
using Grpc.Core;

namespace BIM765T.Revit.WorkerHost.Grpc;

internal sealed class ContextGrpcService : ContextService.ContextServiceBase
{
    private readonly MissionOrchestrator _orchestrator;
    private readonly MemorySearchService _memorySearch;

    public ContextGrpcService(MissionOrchestrator orchestrator, MemorySearchService memorySearch)
    {
        _orchestrator = orchestrator;
        _memorySearch = memorySearch;
    }

    public override Task<ContextReply> GetContext(ContextRequest request, ServerCallContext context)
    {
        return InvokeToolAsync(request, ToolNames.SessionGetTaskContext, JsonUtil.Serialize(new TaskContextRequest
        {
            MaxRecentOperations = request.MaxRecentOperations <= 0 ? 10 : request.MaxRecentOperations,
            MaxRecentEvents = request.MaxRecentEvents <= 0 ? 10 : request.MaxRecentEvents
        }), context);
    }

    public override Task<ContextReply> GetDeltaSummary(ContextRequest request, ServerCallContext context)
    {
        return InvokeToolAsync(request, ToolNames.ContextGetDeltaSummary, JsonUtil.Serialize(new ContextDeltaSummaryRequest
        {
            MaxRecentOperations = request.MaxRecentOperations <= 0 ? 10 : request.MaxRecentOperations,
            MaxRecentEvents = request.MaxRecentEvents <= 0 ? 10 : request.MaxRecentEvents
        }), context);
    }

    public override async Task<SearchMemoryReply> SearchMemory(ContextRequest request, ServerCallContext context)
    {
        var hits = await _memorySearch.SearchAsync(
            request.Query ?? string.Empty,
            request.Meta?.DocumentKey ?? string.Empty,
            request.TopK <= 0 ? 5 : request.TopK,
            context.CancellationToken).ConfigureAwait(false);

        var reply = new SearchMemoryReply
        {
            Status = new StatusEnvelope
            {
                Succeeded = true,
                StatusCode = BIM765T.Revit.Contracts.Common.StatusCodes.ReadSucceeded,
                Message = "Memory hits ready."
            }
        };

        foreach (var hit in hits)
        {
            reply.Hits.Add(new MemoryHit
            {
                Id = hit.Id,
                Kind = hit.Kind,
                Title = hit.Title,
                Snippet = hit.Snippet,
                SourceRef = hit.SourceRef,
                DocumentKey = hit.DocumentKey,
                CreatedUtc = hit.CreatedUtc,
                Score = hit.Score
            });
            reply.Hits[^1].Tags.AddRange(hit.Tags);
        }

        return reply;
    }

    private async Task<ContextReply> InvokeToolAsync(ContextRequest request, string toolName, string payloadJson, ServerCallContext context)
    {
        var compat = new CompatibilityGrpcService(_orchestrator);
        var invoke = await compat.InvokeTool(new CompatToolRequest
        {
            Meta = request.Meta ?? new EnvelopeMetadata { CorrelationId = Guid.NewGuid().ToString("N") },
            ToolName = toolName,
            PayloadJson = payloadJson,
            DryRun = true
        }, context).ConfigureAwait(false);

        var reply = new ContextReply
        {
            Status = new StatusEnvelope
            {
                Succeeded = invoke.Status.Succeeded,
                StatusCode = invoke.Status.StatusCode,
                Message = invoke.Status.StatusCode
            },
            PayloadJson = invoke.PayloadJson ?? string.Empty
        };
        reply.Status.Diagnostics.AddRange(invoke.Status.Diagnostics);
        return reply;
    }
}
