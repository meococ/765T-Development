using BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows;
using BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows.Steps;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class WorkerToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal WorkerToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var worker = _context.Worker;
        var workerReview = ToolManifestPresets.Review()
            .WithBatchMode("conversational")
            .WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul)
            .WithSkillGroup(WorkerSkillGroups.Orchestration);
        var workerRead = ToolManifestPresets.Read()
            .WithBatchMode("conversational")
            .WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul)
            .WithSkillGroup(WorkerSkillGroups.Orchestration);
        var workerContext = workerRead.WithRequiredContext("document", "view").WithTouchesActiveView();

        // worker.message uses the async workflow pipeline:
        // GatherContext (UI ~100ms) → Plan (thread pool, LLM) → ExecuteIntent (UI) → Enhance (thread pool, LLM) → BuildResponse (UI)
        // Only GatherContext + ExecuteIntent + BuildResponse touch the UI thread in short bursts.
        // Plan + Enhance run on the thread pool — UI stays responsive during LLM HTTP calls.
        registry.RegisterWorkflow(
            ToolNames.WorkerMessage,
            "Send a natural-language message into the 765T Worker orchestration lane and receive messages, action cards, approvals, and tool result cards.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            "{\"SessionId\":\"\",\"Message\":\"kiểm tra model health\",\"PersonaId\":\"revit_worker\",\"ClientSurface\":\"ui\",\"ContinueMission\":true}",
            workerReview,
            envelope =>
            {
                var msgRequest = ToolPayloads.Read<WorkerMessageRequest>(envelope);
                var ctx = new MessageWorkflowContext
                {
                    Envelope = envelope,
                    Request = msgRequest
                };

                return new WorkflowSetup
                {
                    Context = ctx,
                    FirstStep = new GatherContextStep(worker)
                };
            });

        registry.Register(
            ToolNames.WorkerGetSession,
            "Get the current worker session, mission state, conversation history, and pending approval summary.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            workerRead,
            (uiapp, request) => ToolResponses.Success(request, worker.GetSession(ToolPayloads.Read<WorkerSessionRequest>(request))),
            "{\"SessionId\":\"\"}");

        registry.Register(
            ToolNames.WorkerListSessions,
            "List recent worker sessions for UI or MCP callers.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            workerRead,
            (uiapp, request) => ToolResponses.Success(request, worker.ListSessions(ToolPayloads.Read<WorkerListSessionsRequest>(request))),
            "{\"MaxResults\":20,\"IncludeEnded\":false}");

        registry.Register(
            ToolNames.WorkerEndSession,
            "End a worker session and clear pending approval state inside the worker shell.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            workerReview,
            (uiapp, request) => ToolResponses.Success(request, worker.EndSession(ToolPayloads.Read<WorkerSessionRequest>(request)), StatusCodes.ExecuteSucceeded),
            "{\"SessionId\":\"\"}");

        registry.Register(
            ToolNames.WorkerSetPersona,
            "Set the active worker persona for a session. Tone changes, but safety behavior stays unchanged.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            workerReview,
            (uiapp, request) => ToolResponses.Success(request, worker.SetPersona(ToolPayloads.Read<WorkerSetPersonaRequest>(request)), StatusCodes.ExecuteSucceeded),
            "{\"SessionId\":\"\",\"PersonaId\":\"revit_worker\"}");

        registry.Register(
            ToolNames.WorkerListPersonas,
            "List built-in worker personas available to UI and MCP callers.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            workerRead,
            (uiapp, request) => ToolResponses.Success(request, worker.ListPersonas()));

        registry.Register(
            ToolNames.WorkerGetContext,
            "Get the worker context bundle: task context, delta summary, queue state, and similar episodic hints.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            workerContext,
            (uiapp, request) => ToolResponses.Success(request, worker.GetContext(uiapp, request, ToolPayloads.Read<WorkerContextRequest>(request))),
            "{\"SessionId\":\"\",\"IncludeTaskContext\":true,\"IncludeDeltaSummary\":true,\"MaxRecentOperations\":10,\"MaxRecentEvents\":10}");
    }
}
