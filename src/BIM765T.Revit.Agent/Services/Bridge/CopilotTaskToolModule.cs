using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BIM765T.Revit.Agent.Infrastructure;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class CopilotTaskToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal CopilotTaskToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var copilot = _context.CopilotTasks;
        var readNoContext = ToolManifestPresets.Read();
        var readDocument = ToolManifestPresets.Read("document");
        var readDocumentView = ToolManifestPresets.Read("document", "view").WithTouchesActiveView();
        var taskReviewDocument = ToolManifestPresets.Review("document")
            .WithBatchMode("chunked")
            .WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul)
            .WithSkillGroup(WorkerSkillGroups.Orchestration);
        var taskReviewNoContext = ToolManifestPresets.Review()
            .WithBatchMode("chunked")
            .WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul)
            .WithSkillGroup(WorkerSkillGroups.Orchestration);
        var taskMutateDocument = ToolManifestPresets.Mutation("document")
            .WithBatchMode("chunked")
            .WithIdempotency("checkpointed")
            .WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul)
            .WithSkillGroup(WorkerSkillGroups.Orchestration);
        var taskQueueNoContext = ToolManifestPresets.Review()
            .WithBatchMode("queued")
            .WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul)
            .WithSkillGroup(WorkerSkillGroups.Orchestration);
        var connectorReviewDocument = ToolManifestPresets.Review("document")
            .WithBatchMode("chunked")
            .WithCapabilityPack(WorkerCapabilityPacks.Connector)
            .WithSkillGroup(WorkerSkillGroups.Orchestration)
            .WithAudience(WorkerAudience.Connector)
            .WithVisibility(WorkerVisibility.Hidden);
        var connectorReviewNoContext = ToolManifestPresets.Review()
            .WithBatchMode("chunked")
            .WithCapabilityPack(WorkerCapabilityPacks.Connector)
            .WithSkillGroup(WorkerSkillGroups.Orchestration)
            .WithAudience(WorkerAudience.Connector)
            .WithVisibility(WorkerVisibility.Hidden);
        var connectorMutateDocument = ToolManifestPresets.Mutation("document")
            .WithBatchMode("queued")
            .WithIdempotency("checkpointed")
            .WithCapabilityPack(WorkerCapabilityPacks.Connector)
            .WithSkillGroup(WorkerSkillGroups.Orchestration)
            .WithAudience(WorkerAudience.Connector)
            .WithVisibility(WorkerVisibility.Hidden);
        var intentReview = ToolManifestPresets.Review()
            .WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul)
            .WithSkillGroup(WorkerSkillGroups.Intent)
            .WithCapabilityDomain(CapabilityDomains.Intent)
            .WithDeterminismLevel(ToolDeterminismLevels.PolicyBacked)
            .WithVerificationMode(ToolVerificationModes.PolicyCheck)
            .WithIssueKinds(CapabilityIssueKinds.IntentCompile);
        var governanceReview = ToolManifestPresets.Review()
            .WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul)
            .WithSkillGroup(WorkerSkillGroups.Governance)
            .WithCapabilityDomain(CapabilityDomains.Governance)
            .WithRequiresPolicyPack()
            .WithDeterminismLevel(ToolDeterminismLevels.PolicyBacked)
            .WithVerificationMode(ToolVerificationModes.PolicyCheck)
            .WithSupportedDisciplines(CapabilityDisciplines.Common, CapabilityDisciplines.Architecture, CapabilityDisciplines.Structure, CapabilityDisciplines.Mep);
        var systemsReview = ToolManifestPresets.Review()
            .WithCapabilityPack(WorkerCapabilityPacks.CoreWorker)
            .WithSkillGroup(WorkerSkillGroups.Systems)
            .WithCapabilityDomain(CapabilityDomains.Systems)
            .WithRequiresPolicyPack()
            .WithDeterminismLevel(ToolDeterminismLevels.Scaffold)
            .WithVerificationMode(ToolVerificationModes.SystemConsistency)
            .WithSupportedDisciplines(CapabilityDisciplines.Mep, CapabilityDisciplines.Mechanical, CapabilityDisciplines.Plumbing, CapabilityDisciplines.Electrical);
        var integrationReview = ToolManifestPresets.Review()
            .WithCapabilityPack(WorkerCapabilityPacks.Connector)
            .WithSkillGroup(WorkerSkillGroups.Integration)
            .WithCapabilityDomain(CapabilityDomains.Integration)
            .WithRequiresPolicyPack()
            .WithDeterminismLevel(ToolDeterminismLevels.Scaffold)
            .WithVerificationMode(ToolVerificationModes.ReportOnly)
            .WithIssueKinds(CapabilityIssueKinds.ExternalSync, CapabilityIssueKinds.ScanToBim, CapabilityIssueKinds.LargeModelSplit);

        registry.Register(
            ToolNames.SessionGetRuntimeHealth,
            "Get copilot runtime health: durable task store, queue state, supported task kinds, and task/context broker support.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, copilot.GetRuntimeHealth(registry.GetToolCatalog(), GetQueueState())));

        registry.Register(
            ToolNames.SessionGetQueueState,
            "Get current pending queue depth and active invocation on the Revit execution lane.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, GetQueueState()));

        registry.Register(
            ToolNames.TaskPlan,
            "Plan a durable copilot task run that wraps a workflow or fix-loop scenario and persists resumable state.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            taskReviewDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TaskPlanRequest>(request);
                var documentKey = string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey;
                var doc = platform.ResolveDocument(uiapp, documentKey);
                return ToolResponses.Success(request, copilot.Plan(uiapp, doc, payload, request.Caller, request.SessionId));
            },
            "{\"DocumentKey\":\"\",\"TaskKind\":\"fix_loop\",\"TaskName\":\"parameter_hygiene\",\"IntentSummary\":\"Fill missing parameter values with verification.\",\"InputJson\":\"{}\",\"Tags\":[],\"TaskSpec\":{\"Source\":\"panel\",\"Goal\":\"Fix documentation issue safely.\",\"DocumentScope\":\"\",\"ProjectScope\":\"\",\"Constraints\":[],\"ApprovalPolicy\":{\"ReviewMode\":\"checkpointed\",\"RequiresOperatorApproval\":true,\"AllowQueuedExecution\":false,\"MaxBatchSize\":100},\"Deliverables\":[],\"CallbackTarget\":{\"System\":\"panel\",\"Reference\":\"\",\"Mode\":\"panel_only\",\"Destination\":\"\"}},\"WorkerProfile\":{\"PersonaId\":\"freelancer_default\",\"Tone\":\"pragmatic\",\"QaStrictness\":\"standard\",\"AllowedSkillGroups\":[\"documentation\"],\"RiskTolerance\":\"guarded\",\"EscalationStyle\":\"checkpoint_first\"},\"PreferredCapabilityPack\":\"core_worker\"}");

        registry.Register(
            ToolNames.TaskPreview,
            "Create or hydrate a task preview so approval-ready intent, selected actions, preview token, and expected context are persisted on the durable task run.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            taskReviewDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TaskPreviewRequest>(request);
                var run = copilot.GetRun(payload.RunId);
                var doc = platform.ResolveDocument(uiapp, run.DocumentKey);
                return ToolResponses.Success(request, copilot.Preview(uiapp, doc, payload, request));
            },
            "{\"RunId\":\"\",\"StepId\":\"preview\",\"ActionIds\":[]}");

        registry.Register(
            ToolNames.TaskApproveStep,
            "Record operator approval for a durable task step without mutating the Revit model.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            taskReviewNoContext,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TaskApproveStepRequest>(request);
                return ToolResponses.Success(request, copilot.ApproveStep(payload, request.Caller, request.SessionId));
            },
            "{\"RunId\":\"\",\"StepId\":\"approve\",\"ApprovalToken\":\"\",\"PreviewRunId\":\"\",\"Note\":\"\"}");

        registry.Register(
            ToolNames.TaskExecuteStep,
            "Execute the next approved task step using the persisted preview token and expected context captured during task preview.",
            PermissionLevel.Mutate,
            ApprovalRequirement.None,
            false,
            taskMutateDocument,
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowWriteTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.WriteDisabled);
                }

                var payload = ToolPayloads.Read<TaskExecuteStepRequest>(request);
                try
                {
                    var run = copilot.GetRun(payload.RunId);
                    var doc = platform.ResolveDocument(uiapp, run.DocumentKey);
                    return ToolResponses.Success(request, copilot.Execute(uiapp, doc, payload, request), StatusCodes.ExecuteSucceeded);
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromTaskException(request, ex);
                }
            },
            "{\"RunId\":\"\",\"StepId\":\"execute\",\"AllowMutations\":true}");

        registry.Register(
            ToolNames.TaskResume,
            "Resume a paused durable task run from its next actionable step.",
            PermissionLevel.Mutate,
            ApprovalRequirement.None,
            false,
            taskMutateDocument,
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowWriteTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.WriteDisabled);
                }

                var payload = ToolPayloads.Read<TaskResumeRequest>(request);
                try
                {
                    var run = copilot.GetRun(payload.RunId);
                    var doc = platform.ResolveDocument(uiapp, run.DocumentKey);
                    return ToolResponses.Success(request, copilot.Resume(uiapp, doc, payload, request), StatusCodes.ExecuteSucceeded);
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromTaskException(request, ex);
                }
            },
            "{\"RunId\":\"\",\"AllowMutations\":true,\"RecoveryBranchId\":\"\",\"MaxResidualIssues\":200}");

        registry.Register(
            ToolNames.TaskVerify,
            "Verify a durable task run and persist residual issue summary for post-action truth.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            taskReviewDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TaskVerifyRequest>(request);
                try
                {
                    var run = copilot.GetRun(payload.RunId);
                    var doc = platform.ResolveDocument(uiapp, run.DocumentKey);
                    return ToolResponses.Success(request, copilot.Verify(uiapp, doc, payload));
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromTaskException(request, ex);
                }
            },
            "{\"RunId\":\"\",\"MaxResidualIssues\":200}");

        registry.Register(
            ToolNames.TaskGetRun,
            "Get a durable task run with approval state, selected actions, residual summary, and resumable step state.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TaskGetRunRequest>(request);
                try
                {
                    return ToolResponses.Success(request, copilot.GetRun(payload.RunId));
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromTaskException(request, ex);
                }
            },
            "{\"RunId\":\"\"}");

        registry.Register(
            ToolNames.TaskListRuns,
            "List durable copilot task runs with status and document/task filters.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, copilot.ListRuns(ToolPayloads.Read<TaskListRunsRequest>(request))),
            "{\"TaskKind\":\"\",\"TaskName\":\"\",\"DocumentKey\":\"\",\"Status\":\"\",\"MaxResults\":50}");

        registry.Register(
            ToolNames.TaskSummarize,
            "Return a compact task summary with next action and residual count for context-efficient agent loops.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TaskSummarizeRequest>(request);
                try
                {
                    return ToolResponses.Success(request, copilot.Summarize(payload));
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromTaskException(request, ex);
                }
            },
            "{\"RunId\":\"\"}");

        registry.Register(
            ToolNames.TaskGetMetrics,
            "Aggregate durable task metrics by task kind/name/document to measure copilot effectiveness over time.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, copilot.GetMetrics(ToolPayloads.Read<TaskMetricsRequest>(request))),
            "{\"TaskKind\":\"\",\"TaskName\":\"\",\"DocumentKey\":\"\",\"MaxResults\":100}");

        registry.Register(
            ToolNames.TaskGetResiduals,
            "Get the persisted residual summary and diagnostics for a durable task run.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TaskResidualsRequest>(request);
                try
                {
                    return ToolResponses.Success(request, copilot.GetResiduals(payload));
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromTaskException(request, ex);
                }
            },
            "{\"RunId\":\"\"}");

        registry.Register(
            ToolNames.TaskPromoteMemorySafe,
            "Promote a verified task run into a curated local memory candidate for later review and playbook curation.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            taskReviewNoContext,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TaskPromoteMemoryRequest>(request);
                try
                {
                    return ToolResponses.Success(request, copilot.PromoteMemory(payload, request.Caller), StatusCodes.ExecuteSucceeded);
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromTaskException(request, ex);
                }
            },
            "{\"RunId\":\"\",\"PromotionKind\":\"lesson\",\"Summary\":\"\",\"Tags\":[],\"Notes\":\"\"}");

        registry.Register(
            ToolNames.TaskIntakeExternal,
            "Normalize an external connector task (ACC/issues/etc.), create a durable task run, and preserve callback metadata for later report-back.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            connectorReviewDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ExternalTaskIntakeRequest>(request);
                var documentKey = string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey;
                var doc = platform.ResolveDocument(uiapp, documentKey);
                return ToolResponses.Success(request, copilot.IntakeExternalTask(uiapp, doc, payload, request.Caller, request.SessionId));
            },
            "{\"Envelope\":{\"ExternalSystem\":\"acc\",\"ExternalTaskRef\":\"issue-105\",\"ProjectRef\":\"proj-001\",\"AuthContext\":\"\",\"CallbackMode\":\"report_back\",\"StatusMapping\":{\"done\":\"closed\"},\"Title\":\"Check issue #105\",\"Description\":\"Review and execute issue #105 safely.\",\"DocumentHint\":\"\",\"Metadata\":{}},\"DocumentKey\":\"\",\"TaskKind\":\"workflow\",\"TaskName\":\"issue_105\",\"IntentSummary\":\"Review and execute external issue safely.\",\"InputJson\":\"{}\",\"Tags\":[],\"TaskSpec\":{\"Source\":\"acc\",\"Goal\":\"Review issue safely.\",\"DocumentScope\":\"\",\"ProjectScope\":\"\",\"Constraints\":[],\"ApprovalPolicy\":{\"ReviewMode\":\"checkpointed\",\"RequiresOperatorApproval\":true,\"AllowQueuedExecution\":true,\"MaxBatchSize\":100},\"Deliverables\":[],\"CallbackTarget\":{\"System\":\"acc\",\"Reference\":\"issue-105\",\"Mode\":\"report_back\",\"Destination\":\"proj-001\"}},\"WorkerProfile\":{\"PersonaId\":\"freelancer_default\",\"Tone\":\"pragmatic\",\"QaStrictness\":\"standard\",\"AllowedSkillGroups\":[\"documentation\"],\"RiskTolerance\":\"guarded\",\"EscalationStyle\":\"checkpoint_first\"},\"PreferredCapabilityPack\":\"core_worker\"}");

        registry.Register(
            ToolNames.TaskEnqueueApproved,
            "Queue an already-approved durable task run for off-hours or connector-driven execution.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            taskQueueNoContext,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TaskQueueEnqueueRequest>(request);
                try
                {
                    return ToolResponses.Success(request, copilot.EnqueueApproved(payload, request.Caller), StatusCodes.ExecuteSucceeded);
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromTaskException(request, ex);
                }
            },
            "{\"RunId\":\"\",\"QueueName\":\"approved\",\"ScheduledUtc\":null,\"Note\":\"Run after operator approval.\"}");

        registry.Register(
            ToolNames.TaskListQueue,
            "List durable queued task items for off-hours worker execution and connector report-back.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext.WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul).WithSkillGroup(WorkerSkillGroups.Orchestration),
            (uiapp, request) => ToolResponses.Success(request, copilot.ListQueue(ToolPayloads.Read<TaskQueueListRequest>(request))),
            "{\"QueueName\":\"\",\"Status\":\"\",\"RunId\":\"\",\"ConnectorSystem\":\"\",\"MaxResults\":50,\"IncludeCompleted\":false}");

        registry.Register(
            ToolNames.TaskClaimQueueItem,
            "Lease the next ready queued task item for a connector/off-hours worker without mutating the model yet.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            connectorReviewNoContext,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TaskQueueClaimRequest>(request);
                try
                {
                    return ToolResponses.Success(request, copilot.ClaimQueueItem(payload), StatusCodes.ExecuteSucceeded);
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromTaskException(request, ex);
                }
            },
            "{\"QueueName\":\"approved\",\"LeaseOwner\":\"worker-1\",\"IncludeScheduledFuture\":false}");

        registry.Register(
            ToolNames.TaskCompleteQueueItem,
            "Mark a queued task item complete/failed/cancelled after execution and preserve a concise status note for report-back.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            connectorReviewNoContext,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TaskQueueCompleteRequest>(request);
                try
                {
                    return ToolResponses.Success(request, copilot.CompleteQueueItem(payload), StatusCodes.ExecuteSucceeded);
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromTaskException(request, ex);
                }
            },
            "{\"QueueItemId\":\"\",\"Status\":\"completed\",\"Message\":\"\",\"ArtifactKeys\":[]}");

        registry.Register(
            ToolNames.TaskRunQueueItem,
            "Execute a leased approved queue item end-to-end inside Revit: execute/resume, optionally verify, reconcile queue state, and prepare callback preview.",
            PermissionLevel.Mutate,
            ApprovalRequirement.None,
            false,
            connectorMutateDocument,
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowWriteTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.WriteDisabled);
                }

                var payload = ToolPayloads.Read<TaskQueueRunRequest>(request);
                try
                {
                    return ToolResponses.Success(request, copilot.RunQueueItem(uiapp, payload, request), StatusCodes.ExecuteSucceeded);
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromTaskException(request, ex);
                }
            },
            "{\"QueueItemId\":\"\",\"LeaseOwner\":\"worker-1\",\"AllowMutations\":true,\"AutoVerify\":true,\"MaxResidualIssues\":200,\"RecoveryBranchId\":\"\",\"ResultStatusOverride\":\"\"}");

        registry.Register(
            ToolNames.TaskBuildCallbackPreview,
            "Build the connector callback/report-back payload preview for an external task without sending anything to the external system.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            connectorReviewNoContext,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ConnectorCallbackPreviewRequest>(request);
                try
                {
                    return ToolResponses.Success(request, copilot.BuildCallbackPreview(payload));
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromTaskException(request, ex);
                }
            },
            "{\"RunId\":\"\",\"QueueItemId\":\"\",\"ResultStatus\":\"\"}");

        registry.Register(
            ToolNames.ContextGetHotState,
            "Get hot context only: task context, queue, document state graph, and pending task summaries without dumping raw files.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readDocumentView,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<HotStateRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, copilot.GetHotState(uiapp, doc, payload, registry.GetToolCatalog(), GetQueueState()));
            },
            "{\"DocumentKey\":\"\",\"MaxRecentOperations\":10,\"MaxRecentEvents\":10,\"MaxPendingTasks\":10,\"IncludeGraph\":true,\"IncludeToolCatalog\":false}");

        registry.Register(
            ToolNames.ContextGetDeltaSummary,
            "Summarize the hottest document delta from recent operations/events and suggest the next recovery or verification tools.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ContextDeltaSummaryRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, copilot.GetContextDeltaSummary(uiapp, doc, payload, registry.GetToolCatalog(), GetQueueState()));
            },
            "{\"DocumentKey\":\"\",\"MaxRecentOperations\":10,\"MaxRecentEvents\":10,\"MaxRecommendations\":5}");

        registry.Register(
            ToolNames.ContextResolveBundle,
            "Resolve a bounded context bundle with hot/warm/cold tiers so agents avoid loading oversized raw files into context.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ContextResolveBundleRequest>(request);
                var hotItems = BuildHotItems(registry.GetToolCatalog());
                return ToolResponses.Success(request, copilot.ResolveBundle(hotItems, payload));
            },
            "{\"RunId\":\"\",\"Query\":\"parameter hygiene comments\",\"Tags\":[],\"MaxAnchors\":8,\"IncludeHot\":true,\"IncludeWarm\":true,\"IncludeCold\":false}");

        registry.Register(
            ToolNames.ContextSearchAnchors,
            "Search durable task and promoted-memory anchors by query/tags without opening large raw artifacts.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, copilot.SearchAnchors(ToolPayloads.Read<ContextSearchAnchorsRequest>(request))),
            "{\"Query\":\"penetration export\",\"Tags\":[],\"MaxResults\":20}");

        registry.Register(
            ToolNames.MemoryFindSimilarRuns,
            "Find similar durable task runs to reuse known-good patterns before planning a new task.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, copilot.FindSimilarRuns(ToolPayloads.Read<MemoryFindSimilarRunsRequest>(request))),
            "{\"RunId\":\"\",\"TaskKind\":\"fix_loop\",\"TaskName\":\"parameter_hygiene\",\"DocumentKey\":\"\",\"Query\":\"comments review required\",\"MaxResults\":10}");

        registry.Register(
            ToolNames.ToolFindByCapability,
            "Find tools by capability keywords, risk tags, and required context instead of scanning the full tool catalog.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, copilot.FindTools(registry.GetToolCatalog(), ToolPayloads.Read<ToolCapabilityLookupRequest>(request))),
            "{\"Query\":\"export ifc print\",\"RiskTags\":[],\"RequiredContext\":[],\"MaxResults\":10}");

        registry.Register(
            ToolNames.ToolGetGuidance,
            "Return curated tool guidance with risk/cost, prerequisites, follow-up tools, and likely recovery tools.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, copilot.GetToolGuidance(registry.GetToolCatalog(), ToolPayloads.Read<ToolGuidanceRequest>(request))),
            "{\"Query\":\"export ifc\",\"ToolNames\":[],\"MaxResults\":5}");

        registry.Register(
            ToolNames.WorkspaceGetManifest,
            "Get workspace manifest, enabled packs, preferred standards/playbooks, and runtime policy for the selected workspace.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, copilot.GetWorkspaceManifest(ToolPayloads.Read<WorkspaceGetManifestRequest>(request))),
            "{\"WorkspaceId\":\"default\"}");

        registry.Register(
            ToolNames.PackList,
            "List available pack manifests for the current monorepo workspace and optionally filter by pack type or enabled state.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, copilot.ListPacks(ToolPayloads.Read<PackListRequest>(request))),
            "{\"WorkspaceId\":\"default\",\"PackType\":\"playbook-pack\",\"EnabledOnly\":true}");

        registry.Register(
            ToolNames.StandardsResolve,
            "Resolve machine-readable team standards from enabled standards packs and workspace preferences.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, copilot.ResolveStandards(ToolPayloads.Read<StandardsResolutionRequest>(request))),
            "{\"WorkspaceId\":\"default\",\"StandardKind\":\"sheet\",\"Discipline\":\"architectural\",\"RequestedKeys\":[\"templates.json#view_templates.architectural_plan\"],\"PreferredPackIds\":[]}");

        registry.Register(
            ToolNames.PlaybookMatch,
            "Match a workspace-aware playbook/recipe against the current task description before falling back to flat tool search.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, copilot.MatchPlaybook(registry.GetToolCatalog(), ToolPayloads.Read<PlaybookMatchRequest>(request))),
            "{\"WorkspaceId\":\"default\",\"Query\":\"tao sheet A tang 1-5\",\"DocumentContext\":\"project\",\"MaxResults\":5}");

        registry.Register(
            ToolNames.PlaybookPreview,
            "Preview the selected workspace playbook with standards refs, required inputs, and the resolved tool chain.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, copilot.PreviewPlaybook(registry.GetToolCatalog(), ToolPayloads.Read<PlaybookPreviewRequest>(request))),
            "{\"WorkspaceId\":\"default\",\"PlaybookId\":\"sheet_create_arch_package.v1\",\"Query\":\"tao sheet A tang 1-5\",\"DocumentContext\":\"project\"}");

        registry.Register(
            ToolNames.PolicyResolve,
            "Resolve capability policy packs, standards files, and verification posture for the requested domain/discipline/issue set.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            governanceReview.WithRecommendedPlaybooks("warning_triage_safe.v1"),
            (uiapp, request) => ToolResponses.Success(request, copilot.ResolvePolicy(ToolPayloads.Read<PolicyResolutionRequest>(request))),
            "{\"WorkspaceId\":\"default\",\"CapabilityDomain\":\"governance\",\"Discipline\":\"architecture\",\"IssueKinds\":[\"warning_triage\"],\"PreferredPackIds\":[]}");

        registry.Register(
            ToolNames.SpecialistResolve,
            "Resolve the best specialist agents for a capability domain before executing a playbook or fix loop.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            intentReview.WithRecommendedNextTools(ToolNames.PolicyResolve, ToolNames.PlaybookMatch),
            (uiapp, request) => ToolResponses.Success(request, copilot.ResolveSpecialists(ToolPayloads.Read<CapabilitySpecialistRequest>(request))),
            "{\"WorkspaceId\":\"default\",\"CapabilityDomain\":\"coordination\",\"Discipline\":\"mep\",\"IssueKinds\":[\"hard_clash\"]}");

        registry.Register(
            ToolNames.IntentCompile,
            "Compile a natural-language task into a typed capability plan with playbook, policy, specialist, issue-scan, fix, and verify lanes.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            intentReview.WithRecommendedPlaybooks("intent_nl_query_compile.v1"),
            (uiapp, request) => ToolResponses.Success(request, copilot.CompileIntentPlan(registry.GetToolCatalog(), ToolPayloads.Read<IntentCompileRequest>(request))),
            "{\"Task\":{\"Query\":\"loc toan bo cua tang 3 thieu Fire Rating va highlight mau do\",\"WorkspaceId\":\"default\",\"DocumentContext\":\"project\"},\"PreferredCapabilityDomain\":\"intent\",\"Discipline\":\"architecture\",\"RequireDeterministicPlan\":true}");

        registry.Register(
            ToolNames.IntentValidate,
            "Validate a compiled capability plan against the current tool catalog and surface gaps in policy, specialist, or verify lanes.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            intentReview,
            (uiapp, request) => ToolResponses.Success(request, copilot.ValidateCompiledIntent(registry.GetToolCatalog(), ToolPayloads.Read<IntentValidateRequest>(request))),
            "{\"Plan\":{\"CapabilityDomain\":\"intent\",\"CandidateToolNames\":[\"tool.get_guidance\"],\"VerifyTools\":[\"review.smart_qc\"]}}");

        registry.Register(
            ToolNames.SystemCaptureGraph,
            "Capture a scaffold system graph snapshot for connectivity, slope, and routing planning without mutating the model.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            systemsReview.WithIssueKinds(CapabilityIssueKinds.DisconnectedSystem, CapabilityIssueKinds.SlopeContinuity, CapabilityIssueKinds.BasicRouting),
            (uiapp, request) => ToolResponses.Success(request, copilot.CaptureSystemGraph(ToolPayloads.Read<SystemGraphRequest>(request))),
            "{\"WorkspaceId\":\"default\",\"Discipline\":\"plumbing\",\"Query\":\"kiem tra disconnected sanitary system\",\"SystemNames\":[\"SAN\"]}");

        registry.Register(
            ToolNames.SystemPlanConnectivityFix,
            "Create a policy-backed scaffold fix plan for disconnected-system or basic routing scenarios.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            systemsReview.WithRecommendedPlaybooks("system_disconnected_fix.v1"),
            (uiapp, request) => ToolResponses.Success(request, copilot.PlanSystemFix(ToolPayloads.Read<SystemFixPlanRequest>(request))),
            "{\"WorkspaceId\":\"default\",\"Discipline\":\"mechanical\",\"IssueKind\":\"disconnected_system\",\"Query\":\"de xuat noi kin nhanh ong gio\"}");

        registry.Register(
            ToolNames.SystemPlanSlopeRemediation,
            "Create a scaffold fix plan for slope continuity issues with residual/manual-review buckets.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            systemsReview.WithIssueKinds(CapabilityIssueKinds.SlopeContinuity).WithRecommendedPlaybooks("system_slope_remediation.v1"),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<SystemFixPlanRequest>(request);
                if (string.IsNullOrWhiteSpace(payload.IssueKind))
                {
                    payload.IssueKind = CapabilityIssueKinds.SlopeContinuity;
                }

                return ToolResponses.Success(request, copilot.PlanSystemFix(payload));
            },
            "{\"WorkspaceId\":\"default\",\"Discipline\":\"plumbing\",\"IssueKind\":\"slope_continuity\",\"Query\":\"review fix slope sanitary stack\"}");

        registry.Register(
            ToolNames.IntegrationPreviewSync,
            "Preview integration deltas for Excel/PDF/CAD/BOQ/4D5D connectors without taking ownership of Revit execution.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            integrationReview.WithRecommendedPlaybooks("integration_cost_time_sync.v1"),
            (uiapp, request) => ToolResponses.Success(request, copilot.PreviewIntegrationSync(ToolPayloads.Read<IntegrationPreviewSyncRequest>(request))),
            "{\"ExternalSystem\":\"excel\",\"EntityKind\":\"parameters\",\"CapabilityDomain\":\"integration\",\"Discipline\":\"common\",\"SourceArtifactRefs\":[\"specs/equipment.xlsx\"],\"Query\":\"preview parameter sync\"}");

        registry.Register(
            ToolNames.ProjectInitPreview,
            "Preview BIM /init: discover .rvt/.rfa/.pdf sources, validate firm packs, suggest workspace id, and detect primary-model conflicts before writing any workspace state.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext.WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul).WithSkillGroup(WorkerSkillGroups.Orchestration),
            (uiapp, request) => ToolResponses.Success(request, copilot.PreviewProjectInit(ToolPayloads.Read<ProjectInitPreviewRequest>(request))),
            "{\"SourceRootPath\":\"D:\\Projects\\SampleJob\",\"WorkspaceId\":\"sample-job\",\"DisplayName\":\"Sample Job\",\"FirmPackIds\":[\"bim765t.standards.core\"],\"PrimaryRevitFilePath\":\"\"}");

        registry.Register(
            ToolNames.ProjectInitApply,
            "Apply BIM /init: create workspace.json + project.context.json + curated manifest/brief files, and optionally seed one live primary-model summary.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            readNoContext.WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul).WithSkillGroup(WorkerSkillGroups.Orchestration),
            (uiapp, request) =>
            {
                try
                {
                    return ToolResponses.Success(request, copilot.ApplyProjectInit(uiapp, ToolPayloads.Read<ProjectInitApplyRequest>(request), registry.GetToolCatalog()), StatusCodes.ExecuteSucceeded);
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromTaskException(request, ex);
                }
            },
            "{\"SourceRootPath\":\"D:\\Projects\\SampleJob\",\"WorkspaceId\":\"sample-job\",\"DisplayName\":\"Sample Job\",\"FirmPackIds\":[],\"PrimaryRevitFilePath\":\"D:\\Projects\\SampleJob\\Model.rvt\",\"AllowExistingWorkspaceOverwrite\":false,\"IncludeLivePrimaryModelSummary\":true}");

        registry.Register(
            ToolNames.ProjectGetManifest,
            "Get the curated project-init manifest for a workspace without loading raw BIM/PDF contents.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext.WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul).WithSkillGroup(WorkerSkillGroups.Orchestration),
            (uiapp, request) => ToolResponses.Success(request, copilot.GetProjectManifest(ToolPayloads.Read<ProjectManifestRequest>(request))),
            "{\"WorkspaceId\":\"sample-job\"}");

        registry.Register(
            ToolNames.ProjectGetContextBundle,
            "Get the compact project context bundle: project brief, manifest stats, top standards refs, source refs, pending unknowns, and primary-model status.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext.WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul).WithSkillGroup(WorkerSkillGroups.Orchestration),
            (uiapp, request) => ToolResponses.Success(request, copilot.GetProjectContextBundle(registry.GetToolCatalog(), ToolPayloads.Read<ProjectContextBundleRequest>(request))),
            "{\"WorkspaceId\":\"sample-job\",\"Query\":\"overview project\",\"MaxSourceRefs\":8,\"MaxStandardsRefs\":6}");

        registry.Register(
            ToolNames.ProjectGetDeepScan,
            "Get the latest Project Brain deep scan report and summary refs for a workspace without rerunning the scan.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext.WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul).WithSkillGroup(WorkerSkillGroups.Orchestration),
            (uiapp, request) => ToolResponses.Success(request, copilot.GetProjectDeepScan(ToolPayloads.Read<ProjectDeepScanGetRequest>(request))),
            "{\"WorkspaceId\":\"sample-job\"}");

        registry.Register(
            ToolNames.ArtifactSummarize,
            "Summarize a local artifact/file into a small preview, top-level keys, and retrieval-safe description.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, copilot.SummarizeArtifact(ToolPayloads.Read<ArtifactSummarizeRequest>(request))),
            "{\"ArtifactPath\":\"C:\\\\temp\\\\artifact.json\",\"MaxChars\":2000,\"MaxLines\":40}");
    }

    private static QueueStateResponse GetQueueState()
    {
        return AgentHost.TryGetCurrent(out var runtime) && runtime != null
            ? runtime.ExternalEventHandler.GetQueueState(runtime.Queue)
            : new QueueStateResponse();
    }

    private static List<ContextBundleItem> BuildHotItems(IEnumerable<ToolManifest> manifests)
    {
        return new List<ContextBundleItem>
        {
            new ContextBundleItem
            {
                AnchorId = "hot:runtime",
                Tier = "hot",
                Title = "Runtime hot state",
                Summary = "Use context.get_hot_state for active doc/view/selection, queue, and graph summary.",
                SourceKind = "runtime",
                SourcePath = ToolNames.ContextGetHotState,
                Tags = new List<string> { "hot", "runtime", "context" },
                RetrievalHint = "Call context.get_hot_state before larger task planning.",
                Score = 100
            },
            new ContextBundleItem
            {
                AnchorId = "hot:tools",
                Tier = "hot",
                Title = "Capability lookup",
                Summary = $"Tool catalog currently exposes {manifests.Count()} tools; use tool.find_by_capability to keep context small.",
                SourceKind = "tool_catalog",
                SourcePath = ToolNames.ToolFindByCapability,
                Tags = new List<string> { "hot", "tooling", "capability" },
                RetrievalHint = "Prefer tool.find_by_capability over dumping full session.list_tools into model context.",
                Score = 90
            },
            new ContextBundleItem
            {
                AnchorId = "hot:guidance",
                Tier = "hot",
                Title = "Tool guidance",
                Summary = "Use tool.get_guidance after capability lookup when you need prerequisites, risk, and likely recovery tools.",
                SourceKind = "tool_guidance",
                SourcePath = ToolNames.ToolGetGuidance,
                Tags = new List<string> { "hot", "tooling", "guidance" },
                RetrievalHint = "Call tool.get_guidance for shortlists before executing high-risk or file-lifecycle tools.",
                Score = 85
            }
        };
    }

    private static ToolResponseEnvelope FailureFromTaskException(ToolRequestEnvelope request, InvalidOperationException ex)
    {
        var statusCode = ex.Message switch
        {
            StatusCodes.TaskRunNotFound => StatusCodes.TaskRunNotFound,
            StatusCodes.TaskKindNotSupported => StatusCodes.TaskKindNotSupported,
            StatusCodes.TaskStepBlocked => StatusCodes.TaskStepBlocked,
            StatusCodes.TaskAlreadyCompleted => StatusCodes.TaskAlreadyCompleted,
            StatusCodes.TaskApprovalRequired => StatusCodes.TaskApprovalRequired,
            StatusCodes.TaskPromotionBlocked => StatusCodes.TaskPromotionBlocked,
            StatusCodes.TaskResumeNotAvailable => StatusCodes.TaskResumeNotAvailable,
            StatusCodes.TaskRecoveryBranchNotFound => StatusCodes.TaskRecoveryBranchNotFound,
            StatusCodes.TaskQueueNotFound => StatusCodes.TaskQueueNotFound,
            StatusCodes.TaskQueueBlocked => StatusCodes.TaskQueueBlocked,
            StatusCodes.TaskQueueApprovalRequired => StatusCodes.TaskQueueApprovalRequired,
            StatusCodes.TaskQueueAlreadyQueued => StatusCodes.TaskQueueAlreadyQueued,
            StatusCodes.TaskQueueLeaseOwnerRequired => StatusCodes.TaskQueueLeaseOwnerRequired,
            StatusCodes.TaskQueueLeaseMismatch => StatusCodes.TaskQueueLeaseMismatch,
            StatusCodes.TaskQueueNoPendingItems => StatusCodes.TaskQueueNoPendingItems,
            StatusCodes.TaskQueueAlreadyCompleted => StatusCodes.TaskQueueAlreadyCompleted,
            StatusCodes.ContextMismatch => StatusCodes.ContextMismatch,
            StatusCodes.ApprovalInvalid => StatusCodes.ApprovalInvalid,
            StatusCodes.ApprovalExpired => StatusCodes.ApprovalExpired,
            StatusCodes.ApprovalMismatch => StatusCodes.ApprovalMismatch,
            StatusCodes.WorkflowRunNotFound => StatusCodes.WorkflowRunNotFound,
            StatusCodes.FixLoopRunNotFound => StatusCodes.FixLoopRunNotFound,
            StatusCodes.ProjectSourceRootNotFound => StatusCodes.ProjectSourceRootNotFound,
            StatusCodes.ProjectWorkspaceConflict => StatusCodes.ProjectWorkspaceConflict,
            StatusCodes.ProjectPrimaryModelSelectionRequired => StatusCodes.ProjectPrimaryModelSelectionRequired,
            StatusCodes.ProjectPackNotFound => StatusCodes.ProjectPackNotFound,
            StatusCodes.ProjectPackTypeInvalid => StatusCodes.ProjectPackTypeInvalid,
            StatusCodes.ProjectManifestNotFound => StatusCodes.ProjectManifestNotFound,
            StatusCodes.ProjectContextNotInitialized => StatusCodes.ProjectContextNotInitialized,
            _ => StatusCodes.InternalError
        };

        return ToolResponses.Failure(
            request,
            statusCode,
            DiagnosticRecord.Create(statusCode, DiagnosticSeverity.Error, statusCode == StatusCodes.InternalError ? ex.Message : $"Task runtime failed: {statusCode}"));
    }
}
