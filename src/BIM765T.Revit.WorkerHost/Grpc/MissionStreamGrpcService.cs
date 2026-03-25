using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BIM765T.Revit.Contracts.Proto;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Routing;
using Grpc.Core;

namespace BIM765T.Revit.WorkerHost.Grpc;

internal sealed class MissionStreamGrpcService : MissionStreamService.MissionStreamServiceBase
{
    private readonly MissionOrchestrator _orchestrator;
    private readonly WorkerHostSettings _settings;

    public MissionStreamGrpcService(MissionOrchestrator orchestrator, WorkerHostSettings settings)
    {
        _orchestrator = orchestrator;
        _settings = settings;
    }

    public override async Task StreamMissionEvents(MissionQuery request, IServerStreamWriter<EventEnvelope> responseStream, ServerCallContext context)
    {
        var emittedVersion = 0L;
        var stopwatch = Stopwatch.StartNew();
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var events = await _orchestrator.GetEventsAsync(request.MissionId, context.CancellationToken).ConfigureAwait(false);
            foreach (var record in events.Where(x => x.Version > emittedVersion))
            {
                emittedVersion = record.Version;
                await responseStream.WriteAsync(new EventEnvelope
                {
                    StreamId = record.StreamId,
                    Version = record.Version,
                    EventType = record.EventType,
                    PayloadJson = record.PayloadJson,
                    OccurredUtc = record.OccurredUtc,
                    CorrelationId = record.CorrelationId,
                    CausationId = record.CausationId,
                    ActorId = record.ActorId,
                    DocumentKey = record.DocumentKey,
                    Terminal = record.Terminal
                }).ConfigureAwait(false);

                if (record.Terminal)
                {
                    return;
                }
            }

            if (stopwatch.ElapsedMilliseconds >= _settings.StreamingIdleTimeoutMs)
            {
                return;
            }

            await Task.Delay(_settings.StreamingPollIntervalMs, context.CancellationToken).ConfigureAwait(false);
        }
    }
}
