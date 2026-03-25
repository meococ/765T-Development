using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure.Bridge;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class CopilotTaskService
{
    private readonly PlatformServices _platform;
    private readonly Workflow.WorkflowRuntimeService _workflow;
    private readonly FixLoopService _fixLoop;
    private readonly ModelStateGraphService _graph;
    private readonly CopilotTaskRunStore _store;
    private readonly TaskMetricsService _metrics;
    private readonly ContextAnchorService _anchors;
    private readonly ArtifactSummaryService _artifactSummary;
    private readonly ToolCapabilitySearchService _toolSearch;
    private readonly ToolGuidanceService _toolGuidance;
    private readonly ContextDeltaSummaryService _contextDelta;
    private readonly TaskRecoveryPlanner _recovery;
    private readonly ExternalTaskIntakeService _externalIntake;
    private readonly TaskQueueCoordinator _taskQueue;
    private readonly ConnectorCallbackPreviewService _callbackPreview;
    private readonly PackCatalogService _packCatalog;
    private readonly WorkspaceCatalogService _workspaceCatalog;
    private readonly StandardsCatalogService _standardsCatalog;
    private readonly PlaybookOrchestrationService _playbookOrchestration;
    private readonly PolicyResolutionService _policyResolution;
    private readonly SpecialistRegistryService _specialistRegistry;
    private readonly CapabilityTaskCompilerService _capabilityCompiler;
    private readonly ProjectInitService _projectInit;
    private readonly ProjectContextComposer _projectContextComposer;
    private readonly ProjectDeepScanService _projectDeepScan;

    internal CopilotTaskService(
        PlatformServices platform,
        Workflow.WorkflowRuntimeService workflow,
        FixLoopService fixLoop,
        ModelStateGraphService graph,
        CopilotTaskRunStore store,
        TaskMetricsService metrics,
        ContextAnchorService anchors,
        ArtifactSummaryService artifactSummary,
        ToolCapabilitySearchService toolSearch,
        ToolGuidanceService toolGuidance,
        ContextDeltaSummaryService contextDelta,
        TaskRecoveryPlanner recovery,
        ExternalTaskIntakeService? externalIntake = null,
        TaskQueueCoordinator? taskQueue = null,
        ConnectorCallbackPreviewService? callbackPreview = null,
        PackCatalogService? packCatalog = null,
        WorkspaceCatalogService? workspaceCatalog = null,
        StandardsCatalogService? standardsCatalog = null,
        PolicyResolutionService? policyResolution = null,
        SpecialistRegistryService? specialistRegistry = null,
        CapabilityTaskCompilerService? capabilityCompiler = null,
        PlaybookOrchestrationService? playbookOrchestration = null,
        ProjectInitService? projectInit = null,
        ProjectContextComposer? projectContextComposer = null,
        ProjectDeepScanService? projectDeepScan = null)
    {
        _platform = platform;
        _workflow = workflow;
        _fixLoop = fixLoop;
        _graph = graph;
        _store = store;
        _metrics = metrics;
        _anchors = anchors;
        _artifactSummary = artifactSummary;
        _toolSearch = toolSearch;
        _toolGuidance = toolGuidance;
        _contextDelta = contextDelta;
        _recovery = recovery;
        _externalIntake = externalIntake ?? new ExternalTaskIntakeService();
        _taskQueue = taskQueue ?? new TaskQueueCoordinator(new CopilotTaskQueueStore(new CopilotStatePaths(store.RootPath)));
        _callbackPreview = callbackPreview ?? new ConnectorCallbackPreviewService();
        _packCatalog = packCatalog ?? new PackCatalogService();
        _workspaceCatalog = workspaceCatalog ?? new WorkspaceCatalogService();
        _standardsCatalog = standardsCatalog ?? new StandardsCatalogService(_packCatalog, _workspaceCatalog);
        _policyResolution = policyResolution ?? new PolicyResolutionService(_packCatalog, _workspaceCatalog);
        _specialistRegistry = specialistRegistry ?? new SpecialistRegistryService(_packCatalog, _workspaceCatalog);
        _playbookOrchestration = playbookOrchestration ?? new PlaybookOrchestrationService(new PlaybookLoaderService(), _packCatalog, _workspaceCatalog, _standardsCatalog);
        _capabilityCompiler = capabilityCompiler ?? new CapabilityTaskCompilerService(_toolSearch, _playbookOrchestration, _policyResolution, _specialistRegistry);
        _projectInit = projectInit ?? new ProjectInitService(_packCatalog, _workspaceCatalog, AppDomain.CurrentDomain.BaseDirectory);
        _projectContextComposer = projectContextComposer ?? new ProjectContextComposer(_projectInit, _workspaceCatalog, _standardsCatalog, _playbookOrchestration, AppDomain.CurrentDomain.BaseDirectory);
        _projectDeepScan = projectDeepScan ?? new ProjectDeepScanService(_projectInit, AppDomain.CurrentDomain.BaseDirectory);
    }

    internal TaskRun Plan(UIApplication uiapp, Document doc, TaskPlanRequest request, string caller, string sessionId)
    {
        request ??= new TaskPlanRequest();
        var documentKey = string.IsNullOrWhiteSpace(request.DocumentKey) ? _platform.GetDocumentKey(doc) : request.DocumentKey;
        var taskKind = (request.TaskKind ?? string.Empty).Trim().ToLowerInvariant();

        TaskRun taskRun;
        switch (taskKind)
        {
            case "workflow":
                var workflowRequest = ParseJson<WorkflowPlanRequest>(request.InputJson);
                workflowRequest.WorkflowName = string.IsNullOrWhiteSpace(workflowRequest.WorkflowName) ? request.TaskName : workflowRequest.WorkflowName;
                workflowRequest.DocumentKey = string.IsNullOrWhiteSpace(workflowRequest.DocumentKey) ? documentKey : workflowRequest.DocumentKey;
                var workflowRun = _workflow.Plan(uiapp, doc, workflowRequest, caller, sessionId);
                taskRun = MapFromWorkflow(workflowRun, request, caller, sessionId);
                break;

            case "fix_loop":
                var fixLoopRequest = ParseJson<FixLoopPlanRequest>(request.InputJson);
                fixLoopRequest.DocumentKey = string.IsNullOrWhiteSpace(fixLoopRequest.DocumentKey) ? documentKey : fixLoopRequest.DocumentKey;
                fixLoopRequest.ScenarioName = string.IsNullOrWhiteSpace(fixLoopRequest.ScenarioName) ? request.TaskName : fixLoopRequest.ScenarioName;
                var fixLoopRun = _fixLoop.Plan(uiapp, doc, fixLoopRequest);
                taskRun = MapFromFixLoop(fixLoopRun, request, caller, sessionId);
                break;

            default:
                throw new InvalidOperationException(StatusCodes.TaskKindNotSupported);
        }

        RefreshRecoveryState(taskRun);
        return SaveRun(taskRun);
    }

    internal TaskRun Preview(UIApplication uiapp, Document doc, TaskPreviewRequest request, ToolRequestEnvelope envelope)
    {
        var taskRun = GetRun(request.RunId);
        try
        {
            if (string.Equals(taskRun.TaskKind, "workflow", StringComparison.OrdinalIgnoreCase))
            {
                var workflowRun = _workflow.GetRun(taskRun.UnderlyingRunId);
                taskRun.ApprovalToken = workflowRun.ApprovalToken;
                taskRun.PreviewRunId = workflowRun.PreviewRunId;
                taskRun.ExpectedContextJson = workflowRun.ExpectedContextJson;
                taskRun.Status = workflowRun.RequiresApproval ? "preview_ready" : "planned";
                taskRun.LastErrorCode = string.Empty;
                taskRun.LastErrorMessage = string.Empty;
                MarkStep(taskRun, "preview", workflowRun.RequiresApproval ? "completed" : "skipped", taskRun.ArtifactKeys, taskRun.ChangedIds, 0, 0);
                AppendCheckpoint(taskRun, "preview", workflowRun.RequiresApproval ? "completed" : "skipped", "Workflow preview hydrated.", string.Empty, string.Empty);
                RefreshRecoveryState(taskRun);
                return SaveRun(taskRun);
            }

            if (!string.Equals(taskRun.TaskKind, "fix_loop", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(StatusCodes.TaskKindNotSupported);
            }

            var actionIds = request.ActionIds.Count > 0
                ? request.ActionIds
                : taskRun.RecommendedActionIds.Count > 0 ? taskRun.RecommendedActionIds : taskRun.SelectedActionIds;
            if (actionIds.Count == 0)
            {
                throw new InvalidOperationException(StatusCodes.TaskStepBlocked);
            }

            taskRun.SelectedActionIds = actionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var previewRequest = new FixLoopApplyRequest
            {
                RunId = taskRun.UnderlyingRunId,
                ActionIds = taskRun.SelectedActionIds,
                AllowMutations = true
            };
            var preview = _fixLoop.PreviewApply(uiapp, doc, previewRequest);
            preview = _platform.FinalizePreviewResult(uiapp, doc, envelope, preview);

            taskRun.ApprovalToken = preview.ApprovalToken;
            taskRun.PreviewRunId = preview.PreviewRunId;
            taskRun.ExpectedContextJson = JsonUtil.Serialize(preview.ResolvedContext ?? _platform.BuildContextFingerprint(uiapp, doc));
            taskRun.ArtifactKeys = preview.Artifacts.ToList();
            taskRun.Diagnostics = preview.Diagnostics.ToList();
            taskRun.Status = "preview_ready";
            taskRun.LastErrorCode = string.Empty;
            taskRun.LastErrorMessage = string.Empty;
            MarkStep(taskRun, "preview", "completed", taskRun.ArtifactKeys, preview.ChangedIds, taskRun.ExpectedDelta, 0);
            AppendCheckpoint(taskRun, "preview", "completed", $"Preview ready with {taskRun.SelectedActionIds.Count} selected actions.", string.Empty, string.Empty);
            RefreshRecoveryState(taskRun);
            return SaveRun(taskRun);
        }
        catch (InvalidOperationException ex)
        {
            PersistFailure(taskRun, "preview", ex.Message, $"Preview blocked: {ex.Message}");
            throw;
        }
    }

    internal TaskRun ApproveStep(TaskApproveStepRequest request, string caller, string sessionId)
    {
        var taskRun = GetRun(request.RunId);
        try
        {
            if (string.IsNullOrWhiteSpace(taskRun.ApprovalToken) && string.IsNullOrWhiteSpace(request.ApprovalToken))
            {
                throw new InvalidOperationException(StatusCodes.TaskApprovalRequired);
            }

            if (!string.IsNullOrWhiteSpace(request.ApprovalToken))
            {
                taskRun.ApprovalToken = request.ApprovalToken;
            }

            if (!string.IsNullOrWhiteSpace(request.PreviewRunId))
            {
                taskRun.PreviewRunId = request.PreviewRunId;
            }

            taskRun.ApprovedByCaller = caller ?? string.Empty;
            taskRun.ApprovedBySessionId = sessionId ?? string.Empty;
            taskRun.ApprovalNote = request.Note ?? string.Empty;
            taskRun.Status = "approved";
            taskRun.LastErrorCode = string.Empty;
            taskRun.LastErrorMessage = string.Empty;
            MarkStep(taskRun, "approve", "completed", taskRun.ArtifactKeys, taskRun.ChangedIds, taskRun.ExpectedDelta, taskRun.ActualDelta);
            AppendCheckpoint(taskRun, "approve", "completed", "Operator approval recorded.", string.Empty, string.Empty);
            RefreshRecoveryState(taskRun);
            return SaveRun(taskRun);
        }
        catch (InvalidOperationException ex)
        {
            PersistFailure(taskRun, "approve", ex.Message, $"Approval blocked: {ex.Message}");
            throw;
        }
    }

    internal TaskRun Execute(UIApplication uiapp, Document doc, TaskExecuteStepRequest request, ToolRequestEnvelope envelope)
    {
        var taskRun = GetRun(request.RunId);
        try
        {
            if (string.Equals(taskRun.Status, "completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(taskRun.Status, "verified", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(StatusCodes.TaskAlreadyCompleted);
            }

            if (RequiresApproval(taskRun) && string.IsNullOrWhiteSpace(taskRun.ApprovalToken))
            {
                throw new InvalidOperationException(StatusCodes.TaskApprovalRequired);
            }

            if (!_platform.MatchesExpectedContextStrict(uiapp, doc, taskRun.ExpectedContextJson))
            {
                throw new InvalidOperationException(StatusCodes.ContextMismatch);
            }

            if (RequiresApproval(taskRun))
            {
                var approvalEnvelope = BuildApprovalEnvelope(taskRun, envelope);
                var approval = _platform.ValidateApprovalRequest(uiapp, doc, approvalEnvelope);
                if (!string.Equals(approval, StatusCodes.Ok, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(approval);
                }
            }

            if (string.Equals(taskRun.TaskKind, "workflow", StringComparison.OrdinalIgnoreCase))
            {
                var workflowRun = _workflow.Apply(uiapp, new WorkflowApplyRequest
                {
                    RunId = taskRun.UnderlyingRunId,
                    ApprovalToken = taskRun.ApprovalToken,
                    AllowMutations = request.AllowMutations
                });

                taskRun = MergeWorkflowExecution(taskRun, workflowRun);
                taskRun.LastErrorCode = string.Empty;
                taskRun.LastErrorMessage = string.Empty;
                RefreshRecoveryState(taskRun);
                return SaveRun(taskRun, reconcileQueue: true);
            }

            if (!string.Equals(taskRun.TaskKind, "fix_loop", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(StatusCodes.TaskKindNotSupported);
            }

            var fixLoopRun = _fixLoop.Apply(uiapp, doc, new FixLoopApplyRequest
            {
                RunId = taskRun.UnderlyingRunId,
                ActionIds = taskRun.SelectedActionIds,
                AllowMutations = request.AllowMutations
            });

            taskRun = MergeFixLoopExecution(taskRun, fixLoopRun);
            taskRun.LastErrorCode = string.Empty;
            taskRun.LastErrorMessage = string.Empty;
            RefreshRecoveryState(taskRun);
            return SaveRun(taskRun, reconcileQueue: true);
        }
        catch (InvalidOperationException ex)
        {
            PersistFailure(taskRun, "execute", ex.Message, $"Execute blocked: {ex.Message}");
            throw;
        }
    }

    internal TaskRun Resume(UIApplication uiapp, Document doc, TaskResumeRequest request, ToolRequestEnvelope envelope)
    {
        var taskRun = GetRun(request.RunId);
        RefreshRecoveryState(taskRun);
        var branch = _recovery.SelectBranch(taskRun, request.RecoveryBranchId);
        if (branch == null)
        {
            PersistFailure(taskRun, "resume", StatusCodes.TaskRecoveryBranchNotFound, "No recovery branch matched the requested resume intent.");
            throw new InvalidOperationException(StatusCodes.TaskRecoveryBranchNotFound);
        }

        if (!branch.AutoResumable)
        {
            var failureCode = branch.RequiresApproval ? StatusCodes.TaskApprovalRequired : StatusCodes.TaskResumeNotAvailable;
            PersistFailure(taskRun, "resume", failureCode, branch.Description);
            throw new InvalidOperationException(failureCode);
        }

        switch (branch.NextAction)
        {
            case "preview":
                if (branch.RequiresFreshPreview)
                {
                    taskRun.ApprovalToken = string.Empty;
                    taskRun.PreviewRunId = string.Empty;
                    MarkStep(taskRun, "preview", "pending", taskRun.ArtifactKeys, taskRun.ChangedIds, taskRun.ExpectedDelta, taskRun.ActualDelta);
                    MarkStep(taskRun, "approve", RequiresApproval(taskRun) ? "pending" : "skipped", taskRun.ArtifactKeys, taskRun.ChangedIds, taskRun.ExpectedDelta, taskRun.ActualDelta);
                    AppendCheckpoint(taskRun, "resume", "completed", branch.Description, branch.ReasonCode, branch.Description);
                    RefreshRecoveryState(taskRun);
                    SaveRun(taskRun);
                }

                return Preview(uiapp, doc, new TaskPreviewRequest
                {
                    RunId = request.RunId,
                    StepId = "preview",
                    ActionIds = taskRun.SelectedActionIds.Count > 0 ? taskRun.SelectedActionIds : taskRun.RecommendedActionIds
                }, envelope);

            case "execute":
                if (string.Equals(taskRun.TaskKind, "workflow", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(taskRun.Status, "partial", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(taskRun.Status, "applied", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        var workflowRun = _workflow.Resume(uiapp, new WorkflowApplyRequest
                        {
                            RunId = taskRun.UnderlyingRunId,
                            ApprovalToken = taskRun.ApprovalToken,
                            AllowMutations = request.AllowMutations
                        });
                        taskRun = MergeWorkflowExecution(taskRun, workflowRun);
                        taskRun.LastErrorCode = string.Empty;
                        taskRun.LastErrorMessage = string.Empty;
                        AppendCheckpoint(taskRun, "resume", "completed", "Workflow resumed from durable checkpoint.", string.Empty, string.Empty);
                        RefreshRecoveryState(taskRun);
                        return SaveRun(taskRun, reconcileQueue: true);
                    }
                    catch (InvalidOperationException ex)
                    {
                        PersistFailure(taskRun, "resume", ex.Message, $"Resume blocked: {ex.Message}");
                        throw;
                    }
                }

                return Execute(uiapp, doc, new TaskExecuteStepRequest
                {
                    RunId = request.RunId,
                    StepId = "execute",
                    AllowMutations = request.AllowMutations
                }, envelope);

            case "verify":
                return Verify(uiapp, doc, new TaskVerifyRequest
                {
                    RunId = request.RunId,
                    MaxResidualIssues = request.MaxResidualIssues
                });

            case "summary":
                taskRun.Status = string.Equals(taskRun.VerificationStatus, "pass", StringComparison.OrdinalIgnoreCase)
                    ? "verified"
                    : string.IsNullOrWhiteSpace(taskRun.Status) ? "completed" : taskRun.Status;
                taskRun.LastErrorCode = string.Empty;
                taskRun.LastErrorMessage = string.Empty;
                MarkStep(taskRun, "summarize", "completed", taskRun.ArtifactKeys, taskRun.ChangedIds, taskRun.ExpectedDelta, taskRun.ActualDelta);
                AppendCheckpoint(taskRun, "summarize", "completed", "Summary checkpoint finalized.", string.Empty, string.Empty);
                RefreshRecoveryState(taskRun);
                return SaveRun(taskRun, reconcileQueue: true);

            default:
                PersistFailure(taskRun, "resume", StatusCodes.TaskResumeNotAvailable, $"Unsupported resume action: {branch.NextAction}");
                throw new InvalidOperationException(StatusCodes.TaskResumeNotAvailable);
        }
    }

    internal TaskRun Verify(UIApplication uiapp, Document doc, TaskVerifyRequest request)
    {
        var taskRun = GetRun(request.RunId);
        try
        {
            if (string.Equals(taskRun.TaskKind, "fix_loop", StringComparison.OrdinalIgnoreCase))
            {
                var fixLoopRun = _fixLoop.Verify(uiapp, doc, new FixLoopVerifyRequest
                {
                    RunId = taskRun.UnderlyingRunId,
                    MaxResidualIssues = request.MaxResidualIssues
                });
                taskRun = MergeFixLoopExecution(taskRun, fixLoopRun);
                taskRun.LastErrorCode = string.Empty;
                taskRun.LastErrorMessage = string.Empty;
                RefreshRecoveryState(taskRun);
                return SaveRun(taskRun, reconcileQueue: true);
            }

            var workflowRun = _workflow.GetRun(taskRun.UnderlyingRunId);
            taskRun.VerificationStatus = workflowRun.Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error) ? "blocked" : "legacy_not_configured";
            taskRun.ResidualSummary = workflowRun.Diagnostics.Count.ToString(CultureInfo.InvariantCulture);
            taskRun.Diagnostics = workflowRun.Diagnostics.ToList();
            taskRun.VerifiedUtc = DateTime.UtcNow;
            taskRun.LastErrorCode = string.Empty;
            taskRun.LastErrorMessage = string.Empty;
            MarkStep(taskRun, "verify", "completed", workflowRun.Evidence.ArtifactKeys.ToList(), workflowRun.ChangedIds.ToList(), taskRun.ExpectedDelta, taskRun.ActualDelta);
            AppendCheckpoint(taskRun, "verify", "completed", "Workflow verification checkpoint recorded.", string.Empty, string.Empty);
            RefreshRecoveryState(taskRun);
            return SaveRun(taskRun, reconcileQueue: true);
        }
        catch (InvalidOperationException ex)
        {
            PersistFailure(taskRun, "verify", ex.Message, $"Verify blocked: {ex.Message}");
            throw;
        }
    }

    internal TaskRun GetRun(string runId)
    {
        var run = _store.TryGet(runId);
        if (run == null)
        {
            throw new InvalidOperationException(StatusCodes.TaskRunNotFound);
        }

        RefreshRecoveryState(run);
        return run;
    }

    internal TaskListResponse ListRuns(TaskListRunsRequest request)
    {
        return new TaskListResponse
        {
            Runs = _store.List(request)
                .Select(x =>
                {
                    RefreshRecoveryState(x);
                    return x;
                })
                .ToList()
        };
    }

    internal TaskSummaryResponse Summarize(TaskSummarizeRequest request)
    {
        return BuildSummary(GetRun(request.RunId));
    }

    internal TaskMetricsResponse GetMetrics(TaskMetricsRequest request)
    {
        return _metrics.Build(_store.List(new TaskListRunsRequest { MaxResults = Math.Max(500, request.MaxResults) }), request);
    }

    internal TaskResidualsResponse GetResiduals(TaskResidualsRequest request)
    {
        var run = GetRun(request.RunId);
        RefreshRecoveryState(run);
        return new TaskResidualsResponse
        {
            RunId = run.RunId,
            VerificationStatus = run.VerificationStatus,
            ResidualSummary = run.ResidualSummary,
            Diagnostics = run.Diagnostics.ToList(),
            RecoveryBranches = run.RecoveryBranches.ToList(),
            LastErrorCode = run.LastErrorCode,
            LastErrorMessage = run.LastErrorMessage
        };
    }

    internal SessionRuntimeHealthResponse GetRuntimeHealth(IEnumerable<ToolManifest> manifests, QueueStateResponse queueState)
    {
        var loadedAssemblyPath = GetLoadedAssemblyPath();
        var configuredAssemblyPath = TryGetConfiguredAssemblyPath();
        var restartRequired = !string.IsNullOrWhiteSpace(configuredAssemblyPath)
            && !PathsEqual(loadedAssemblyPath, configuredAssemblyPath);
        var runtimeWarnings = new List<string>();
        if (restartRequired)
        {
            runtimeWarnings.Add("A newer 765T add-in build is installed. Restart Revit to load the latest runtime.");
        }

        return new SessionRuntimeHealthResponse
        {
            RuntimeMode = "embedded_agent",
            SupportsTaskRuntime = true,
            SupportsContextBroker = true,
            SupportsStateGraph = true,
            SupportsDurableTaskRuns = true,
            StateRootPath = _store.RootPath,
            DurableRunCount = _store.CountRuns(),
            PromotionCount = _store.CountPromotions(),
            ToolCount = manifests.Count(),
            Queue = queueState,
            SupportedTaskKinds = new List<string> { "workflow", "fix_loop" },
            SupportsCheckpointRecovery = true,
            EnabledCapabilityPacks = _platform.Settings.EnabledCapabilityPacks?.ToList() ?? new List<string>(),
            DefaultWorkerProfile = _platform.Settings.DefaultWorkerProfile ?? new WorkerProfile(),
            VisibleShellMode = string.IsNullOrWhiteSpace(_platform.Settings.VisibleShellMode) ? WorkerShellModes.Worker : _platform.Settings.VisibleShellMode,
            DurableQueuePendingCount = _taskQueue.Count("pending"),
            DurableQueueLeasedCount = _taskQueue.Count("leased"),
            ConfiguredProvider = _platform.Settings.LlmProviderLabel,
            PlannerModel = _platform.Settings.LlmPlannerModel,
            ResponseModel = _platform.Settings.LlmResponseModel,
            ReasoningMode = _platform.Settings.LlmConfigured ? WorkerReasoningModes.LlmValidated : WorkerReasoningModes.RuleFirst,
            SecretSourceKind = _platform.Settings.LlmSecretSourceKind,
            LoadedAssemblyPath = loadedAssemblyPath,
            ConfiguredAssemblyPath = configuredAssemblyPath,
            RestartRequired = restartRequired,
            RuntimeWarnings = runtimeWarnings
        };
    }

    private static string GetLoadedAssemblyPath()
    {
        try
        {
            return Path.GetFullPath(typeof(CopilotTaskService).Assembly.Location ?? string.Empty);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TryGetConfiguredAssemblyPath()
    {
        try
        {
            var addinsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Autodesk",
                "Revit",
                "Addins");
            if (!Directory.Exists(addinsRoot))
            {
                return string.Empty;
            }

            var manifestPath = Directory
                .EnumerateFiles(addinsRoot, "BIM765T.Revit.Agent.addin", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .Select(info => info.FullName)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                return string.Empty;
            }

            var manifestContent = File.ReadAllText(manifestPath);
            var match = Regex.Match(
                manifestContent,
                "<Assembly>\\s*(?<path>[^<]+?)\\s*</Assembly>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!match.Success)
            {
                return string.Empty;
            }

            var assemblyPath = match.Groups["path"].Value.Trim();
            return string.IsNullOrWhiteSpace(assemblyPath) ? string.Empty : Path.GetFullPath(assemblyPath);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    internal HotStateResponse GetHotState(UIApplication uiapp, Document doc, HotStateRequest request, IEnumerable<ToolManifest> manifests, QueueStateResponse queueState)
    {
        request ??= new HotStateRequest();
        var context = _platform.GetTaskContext(uiapp, doc, new TaskContextRequest
        {
            DocumentKey = request.DocumentKey,
            MaxRecentEvents = request.MaxRecentEvents,
            MaxRecentOperations = request.MaxRecentOperations,
            IncludeCapabilities = true,
            IncludeToolCatalog = request.IncludeToolCatalog
        }, manifests);

        var pendingTasks = _store.List(new TaskListRunsRequest
        {
            DocumentKey = _platform.GetDocumentKey(doc),
            MaxResults = Math.Max(1, request.MaxPendingTasks)
        })
        .Where(x => !string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(x.Status, "verified", StringComparison.OrdinalIgnoreCase))
        .Select(BuildSummary)
        .ToList();

        return new HotStateResponse
        {
            TaskContext = context,
            Graph = request.IncludeGraph ? _graph.CaptureHotGraph(uiapp, _platform, doc) : new DocumentStateGraphSnapshot { DocumentKey = _platform.GetDocumentKey(doc) },
            Queue = queueState,
            PendingTasks = pendingTasks,
            Tools = request.IncludeToolCatalog ? manifests.OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase).ToList() : new List<ToolManifest>()
        };
    }

    internal ContextResolveBundleResponse ResolveBundle(IEnumerable<ContextBundleItem> hotItems, ContextResolveBundleRequest request)
    {
        return _anchors.ResolveBundle(hotItems, _store.List(new TaskListRunsRequest { MaxResults = 200 }), _store.ListPromotions(), request);
    }

    internal ContextSearchAnchorsResponse SearchAnchors(ContextSearchAnchorsRequest request)
    {
        return _anchors.SearchAnchors(_store.List(new TaskListRunsRequest { MaxResults = 200 }), _store.ListPromotions(), request);
    }

    internal MemoryFindSimilarRunsResponse FindSimilarRuns(MemoryFindSimilarRunsRequest request)
    {
        return _anchors.FindSimilarRuns(_store.List(new TaskListRunsRequest { MaxResults = 200 }), request);
    }

    internal ToolCapabilityLookupResponse FindTools(IEnumerable<ToolManifest> manifests, ToolCapabilityLookupRequest request)
    {
        return _toolSearch.Search(manifests, request);
    }

    internal WorkspaceManifestResponse GetWorkspaceManifest(WorkspaceGetManifestRequest request)
    {
        request ??= new WorkspaceGetManifestRequest();
        return _workspaceCatalog.GetManifest(request.WorkspaceId);
    }

    internal PackListResponse ListPacks(PackListRequest request)
    {
        request ??= new PackListRequest();
        var workspace = _workspaceCatalog.GetManifest(request.WorkspaceId).Workspace;
        var enabledPackIds = new HashSet<string>(workspace.EnabledPacks ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var packs = _packCatalog.GetAll()
            .Where(x => string.IsNullOrWhiteSpace(request.PackType)
                || string.Equals(x.Manifest.PackType, request.PackType, StringComparison.OrdinalIgnoreCase))
            .Where(x => !request.EnabledOnly || enabledPackIds.Contains(x.Manifest.PackId) || x.Manifest.EnabledByDefault)
            .ToList();

        return new PackListResponse
        {
            WorkspaceId = workspace.WorkspaceId,
            Packs = packs
        };
    }

    internal StandardsResolution ResolveStandards(StandardsResolutionRequest request)
    {
        return _standardsCatalog.Resolve(request);
    }

    internal PlaybookMatchResponse MatchPlaybook(IEnumerable<ToolManifest> manifests, PlaybookMatchRequest request)
    {
        return _playbookOrchestration.Match(manifests, request);
    }

    internal PlaybookPreviewResponse PreviewPlaybook(IEnumerable<ToolManifest> manifests, PlaybookPreviewRequest request)
    {
        return _playbookOrchestration.Preview(manifests, request);
    }

    internal PolicyResolution ResolvePolicy(PolicyResolutionRequest request)
    {
        return _policyResolution.Resolve(request);
    }

    internal CapabilitySpecialistResponse ResolveSpecialists(CapabilitySpecialistRequest request)
    {
        return _specialistRegistry.Resolve(request);
    }

    internal CompiledTaskPlan CompileIntentPlan(IEnumerable<ToolManifest> manifests, IntentCompileRequest request)
    {
        return _capabilityCompiler.Compile(manifests, request);
    }

    internal IntentValidationResponse ValidateCompiledIntent(IEnumerable<ToolManifest> manifests, IntentValidateRequest request)
    {
        return _capabilityCompiler.Validate(manifests, request);
    }

    internal SystemGraphSnapshot CaptureSystemGraph(SystemGraphRequest request)
    {
        request ??= new SystemGraphRequest();
        var discipline = string.IsNullOrWhiteSpace(request.Discipline) ? CapabilityDisciplines.Common : request.Discipline;
        var systemNames = (request.SystemNames ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (systemNames.Count == 0 && !string.IsNullOrWhiteSpace(request.Query))
        {
            systemNames.Add("primary_system");
        }

        var snapshot = new SystemGraphSnapshot
        {
            WorkspaceId = string.IsNullOrWhiteSpace(request.WorkspaceId) ? "default" : request.WorkspaceId,
            DocumentKey = request.DocumentKey ?? string.Empty,
            Discipline = discipline
        };

        foreach (var systemName in systemNames)
        {
            var sourceId = $"src::{systemName}";
            var sinkId = $"sink::{systemName}";
            snapshot.Nodes.Add(new SystemGraphNode { NodeId = sourceId, NodeKind = "source", SystemName = systemName });
            snapshot.Nodes.Add(new SystemGraphNode { NodeId = sinkId, NodeKind = "sink", SystemName = systemName, IsOpenEnd = true });
            snapshot.Edges.Add(new SystemGraphEdge { EdgeId = $"edge::{systemName}", FromNodeId = sourceId, ToNodeId = sinkId, EdgeKind = "scaffold_path" });
            snapshot.OpenNodeIds.Add(sinkId);
        }

        snapshot.Summary = $"Scaffold system graph for {snapshot.Discipline}: systems={snapshot.Nodes.Select(x => x.SystemName).Distinct(StringComparer.OrdinalIgnoreCase).Count()}, open_nodes={snapshot.OpenNodeIds.Count}.";
        return snapshot;
    }

    internal FixPlan PlanSystemFix(SystemFixPlanRequest request)
    {
        request ??= new SystemFixPlanRequest();
        var issueKind = string.IsNullOrWhiteSpace(request.IssueKind) ? CapabilityIssueKinds.DisconnectedSystem : request.IssueKind;
        var issue = new IssueRecord
        {
            CapabilityDomain = CapabilityDomains.Systems,
            Discipline = string.IsNullOrWhiteSpace(request.Discipline) ? CapabilityDisciplines.Common : request.Discipline,
            IssueKind = issueKind,
            Severity = "warning",
            Summary = string.IsNullOrWhiteSpace(request.Query)
                ? $"System scaffold issue for {issueKind}."
                : request.Query,
            SourceToolName = ToolNames.SystemCaptureGraph,
            CandidateFixToolNames = new List<string>
            {
                issueKind == CapabilityIssueKinds.SlopeContinuity ? ToolNames.SystemPlanSlopeRemediation : ToolNames.SystemPlanConnectivityFix,
                ToolNames.TaskPreview,
                ToolNames.TaskVerify
            },
            VerificationMode = issueKind == CapabilityIssueKinds.SlopeContinuity
                ? ToolVerificationModes.SystemConsistency
                : ToolVerificationModes.SystemConsistency,
            RequiresApproval = true
        };

        var fixTool = issueKind == CapabilityIssueKinds.SlopeContinuity ? ToolNames.SystemPlanSlopeRemediation : ToolNames.SystemPlanConnectivityFix;
        return new FixPlan
        {
            CapabilityDomain = CapabilityDomains.Systems,
            Discipline = issue.Discipline,
            IssueKind = issueKind,
            Summary = $"Scaffold system fix plan for {issueKind}. Preview/approval/verify through task runtime.",
            Issues = new List<IssueRecord> { issue },
            Proposals = new List<FixProposal>
            {
                new FixProposal
                {
                    IssueId = issue.IssueId,
                    Title = fixTool,
                    Summary = issueKind == CapabilityIssueKinds.SlopeContinuity
                        ? "Prepare bounded slope remediation proposal."
                        : "Prepare bounded connectivity remediation proposal.",
                    ToolName = fixTool,
                    DeterminismLevel = ToolDeterminismLevels.Scaffold,
                    VerificationMode = ToolVerificationModes.SystemConsistency,
                    RequiresApproval = true
                }
            },
            VerificationMode = ToolVerificationModes.SystemConsistency,
            ResidualHints = new List<string>
            {
                "Scaffold only: bind to live system graph + connector topology before production mutation.",
                "Always verify with read-back and residual/manual-review buckets."
            }
        };
    }

    internal ExternalSyncDelta PreviewIntegrationSync(IntegrationPreviewSyncRequest request)
    {
        request ??= new IntegrationPreviewSyncRequest();
        var domain = string.IsNullOrWhiteSpace(request.CapabilityDomain) ? CapabilityDomains.Integration : request.CapabilityDomain;
        var discipline = string.IsNullOrWhiteSpace(request.Discipline) ? CapabilityDisciplines.Common : request.Discipline;
        return new ExternalSyncDelta
        {
            ExternalSystem = request.ExternalSystem ?? string.Empty,
            EntityKind = request.EntityKind ?? string.Empty,
            CapabilityDomain = domain,
            Discipline = discipline,
            AddedCount = 0,
            UpdatedCount = Math.Max(1, (request.SourceArtifactRefs ?? new List<string>()).Count),
            RemovedCount = 0,
            RiskNotes = new List<string>
            {
                "Preview only: no external callback or push is executed in the Revit kernel.",
                "Promote to connector/plugin lane before enabling live sync."
            },
            Summary = $"Preview external sync for {request.ExternalSystem}/{request.EntityKind}: artifact_refs={(request.SourceArtifactRefs ?? new List<string>()).Count}, domain={domain}."
        };
    }

    internal ProjectInitPreviewResponse PreviewProjectInit(ProjectInitPreviewRequest request)
    {
        return _projectInit.Preview(request);
    }

    internal ProjectInitApplyResponse ApplyProjectInit(UIApplication uiapp, ProjectInitApplyRequest request, IEnumerable<ToolManifest> manifests)
    {
        request ??= new ProjectInitApplyRequest();
        var liveReport = request.IncludeLivePrimaryModelSummary && !string.IsNullOrWhiteSpace(request.PrimaryRevitFilePath)
            ? TryCapturePrimaryModelReport(uiapp, request.PrimaryRevitFilePath)
            : null;
        var response = _projectInit.Apply(request, liveReport);
        response.ContextBundle = GetProjectContextBundle(manifests, new ProjectContextBundleRequest
        {
            WorkspaceId = response.WorkspaceId,
            Query = string.IsNullOrWhiteSpace(request.DisplayName) ? "project init" : request.DisplayName,
            MaxSourceRefs = 8,
            MaxStandardsRefs = 6
        });
        return response;
    }

    internal ProjectManifestResponse GetProjectManifest(ProjectManifestRequest request)
    {
        return _projectInit.GetManifest(request);
    }

    internal ProjectContextBundleResponse GetProjectContextBundle(IEnumerable<ToolManifest> manifests, ProjectContextBundleRequest request)
    {
        return _projectContextComposer.GetContextBundle(manifests: manifests, request: request, memoryFinder: FindSimilarRuns);
    }

    internal ProjectDeepScanReportResponse GetProjectDeepScan(ProjectDeepScanGetRequest request)
    {
        return _projectDeepScan.GetReport(request);
    }

    internal string ResolveWorkspaceIdForDocument(Document doc)
    {
        if (doc == null)
        {
            return "default";
        }

        var path = doc.PathName ?? string.Empty;
        var resolved = _projectContextComposer.ResolveWorkspaceIdForPrimaryModelPath(path);
        return string.IsNullOrWhiteSpace(resolved) ? "default" : resolved;
    }

    internal ToolGuidanceResponse GetToolGuidance(IEnumerable<ToolManifest> manifests, ToolGuidanceRequest request)
    {
        return _toolGuidance.Build(manifests, request);
    }

    internal ContextDeltaSummaryResponse GetContextDeltaSummary(UIApplication uiapp, Document doc, ContextDeltaSummaryRequest request, IEnumerable<ToolManifest> manifests, QueueStateResponse queueState)
    {
        request ??= new ContextDeltaSummaryRequest();
        var taskContext = _platform.GetTaskContext(uiapp, doc, new TaskContextRequest
        {
            DocumentKey = request.DocumentKey,
            MaxRecentOperations = request.MaxRecentOperations,
            MaxRecentEvents = request.MaxRecentEvents,
            IncludeCapabilities = false,
            IncludeToolCatalog = false
        }, manifests);

        var insight = BuildContextDeltaInsight(doc, taskContext);
        return _contextDelta.Build(taskContext, queueState, manifests, request, insight);
    }

    internal ArtifactSummaryResponse SummarizeArtifact(ArtifactSummarizeRequest request)
    {
        return _artifactSummary.Summarize(request);
    }

    internal TaskMemoryPromotionResponse PromoteMemory(TaskPromoteMemoryRequest request, string caller)
    {
        var run = GetRun(request.RunId);
        if (!string.Equals(run.VerificationStatus, "pass", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(run.VerificationStatus, "legacy_not_configured", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(StatusCodes.TaskPromotionBlocked);
        }

        var record = _store.SavePromotion(new TaskMemoryPromotionRecord
        {
            RunId = run.RunId,
            PromotionKind = string.IsNullOrWhiteSpace(request.PromotionKind) ? "lesson" : request.PromotionKind,
            Summary = string.IsNullOrWhiteSpace(request.Summary) ? run.PlanSummary : request.Summary,
            Tags = request.Tags.Count > 0 ? request.Tags : new List<string>(run.Tags),
            Notes = request.Notes,
            DocumentKey = run.DocumentKey,
            TaskKind = run.TaskKind,
            TaskName = run.TaskName,
            ArtifactKeys = run.ArtifactKeys.ToList(),
            ApprovedByCaller = caller ?? string.Empty,
            MemoryRecord = new MemoryRecord
            {
                Scope = "project",
                Kind = string.IsNullOrWhiteSpace(request.PromotionKind) ? "lesson" : request.PromotionKind,
                Source = $"task:{run.RunId}",
                Summary = string.IsNullOrWhiteSpace(request.Summary) ? run.PlanSummary : request.Summary,
                Tags = request.Tags.Count > 0 ? request.Tags : new List<string>(run.Tags),
                Confidence = string.Equals(run.VerificationStatus, "pass", StringComparison.OrdinalIgnoreCase) ? 0.9d : 0.6d,
                PromotionStatus = "approved",
                LastVerifiedUtc = run.VerifiedUtc,
                CreatedUtc = DateTime.UtcNow
            }
        });

        return new TaskMemoryPromotionResponse
        {
            Promotion = record,
            CandidatePath = PathCombineSafe(_store.RootPath, "memory", $"{record.PromotionId}.json")
        };
    }

    internal ConnectorTaskIntakeResponse IntakeExternalTask(UIApplication uiapp, Document doc, ExternalTaskIntakeRequest request, string caller, string sessionId)
    {
        var normalized = _externalIntake.Normalize(request, _platform.Settings.DefaultWorkerProfile ?? new WorkerProfile());
        var run = Plan(uiapp, doc, normalized, caller, sessionId);
        run.ConnectorTask = normalized.ConnectorTask ?? new ConnectorTaskEnvelope();
        run = SaveRun(run);

        return new ConnectorTaskIntakeResponse
        {
            CreatedRun = run,
            NormalizedRequest = normalized,
            ConnectorSummary = _externalIntake.BuildSummary(run.ConnectorTask),
            CallbackPreviewAvailable = !string.IsNullOrWhiteSpace(run.TaskSpec?.CallbackTarget?.System)
                || !string.IsNullOrWhiteSpace(run.ConnectorTask?.ExternalSystem)
        };
    }

    internal TaskQueueItem EnqueueApproved(TaskQueueEnqueueRequest request, string caller)
    {
        var run = GetRun(request.RunId);
        var item = _taskQueue.Enqueue(run, request, caller);
        run.LastQueueItemId = item.QueueItemId;
        SaveRun(run);
        return item;
    }

    internal TaskQueueListResponse ListQueue(TaskQueueListRequest request)
    {
        return new TaskQueueListResponse
        {
            Items = _taskQueue.List(request)
        };
    }

    internal TaskQueueItem ClaimQueueItem(TaskQueueClaimRequest request)
    {
        return _taskQueue.ClaimNext(request);
    }

    internal TaskQueueItem CompleteQueueItem(TaskQueueCompleteRequest request)
    {
        var existing = _taskQueue.TryGet(request.QueueItemId);
        var run = existing != null ? _store.TryGet(existing.RunId) : null;
        return _taskQueue.Complete(request, run);
    }

    internal TaskQueueRunResponse RunQueueItem(UIApplication uiapp, TaskQueueRunRequest request, ToolRequestEnvelope envelope)
    {
        var queueItem = _taskQueue.Claim(request.QueueItemId, request.LeaseOwner);
        var run = GetRun(queueItem.RunId);

        if (string.Equals(run.Status, "verified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            run = SaveRun(run, reconcileQueue: true);
            queueItem = _taskQueue.TryGet(queueItem.QueueItemId) ?? queueItem;
            return new TaskQueueRunResponse
            {
                QueueItem = queueItem,
                Run = run,
                Summary = BuildSummary(run),
                CallbackPreview = _callbackPreview.Build(run, queueItem, request.ResultStatusOverride)
            };
        }

        var doc = _platform.ResolveDocument(uiapp, run.DocumentKey);
        TaskRun executedRun;
        if (string.Equals(run.Status, "approved", StringComparison.OrdinalIgnoreCase)
            || string.Equals(run.Status, "preview_ready", StringComparison.OrdinalIgnoreCase))
        {
            executedRun = Execute(uiapp, doc, new TaskExecuteStepRequest
            {
                RunId = run.RunId,
                StepId = "execute",
                AllowMutations = request.AllowMutations
            }, envelope);
        }
        else if (string.Equals(run.Status, "blocked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(run.Status, "partial", StringComparison.OrdinalIgnoreCase)
            || string.Equals(run.Status, "applied", StringComparison.OrdinalIgnoreCase))
        {
            executedRun = Resume(uiapp, doc, new TaskResumeRequest
            {
                RunId = run.RunId,
                AllowMutations = request.AllowMutations,
                RecoveryBranchId = request.RecoveryBranchId,
                MaxResidualIssues = request.MaxResidualIssues
            }, envelope);
        }
        else
        {
            throw new InvalidOperationException(StatusCodes.TaskQueueBlocked);
        }

        if (request.AutoVerify
            && !string.Equals(executedRun.Status, "verified", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(executedRun.Status, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            executedRun = Verify(uiapp, doc, new TaskVerifyRequest
            {
                RunId = executedRun.RunId,
                MaxResidualIssues = request.MaxResidualIssues
            });
        }

        executedRun = SaveRun(executedRun, reconcileQueue: true);
        queueItem = _taskQueue.TryGet(queueItem.QueueItemId) ?? queueItem;
        return new TaskQueueRunResponse
        {
            QueueItem = queueItem,
            Run = executedRun,
            Summary = BuildSummary(executedRun),
            CallbackPreview = _callbackPreview.Build(executedRun, queueItem, request.ResultStatusOverride)
        };
    }

    internal ConnectorCallbackPreviewResponse BuildCallbackPreview(ConnectorCallbackPreviewRequest request)
    {
        var run = GetRun(request.RunId);
        var queueItem = !string.IsNullOrWhiteSpace(request.QueueItemId)
            ? _taskQueue.TryGet(request.QueueItemId)
            : !string.IsNullOrWhiteSpace(run.LastQueueItemId) ? _taskQueue.TryGet(run.LastQueueItemId) : null;
        return _callbackPreview.Build(run, queueItem, request.ResultStatus);
    }

    private TaskSummaryResponse BuildSummary(TaskRun run)
    {
        EnsureTaskRunCollections(run);
        RefreshRecoveryState(run);
        return new TaskSummaryResponse
        {
            RunId = run.RunId,
            TaskKind = run.TaskKind,
            TaskName = run.TaskName,
            Status = run.Status,
            IntentSummary = run.IntentSummary,
            PlanSummary = run.PlanSummary,
            NextAction = _recovery.InferNextAction(run),
            ChangedCount = run.ChangedIds.Count,
            ResidualCount = ParseResidualCount(run.ResidualSummary),
            UpdatedUtc = run.UpdatedUtc,
            CheckpointCount = run.Checkpoints.Count,
            RecoveryBranchCount = run.RecoveryBranches.Count,
            CanResume = run.RecoveryBranches.Any(x => x.AutoResumable),
            LastErrorCode = run.LastErrorCode,
            CapabilityPack = run.CapabilityPack,
            PrimarySkillGroup = run.PrimarySkillGroup,
            WorkerPersonaId = run.WorkerProfile?.PersonaId ?? string.Empty,
            RunReport = run.RunReport ?? new RunReport()
        };
    }

    private TaskRun MapFromWorkflow(WorkflowRun workflowRun, TaskPlanRequest request, string caller, string sessionId)
    {
        var run = CreateBaseTaskRun("workflow", workflowRun.WorkflowName, request, caller, sessionId, workflowRun.DocumentKey);
        run.UnderlyingRunId = workflowRun.RunId;
        run.UnderlyingKind = "workflow";
        run.ExpectedContextJson = workflowRun.ExpectedContextJson;
        run.PlanSummary = workflowRun.Evidence.PlanSummary;
        run.ArtifactKeys = workflowRun.Evidence.ArtifactKeys.ToList();
        run.Diagnostics = workflowRun.Diagnostics.ToList();
        run.ApprovalToken = workflowRun.ApprovalToken;
        run.PreviewRunId = workflowRun.PreviewRunId;
        run.Status = workflowRun.RequiresApproval ? "preview_ready" : workflowRun.Status;
        run.Steps = CreateDefaultSteps(workflowRun.RequiresApproval);
        MarkStep(run, "plan", "completed", workflowRun.Evidence.ArtifactKeys.ToList(), workflowRun.ChangedIds.ToList(), 0, 0);
        if (workflowRun.RequiresApproval)
        {
            MarkStep(run, "preview", "completed", workflowRun.Evidence.ArtifactKeys.ToList(), workflowRun.ChangedIds.ToList(), 0, 0);
        }

        AppendCheckpoint(run, "plan", "completed", "Workflow planned and persisted.", string.Empty, string.Empty);
        return run;
    }

    private TaskRun MapFromFixLoop(FixLoopRun fixLoopRun, TaskPlanRequest request, string caller, string sessionId)
    {
        var run = CreateBaseTaskRun("fix_loop", fixLoopRun.ScenarioName, request, caller, sessionId, fixLoopRun.DocumentKey);
        run.UnderlyingRunId = fixLoopRun.RunId;
        run.UnderlyingKind = "fix_loop";
        run.ExpectedContextJson = fixLoopRun.ExpectedContextJson;
        run.PlanSummary = fixLoopRun.Evidence.PlanSummary;
        run.ArtifactKeys = fixLoopRun.Evidence.ArtifactKeys.ToList();
        run.Diagnostics = fixLoopRun.Diagnostics.ToList();
        run.RecommendedActionIds = fixLoopRun.RecommendedActionIds.ToList();
        run.SelectedActionIds = fixLoopRun.RecommendedActionIds.ToList();
        run.ExpectedDelta = fixLoopRun.CandidateActions
            .Where(x => fixLoopRun.RecommendedActionIds.Contains(x.ActionId))
            .Sum(x => x.Verification.ExpectedIssueDelta);
        run.Steps = CreateDefaultSteps(true);
        MarkStep(run, "plan", "completed", run.ArtifactKeys, run.ChangedIds, run.ExpectedDelta, 0);
        AppendCheckpoint(run, "plan", "completed", "Fix-loop plan created with recommended actions.", string.Empty, string.Empty);
        return run;
    }

    private TaskRun CreateBaseTaskRun(string taskKind, string taskName, TaskPlanRequest request, string caller, string sessionId, string documentKey)
    {
        var taskSpec = BuildTaskSpec(request, taskName, documentKey);
        var workerProfile = BuildWorkerProfile(request);
        return new TaskRun
        {
            RunId = Guid.NewGuid().ToString("N"),
            TaskKind = taskKind,
            TaskName = taskName,
            Status = "planned",
            DocumentKey = documentKey,
            IntentSummary = string.IsNullOrWhiteSpace(request.IntentSummary)
                ? $"{taskKind}:{taskName}"
                : request.IntentSummary,
            InputJson = string.IsNullOrWhiteSpace(request.InputJson) ? "{}" : request.InputJson,
            PlannedByCaller = caller ?? string.Empty,
            PlannedBySessionId = sessionId ?? string.Empty,
            Tags = request.Tags.ToList(),
            TaskSpec = taskSpec,
            WorkerProfile = workerProfile,
            CapabilityPack = InferTaskCapabilityPack(request, taskKind, taskName),
            PrimarySkillGroup = InferPrimarySkillGroup(request, taskKind, taskName),
            QueueEligible = taskSpec.ApprovalPolicy?.AllowQueuedExecution == true,
            ConnectorTask = request.ConnectorTask ?? new ConnectorTaskEnvelope(),
            RunReport = new RunReport
            {
                TaskSummary = string.IsNullOrWhiteSpace(request.IntentSummary) ? $"{taskKind}:{taskName}" : request.IntentSummary
            }
        };
    }

    private static List<TaskStepState> CreateDefaultSteps(bool requiresApproval)
    {
        return new List<TaskStepState>
        {
            new TaskStepState { StepId = "plan", Title = "Plan", StepKind = "plan", Status = "pending" },
            new TaskStepState { StepId = "preview", Title = "Preview", StepKind = "preview", Status = "pending" },
            new TaskStepState { StepId = "approve", Title = "Approve", StepKind = "approval", Status = requiresApproval ? "pending" : "skipped", RequiresApproval = requiresApproval },
            new TaskStepState { StepId = "execute", Title = "Execute", StepKind = "execute", Status = "pending", RequiresApproval = requiresApproval },
            new TaskStepState { StepId = "verify", Title = "Verify", StepKind = "verify", Status = "pending" },
            new TaskStepState { StepId = "summarize", Title = "Summarize", StepKind = "summary", Status = "pending" }
        };
    }

    private static void MarkStep(TaskRun run, string stepId, string status, List<string> artifacts, List<int> changedIds, int expectedDelta, int actualDelta)
    {
        EnsureTaskRunCollections(run);
        var step = run.Steps.FirstOrDefault(x => string.Equals(x.StepId, stepId, StringComparison.OrdinalIgnoreCase));
        if (step == null)
        {
            return;
        }

        step.Status = status;
        step.ArtifactKeys = artifacts.ToList();
        step.ChangedIds = changedIds.ToList();
        step.ExpectedDelta = expectedDelta;
        step.ActualDelta = actualDelta;
        step.UpdatedUtc = DateTime.UtcNow;
    }

    private void RefreshRecoveryState(TaskRun run)
    {
        EnsureTaskRunCollections(run);
        run.RecoveryBranches = _recovery.Build(run);
        RefreshRunReport(run);
    }

    private void AppendCheckpoint(TaskRun run, string stepId, string status, string summary, string reasonCode, string reasonMessage)
    {
        EnsureTaskRunCollections(run);
        RefreshRecoveryState(run);
        var nextAction = _recovery.InferNextAction(run);
        var canResume = run.RecoveryBranches.Any(x => x.AutoResumable);
        var latest = run.Checkpoints.LastOrDefault();
        if (latest != null
            && string.Equals(latest.StepId, stepId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(latest.Status, status, StringComparison.OrdinalIgnoreCase)
            && string.Equals(latest.ReasonCode, reasonCode ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && string.Equals(latest.NextAction, nextAction, StringComparison.OrdinalIgnoreCase)
            && string.Equals(latest.Summary, summary ?? string.Empty, StringComparison.Ordinal))
        {
            return;
        }

        run.Checkpoints.Add(new TaskCheckpointRecord
        {
            CheckpointId = Guid.NewGuid().ToString("N"),
            StepId = stepId,
            Status = status,
            Summary = summary ?? string.Empty,
            ReasonCode = reasonCode ?? string.Empty,
            ReasonMessage = reasonMessage ?? string.Empty,
            NextAction = nextAction,
            CanResume = canResume,
            ArtifactKeys = run.ArtifactKeys.ToList(),
            ChangedIds = run.ChangedIds.ToList(),
            ExpectedDelta = run.ExpectedDelta,
            ActualDelta = run.ActualDelta,
            CreatedUtc = DateTime.UtcNow
        });
    }

    private TaskRun SaveRun(TaskRun run, bool reconcileQueue = false)
    {
        var saved = _store.Save(run);
        if (reconcileQueue)
        {
            var queueItem = _taskQueue.Reconcile(saved);
            if (queueItem != null && !string.Equals(saved.LastQueueItemId, queueItem.QueueItemId, StringComparison.OrdinalIgnoreCase))
            {
                saved.LastQueueItemId = queueItem.QueueItemId;
                saved = _store.Save(saved);
            }
        }

        return saved;
    }

    private void PersistFailure(TaskRun run, string stepId, string statusCode, string message)
    {
        run.LastErrorCode = statusCode ?? string.Empty;
        run.LastErrorMessage = message ?? string.Empty;
        run.Status = string.Equals(run.Status, "verified", StringComparison.OrdinalIgnoreCase)
            || string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase)
            ? run.Status
            : "blocked";
        MarkStep(run, stepId, "blocked", run.ArtifactKeys, run.ChangedIds, run.ExpectedDelta, run.ActualDelta);
        AppendCheckpoint(run, stepId, "blocked", message ?? string.Empty, statusCode ?? string.Empty, message ?? string.Empty);
        RefreshRecoveryState(run);
        SaveRun(run, reconcileQueue: true);
    }

    private TaskRun MergeFixLoopExecution(TaskRun taskRun, FixLoopRun fixLoopRun)
    {
        taskRun.Status = string.Equals(fixLoopRun.Verification.Status, "pass", StringComparison.OrdinalIgnoreCase) ? "verified" : fixLoopRun.Status;
        taskRun.ArtifactKeys = fixLoopRun.Evidence.ArtifactKeys.ToList();
        taskRun.Diagnostics = fixLoopRun.Diagnostics.ToList();
        taskRun.ChangedIds = fixLoopRun.ChangedIds.ToList();
        var duration = (fixLoopRun.VerifiedUtc ?? fixLoopRun.AppliedUtc ?? fixLoopRun.PlannedUtc) - fixLoopRun.PlannedUtc;
        taskRun.DurationMs = duration.TotalMilliseconds < 0 ? 0L : (long)duration.TotalMilliseconds;
        taskRun.ExpectedDelta = fixLoopRun.Evidence.ExpectedIssueDelta;
        taskRun.ActualDelta = fixLoopRun.Evidence.ActualIssueDelta;
        taskRun.VerificationStatus = fixLoopRun.Verification.Status;
        taskRun.ResidualSummary = fixLoopRun.Verification.ResidualIssues.Count.ToString(CultureInfo.InvariantCulture);
        taskRun.VerifiedUtc = fixLoopRun.VerifiedUtc;
        taskRun.SelectedActionIds = fixLoopRun.Evidence.SelectedActionIds.ToList();
        taskRun.RecommendedActionIds = fixLoopRun.Evidence.RecommendedActionIds.ToList();
        MarkStep(taskRun, "execute", "completed", taskRun.ArtifactKeys, taskRun.ChangedIds, taskRun.ExpectedDelta, taskRun.ActualDelta);
        MarkStep(taskRun, "verify", "completed", taskRun.ArtifactKeys, taskRun.ChangedIds, taskRun.ExpectedDelta, taskRun.ActualDelta);
        MarkStep(taskRun, "summarize", "completed", taskRun.ArtifactKeys, taskRun.ChangedIds, taskRun.ExpectedDelta, taskRun.ActualDelta);
        AppendCheckpoint(taskRun, "execute", "completed", "Fix-loop execution applied and verification updated.", string.Empty, string.Empty);
        return taskRun;
    }

    private TaskRun MergeWorkflowExecution(TaskRun taskRun, WorkflowRun workflowRun)
    {
        taskRun.Status = workflowRun.Status;
        taskRun.ArtifactKeys = workflowRun.Evidence.ArtifactKeys.ToList();
        taskRun.Diagnostics = workflowRun.Diagnostics.ToList();
        taskRun.ChangedIds = workflowRun.ChangedIds.ToList();
        taskRun.DurationMs = workflowRun.DurationMs;
        taskRun.VerificationStatus = workflowRun.Diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error) ? "blocked" : "legacy_not_configured";
        taskRun.ResidualSummary = workflowRun.Diagnostics.Count.ToString(CultureInfo.InvariantCulture);
        taskRun.VerifiedUtc = DateTime.UtcNow;
        MarkStep(taskRun, "execute", "completed", taskRun.ArtifactKeys, taskRun.ChangedIds, 0, taskRun.ChangedIds.Count);
        MarkStep(taskRun, "verify", "completed", taskRun.ArtifactKeys, taskRun.ChangedIds, 0, taskRun.ChangedIds.Count);
        MarkStep(taskRun, "summarize", "completed", taskRun.ArtifactKeys, taskRun.ChangedIds, 0, taskRun.ChangedIds.Count);
        AppendCheckpoint(taskRun, "execute", "completed", "Workflow apply/resume completed.", string.Empty, string.Empty);
        return taskRun;
    }

    private ToolRequestEnvelope BuildApprovalEnvelope(TaskRun taskRun, ToolRequestEnvelope current)
    {
        return new ToolRequestEnvelope
        {
            RequestId = current.RequestId,
            ToolName = current.ToolName,
            PayloadJson = current.PayloadJson,
            Caller = current.Caller,
            SessionId = current.SessionId,
            DryRun = false,
            ApprovalToken = taskRun.ApprovalToken,
            ExpectedContextJson = taskRun.ExpectedContextJson,
            PreviewRunId = taskRun.PreviewRunId,
            TargetDocument = taskRun.DocumentKey,
            CorrelationId = current.CorrelationId
        };
    }

    private ProjectPrimaryModelReport TryCapturePrimaryModelReport(UIApplication uiapp, string primaryRevitFilePath)
    {
        var normalizedPath = ProjectInitService.NormalizeExistingPath(primaryRevitFilePath) ?? primaryRevitFilePath ?? string.Empty;
        Document? openedDoc = null;
        try
        {
            openedDoc = uiapp.Application.OpenDocumentFile(normalizedPath);
            var summary = _platform.SummarizeDocument(uiapp, openedDoc);
            return new ProjectPrimaryModelReport
            {
                FilePath = normalizedPath,
                Status = ProjectPrimaryModelStatuses.Captured,
                PendingLiveSummary = false,
                CapturedUtc = DateTime.UtcNow,
                Summary = $"Live summary captured for {summary.Title}.",
                DocumentSummary = summary
            };
        }
        catch
        {
            return new ProjectPrimaryModelReport
            {
                FilePath = normalizedPath,
                Status = ProjectPrimaryModelStatuses.PendingLiveSummary,
                PendingLiveSummary = true,
                Summary = "Primary model live summary pending vi Revit kernel chua capture duoc ngay luc init."
            };
        }
        finally
        {
            if (openedDoc != null)
            {
                try
                {
                    var activeDoc = uiapp.ActiveUIDocument?.Document;
                    if (activeDoc == null || !activeDoc.Equals(openedDoc))
                    {
                        openedDoc.Close(false);
                    }
                }
                catch
                {
                }
            }
        }
    }

    private ContextDeltaInsight BuildContextDeltaInsight(Document doc, TaskContextResponse taskContext)
    {
        var insight = new ContextDeltaInsight();
        var operations = taskContext?.RecentOperations ?? new List<OperationJournalEntry>();
        var events = taskContext?.RecentEvents ?? new List<EventRecord>();
        var recentIds = operations.SelectMany(x => x.ChangedIds ?? new List<int>())
            .Concat(events.SelectMany(x => x.ElementIds ?? new List<int>()))
            .Where(x => x > 0)
            .Distinct()
            .ToList();

        if (recentIds.Count == 0)
        {
            return insight;
        }

        var categoryCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var disciplineCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var existingCount = 0;
        var missingCount = 0;

        foreach (var id in recentIds)
        {
            var element = doc.GetElement(new ElementId((long)id));
            if (element == null)
            {
                missingCount++;
                continue;
            }

            existingCount++;
            var category = element.Category?.Name ?? "<No Category>";
            categoryCounts[category] = categoryCounts.TryGetValue(category, out var current) ? current + 1 : 1;

            var discipline = ClassifyDiscipline(category);
            disciplineCounts[discipline] = disciplineCounts.TryGetValue(discipline, out current) ? current + 1 : 1;
        }

        var mutationKinds = operations
            .Where(x => x.ChangedIds != null && x.ChangedIds.Count > 0)
            .Select(x => ClassifyMutationKind(x.ToolName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var createdEstimate = operations
            .Where(x => string.Equals(ClassifyMutationKind(x.ToolName), "create", StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.ChangedIds ?? new List<int>())
            .Distinct()
            .Count();
        var deletedEstimate = Math.Max(
            missingCount,
            operations
                .Where(x => string.Equals(ClassifyMutationKind(x.ToolName), "delete", StringComparison.OrdinalIgnoreCase))
                .SelectMany(x => x.ChangedIds ?? new List<int>())
                .Distinct()
                .Count());

        insight.AddedElementEstimate = createdEstimate;
        insight.RemovedElementEstimate = deletedEstimate;
        insight.ModifiedElementEstimate = Math.Max(0, existingCount - createdEstimate);
        insight.TopCategories = categoryCounts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(x => new CountByNameDto { Name = x.Key, Count = x.Value })
            .ToList();
        insight.DisciplineHints = disciplineCounts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .Select(x => new CountByNameDto { Name = x.Key, Count = x.Value })
            .ToList();
        insight.RecentMutationKinds = mutationKinds;
        return insight;
    }

    private static string ClassifyDiscipline(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return "General";
        }

        if (categoryName.IndexOf("Pipe", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Duct", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Cable", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Mechanical", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Plumbing", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Air", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "MEP";
        }

        if (categoryName.IndexOf("Structural", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Rebar", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Foundation", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Framing", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Structural";
        }

        if (categoryName.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Floor", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Roof", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Window", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Room", StringComparison.OrdinalIgnoreCase) >= 0
            || categoryName.IndexOf("Ceiling", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Architecture";
        }

        return "General";
    }

    private static string ClassifyMutationKind(string toolName)
    {
        var value = toolName ?? string.Empty;
        if (value.IndexOf("delete", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("remove", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("cleanup", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "delete";
        }

        if (value.IndexOf("create", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("place", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("build", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("load", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "create";
        }

        return "modify";
    }

    private static T ParseJson<T>(string raw) where T : new()
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new T();
        }

        return JsonUtil.DeserializePayloadOrDefault<T>(raw);
    }

    private static bool RequiresApproval(TaskRun taskRun)
    {
        EnsureTaskRunCollections(taskRun);
        return taskRun.Steps.Any(x => string.Equals(x.StepId, "approve", StringComparison.OrdinalIgnoreCase) && x.RequiresApproval);
    }

    private static int ParseResidualCount(string residualSummary)
    {
        if (int.TryParse(residualSummary, out var residuals))
        {
            return residuals;
        }

        return string.IsNullOrWhiteSpace(residualSummary) ? 0 : 1;
    }

    private static string PathCombineSafe(string root, string leaf, string fileName)
    {
        return System.IO.Path.Combine(root ?? string.Empty, leaf ?? string.Empty, fileName ?? string.Empty);
    }

    private static void EnsureTaskRunCollections(TaskRun run)
    {
        if (run == null)
        {
            return;
        }

        run.RecommendedActionIds ??= new List<string>();
        run.SelectedActionIds ??= new List<string>();
        run.Steps ??= new List<TaskStepState>();
        run.Diagnostics ??= new List<DiagnosticRecord>();
        run.ChangedIds ??= new List<int>();
        run.ArtifactKeys ??= new List<string>();
        run.Tags ??= new List<string>();
        run.Checkpoints ??= new List<TaskCheckpointRecord>();
        run.RecoveryBranches ??= new List<TaskRecoveryBranch>();
        run.IntentSummary ??= string.Empty;
        run.PlanSummary ??= string.Empty;
        run.TaskKind ??= string.Empty;
        run.TaskName ??= string.Empty;
        run.DocumentKey ??= string.Empty;
        run.Status ??= "planned";
        run.ResidualSummary ??= string.Empty;
        run.LastErrorCode ??= string.Empty;
        run.LastErrorMessage ??= string.Empty;
        run.TaskSpec ??= new TaskSpec();
        run.WorkerProfile ??= new WorkerProfile();
        run.RunReport ??= new RunReport();
        run.CapabilityPack ??= WorkerCapabilityPacks.CoreWorker;
        run.PrimarySkillGroup ??= WorkerSkillGroups.Orchestration;
        run.ConnectorTask ??= new ConnectorTaskEnvelope();
        run.LastQueueItemId ??= string.Empty;
    }

    private TaskSpec BuildTaskSpec(TaskPlanRequest request, string taskName, string documentKey)
    {
        var spec = request.TaskSpec ?? new TaskSpec();
        var connectorSystem = request.ConnectorTask?.ExternalSystem;
        if (string.IsNullOrWhiteSpace(spec.Source))
        {
            spec.Source = string.IsNullOrWhiteSpace(connectorSystem) ? "panel" : (connectorSystem ?? "panel");
        }
        else
        {
            spec.Source = spec.Source ?? "panel";
        }
        spec.Goal = string.IsNullOrWhiteSpace(spec.Goal)
            ? (!string.IsNullOrWhiteSpace(request.IntentSummary) ? request.IntentSummary : taskName)
            : spec.Goal;
        spec.DocumentScope = string.IsNullOrWhiteSpace(spec.DocumentScope) ? documentKey : spec.DocumentScope;
        spec.ProjectScope = string.IsNullOrWhiteSpace(spec.ProjectScope) ? request.ConnectorTask?.ProjectRef ?? string.Empty : spec.ProjectScope;
        spec.Constraints ??= new List<string>();
        spec.ApprovalPolicy ??= new TaskApprovalPolicy();
        if (spec.ApprovalPolicy.MaxBatchSize <= 0)
        {
            spec.ApprovalPolicy.MaxBatchSize = 100;
        }

        spec.Deliverables ??= new List<TaskDeliverableSpec>();
        spec.CallbackTarget ??= new TaskCallbackTarget();
        spec.CallbackTarget.System = string.IsNullOrWhiteSpace(spec.CallbackTarget.System) ? request.ConnectorTask?.ExternalSystem ?? string.Empty : spec.CallbackTarget.System;
        spec.CallbackTarget.Reference = string.IsNullOrWhiteSpace(spec.CallbackTarget.Reference) ? request.ConnectorTask?.ExternalTaskRef ?? string.Empty : spec.CallbackTarget.Reference;
        spec.CallbackTarget.Mode = string.IsNullOrWhiteSpace(spec.CallbackTarget.Mode) ? request.ConnectorTask?.CallbackMode ?? "panel_only" : spec.CallbackTarget.Mode;
        spec.CallbackTarget.Destination = string.IsNullOrWhiteSpace(spec.CallbackTarget.Destination) ? request.ConnectorTask?.ProjectRef ?? string.Empty : spec.CallbackTarget.Destination;
        return spec;
    }

    private WorkerProfile BuildWorkerProfile(TaskPlanRequest request)
    {
        var configured = _platform.Settings.DefaultWorkerProfile ?? new WorkerProfile();
        var profile = request.WorkerProfile;
        if (profile == null || LooksImplicitDefaultProfile(profile))
        {
            return CloneProfile(configured);
        }

        profile.PersonaId = string.IsNullOrWhiteSpace(profile.PersonaId) ? configured.PersonaId : profile.PersonaId;
        profile.Tone = string.IsNullOrWhiteSpace(profile.Tone) ? configured.Tone : profile.Tone;
        profile.QaStrictness = string.IsNullOrWhiteSpace(profile.QaStrictness) ? configured.QaStrictness : profile.QaStrictness;
        profile.RiskTolerance = string.IsNullOrWhiteSpace(profile.RiskTolerance) ? configured.RiskTolerance : profile.RiskTolerance;
        profile.EscalationStyle = string.IsNullOrWhiteSpace(profile.EscalationStyle) ? configured.EscalationStyle : profile.EscalationStyle;
        profile.AllowedSkillGroups ??= new List<string>();
        if (profile.AllowedSkillGroups.Count == 0 && configured.AllowedSkillGroups != null && configured.AllowedSkillGroups.Count > 0)
        {
            profile.AllowedSkillGroups = configured.AllowedSkillGroups.ToList();
        }

        return profile;
    }

    private static WorkerProfile CloneProfile(WorkerProfile profile)
    {
        return new WorkerProfile
        {
            PersonaId = profile.PersonaId ?? WorkerPersonas.FreelancerDefault,
            Tone = profile.Tone ?? "pragmatic",
            QaStrictness = profile.QaStrictness ?? "standard",
            RiskTolerance = profile.RiskTolerance ?? "guarded",
            EscalationStyle = profile.EscalationStyle ?? "checkpoint_first",
            AllowedSkillGroups = profile.AllowedSkillGroups != null
                ? profile.AllowedSkillGroups.ToList()
                : new List<string>()
        };
    }

    private static bool LooksImplicitDefaultProfile(WorkerProfile profile)
    {
        return string.Equals(profile.PersonaId, WorkerPersonas.FreelancerDefault, StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.Tone, "pragmatic", StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.QaStrictness, "standard", StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.RiskTolerance, "guarded", StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.EscalationStyle, "checkpoint_first", StringComparison.OrdinalIgnoreCase)
            && (profile.AllowedSkillGroups == null || profile.AllowedSkillGroups.Count == 0);
    }

    private static string InferTaskCapabilityPack(TaskPlanRequest request, string taskKind, string taskName)
    {
        if (!string.IsNullOrWhiteSpace(request.PreferredCapabilityPack))
        {
            return request.PreferredCapabilityPack;
        }

        var joined = string.Join(" ", new[]
        {
            taskKind ?? string.Empty,
            taskName ?? string.Empty,
            request.IntentSummary ?? string.Empty,
            request.InputJson ?? string.Empty
        });

        return joined.IndexOf("family", StringComparison.OrdinalIgnoreCase) >= 0
            || joined.IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0
            || joined.IndexOf("dynamo", StringComparison.OrdinalIgnoreCase) >= 0
            ? WorkerCapabilityPacks.AutomationLab
            : WorkerCapabilityPacks.CoreWorker;
    }

    private static string InferPrimarySkillGroup(TaskPlanRequest request, string taskKind, string taskName)
    {
        if (request.WorkerProfile?.AllowedSkillGroups != null && request.WorkerProfile.AllowedSkillGroups.Count == 1)
        {
            return request.WorkerProfile.AllowedSkillGroups[0];
        }

        var joined = string.Join(" ", new[]
        {
            taskKind ?? string.Empty,
            taskName ?? string.Empty,
            request.IntentSummary ?? string.Empty,
            request.InputJson ?? string.Empty
        });

        if (joined.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0
            || joined.IndexOf("view", StringComparison.OrdinalIgnoreCase) >= 0
            || joined.IndexOf("annotation", StringComparison.OrdinalIgnoreCase) >= 0
            || joined.IndexOf("schedule", StringComparison.OrdinalIgnoreCase) >= 0
            || joined.IndexOf("export", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return WorkerSkillGroups.Documentation;
        }

        if (joined.IndexOf("qc", StringComparison.OrdinalIgnoreCase) >= 0
            || joined.IndexOf("review", StringComparison.OrdinalIgnoreCase) >= 0
            || joined.IndexOf("audit", StringComparison.OrdinalIgnoreCase) >= 0
            || joined.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0
            || joined.IndexOf("verify", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return WorkerSkillGroups.QualityControl;
        }

        if (joined.IndexOf("family", StringComparison.OrdinalIgnoreCase) >= 0
            || joined.IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0
            || joined.IndexOf("dynamo", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return WorkerSkillGroups.Automation;
        }

        return WorkerSkillGroups.Orchestration;
    }

    private void RefreshRunReport(TaskRun run)
    {
        EnsureTaskRunCollections(run);
        run.RunReport.TaskSummary = string.IsNullOrWhiteSpace(run.IntentSummary)
            ? $"{run.TaskKind}:{run.TaskName}"
            : run.IntentSummary;
        run.RunReport.PlanExecuted = run.Steps
            .Where(x => !string.IsNullOrWhiteSpace(x.Status)
                && !string.Equals(x.Status, "pending", StringComparison.OrdinalIgnoreCase))
            .Select(x => $"{x.StepId}:{x.Status}")
            .ToList();
        run.RunReport.ApprovalCheckpoints = run.Checkpoints
            .Select(x => $"{x.StepId}:{x.Status}:{x.Summary}")
            .Take(12)
            .ToList();
        run.RunReport.ActionsPerformed = run.SelectedActionIds.Count > 0
            ? run.SelectedActionIds.ToList()
            : run.RecommendedActionIds.ToList();
        run.RunReport.ArtifactsGenerated = run.ArtifactKeys.ToList();
        run.RunReport.ResidualRisks = BuildResidualRisks(run);
        run.RunReport.NextRecommendedAction = _recovery.InferNextAction(run);
        run.RunReport.GeneratedUtc = DateTime.UtcNow;
    }

    private static List<string> BuildResidualRisks(TaskRun run)
    {
        var risks = new List<string>();
        if (!string.IsNullOrWhiteSpace(run.LastErrorCode))
        {
            risks.Add($"{run.LastErrorCode}: {run.LastErrorMessage}".Trim());
        }

        if (!string.IsNullOrWhiteSpace(run.ResidualSummary) && !string.Equals(run.ResidualSummary, "0", StringComparison.OrdinalIgnoreCase))
        {
            risks.Add("Residual summary: " + run.ResidualSummary);
        }

        foreach (var diagnostic in run.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Warning || x.Severity == DiagnosticSeverity.Error).Take(5))
        {
            var text = string.IsNullOrWhiteSpace(diagnostic.Message)
                ? diagnostic.Code
                : $"{diagnostic.Code}: {diagnostic.Message}";
            if (!risks.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                risks.Add(text);
            }
        }

        return risks;
    }
}
