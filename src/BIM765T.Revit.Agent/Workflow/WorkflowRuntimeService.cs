using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Agent.Services.Review;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Workflow;

internal sealed class WorkflowRuntimeService
{
    private readonly PlatformServices _platform;
    private readonly ReviewRuleEngineService _reviewRuleEngine;
    private readonly FamilyAxisAuditService _familyAxisAudit;
    private readonly PenetrationShadowService _penetrationShadow;
    private readonly MutationService _mutation;
    private readonly DataExportService _dataExport;
    private readonly SheetViewManagementService _sheetView;
    private readonly ConcurrentDictionary<string, WorkflowRun> _runs = new ConcurrentDictionary<string, WorkflowRun>(StringComparer.OrdinalIgnoreCase);
    private readonly List<WorkflowDefinition> _definitions = BuiltInWorkflows.Create();

    /// <summary>
    /// MEM-FIX: Giới hạn tối đa số workflow runs giữ trong memory.
    /// Khi vượt quá, evict các run cũ nhất đã completed.
    /// </summary>
    private const int MaxRetainedRuns = BridgeConstants.DefaultMaxRetainedWorkflowRuns;

    internal WorkflowRuntimeService(
        PlatformServices platform,
        ReviewRuleEngineService reviewRuleEngine,
        FamilyAxisAuditService familyAxisAudit,
        PenetrationShadowService penetrationShadow,
        MutationService mutation,
        DataExportService dataExport,
        SheetViewManagementService sheetView)
    {
        _platform = platform;
        _reviewRuleEngine = reviewRuleEngine;
        _familyAxisAudit = familyAxisAudit;
        _penetrationShadow = penetrationShadow;
        _mutation = mutation;
        _dataExport = dataExport;
        _sheetView = sheetView;
    }

    internal List<WorkflowDefinition> ListDefinitions()
    {
        return _definitions
            .OrderBy(x => x.WorkflowName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal WorkflowRun GetRun(string runId)
    {
        if (!_runs.TryGetValue(runId, out var run))
        {
            throw new InvalidOperationException(StatusCodes.WorkflowRunNotFound);
        }

        return run;
    }

    internal List<WorkflowRun> ListRuns()
    {
        return _runs.Values
            .OrderByDescending(x => x.PlannedUtc)
            .ToList();
    }

    internal WorkflowRun Plan(UIApplication uiapp, Document doc, WorkflowPlanRequest request, string caller, string sessionId)
    {
        var definition = ResolveDefinition(request.WorkflowName);
        var stopwatch = Stopwatch.StartNew();
        var run = new WorkflowRun
        {
            RunId = Guid.NewGuid().ToString("N"),
            WorkflowName = definition.WorkflowName,
            Status = "planned",
            DocumentKey = _platform.GetDocumentKey(doc),
            Fingerprint = _platform.BuildContextFingerprint(uiapp, doc),
            InputJson = string.IsNullOrWhiteSpace(request.InputJson) ? "{}" : request.InputJson,
            RequiresApproval = definition.RequiresApproval,
            ExpectedContextJson = JsonUtil.Serialize(_platform.BuildContextFingerprint(uiapp, doc)),
            Caller = caller,
            SessionId = sessionId,
            Evidence = new WorkflowEvidenceBundle
            {
                RunId = string.Empty
            }
        };
        run.Evidence.RunId = run.RunId;
        run.Evidence.PlanSummary = definition.Description;
        foreach (var step in definition.Steps)
        {
            run.Checkpoints.Add(new WorkflowCheckpoint
            {
                StepName = step.StepName,
                Status = "pending",
                Message = step.Description
            });
        }

        switch (definition.WorkflowName)
        {
            case "workflow.model_health":
                PlanModelHealth(uiapp, doc, run);
                break;
            case "workflow.sheet_qc":
                PlanSheetQc(uiapp, doc, run);
                break;
            case "workflow.parameter_rollout":
                PlanParameterRollout(uiapp, doc, run);
                break;
            case "workflow.family_axis_audit":
                PlanFamilyAxis(uiapp, doc, run);
                break;
            case "workflow.penetration_round_shadow":
                PlanPenetration(uiapp, doc, run);
                break;
            default:
                throw new InvalidOperationException(StatusCodes.WorkflowNotFound);
        }

        stopwatch.Stop();
        run.DurationMs = stopwatch.ElapsedMilliseconds;
        _runs[run.RunId] = run;
        EvictCompletedRunsIfNeeded();
        return run;
    }

    internal WorkflowRun Apply(UIApplication uiapp, WorkflowApplyRequest request)
    {
        var run = GetRun(request.RunId);
        if (string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(StatusCodes.WorkflowAlreadyCompleted);
        }

        var doc = _platform.ResolveDocument(uiapp, run.DocumentKey);
        if (!_platform.MatchesExpectedContextStrict(uiapp, doc, run.ExpectedContextJson))
        {
            throw new InvalidOperationException(StatusCodes.ContextMismatch);
        }

        var stopwatch = Stopwatch.StartNew();
        switch (run.WorkflowName)
        {
            case "workflow.model_health":
            case "workflow.sheet_qc":
            case "workflow.family_axis_audit":
                run.Status = "completed";
                run.AppliedUtc = DateTime.UtcNow;
                MarkRemainingCheckpointsApplied(run, "read-only workflow completed");
                break;

            case "workflow.parameter_rollout":
                ApplyParameterRollout(uiapp, doc, run, request);
                break;

            case "workflow.penetration_round_shadow":
                ApplyPenetration(uiapp, doc, run, request);
                break;

            default:
                throw new InvalidOperationException(StatusCodes.WorkflowNotFound);
        }

        stopwatch.Stop();
        run.DurationMs += stopwatch.ElapsedMilliseconds;
        _runs[run.RunId] = run;
        return run;
    }

    internal WorkflowRun Resume(UIApplication uiapp, WorkflowApplyRequest request)
    {
        var run = GetRun(request.RunId);
        if (string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(StatusCodes.WorkflowAlreadyCompleted);
        }

        return Apply(uiapp, request);
    }

    private WorkflowDefinition ResolveDefinition(string workflowName)
    {
        return _definitions.FirstOrDefault(x => string.Equals(x.WorkflowName, workflowName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(StatusCodes.WorkflowNotFound);
    }

    private void PlanModelHealth(UIApplication uiapp, Document doc, WorkflowRun run)
    {
        var taskContext = _platform.GetTaskContext(uiapp, doc, new TaskContextRequest(), null);
        var health = _platform.ReviewModelHealth(uiapp, doc);
        var warnings = _platform.ReviewWarnings(doc);
        var snapshot = _platform.CaptureSnapshot(uiapp, doc, new CaptureSnapshotRequest { Scope = "active_view" });

        AddEvidence(run, "task_context", taskContext);
        AddEvidence(run, "model_health", health, health.Review);
        AddEvidence(run, "warnings", warnings, warnings);
        AddEvidence(run, "snapshot", snapshot, snapshot.Review);
        CompleteCheckpoint(run, "task_context", "context captured");
        CompleteCheckpoint(run, "model_health", $"{health.TotalWarnings} warnings / {health.TotalLinks} links");
        CompleteCheckpoint(run, "warnings", $"{warnings.IssueCount} warnings");
        CompleteCheckpoint(run, "snapshot", $"{snapshot.ElementCount} snapshot items");
    }

    private void PlanSheetQc(UIApplication uiapp, Document doc, WorkflowRun run)
    {
        var payload = ParseJson<SheetSummaryRequest>(run.InputJson);
        if (!payload.SheetId.HasValue && string.IsNullOrWhiteSpace(payload.SheetNumber) && doc.ActiveView is ViewSheet activeSheet)
        {
            payload.SheetId = checked((int)activeSheet.Id.Value);
            payload.SheetNumber = activeSheet.SheetNumber ?? string.Empty;
            payload.SheetName = activeSheet.Name ?? string.Empty;
        }
        var summary = _platform.ReviewSheetSummary(uiapp, doc, payload);
        var layout = _sheetView.GetViewportLayout(_platform, doc, new ViewportLayoutRequest
        {
            SheetId = payload.SheetId ?? 0,
            SheetNumber = payload.SheetNumber ?? string.Empty
        });
        var snapshot = _platform.CaptureSnapshot(uiapp, doc, new CaptureSnapshotRequest
        {
            Scope = "sheet",
            SheetId = payload.SheetId,
            SheetNumber = payload.SheetNumber ?? string.Empty,
            SheetName = payload.SheetName ?? string.Empty
        });

        AddEvidence(run, "sheet_summary", summary, summary.Review);
        AddEvidence(run, "viewport_layout", layout);
        AddEvidence(run, "snapshot", snapshot, snapshot.Review);
        CompleteCheckpoint(run, "sheet_summary", summary.SheetNumber);
        CompleteCheckpoint(run, "viewport_layout", $"{layout.Viewports.Count} viewports");
        CompleteCheckpoint(run, "snapshot", $"{snapshot.ElementCount} snapshot items");
    }

    private void PlanFamilyAxis(UIApplication uiapp, Document doc, WorkflowRun run)
    {
        var payload = ParseJson<FamilyAxisAlignmentRequest>(run.InputJson);
        var audit = _familyAxisAudit.ReviewFamilyAxisAlignment(uiapp, _platform, doc, payload, payload.ViewName);
        var snapshot = _platform.CaptureSnapshot(uiapp, doc, new CaptureSnapshotRequest
        {
            Scope = "active_view",
            ViewId = payload.ViewId,
            MaxElements = Math.Max(50, payload.MaxIssues)
        });

        AddEvidence(run, "axis_audit", audit, audit.Review);
        AddEvidence(run, "snapshot", snapshot, snapshot.Review);
        CompleteCheckpoint(run, "axis_audit", $"{audit.MismatchCount} mismatches");
        CompleteCheckpoint(run, "snapshot", $"{snapshot.ElementCount} snapshot items");
    }

    private void PlanParameterRollout(UIApplication uiapp, Document doc, WorkflowRun run)
    {
        if (TryParseDataImport(run.InputJson, out var importPayload))
        {
            var importDryRunEnvelope = BuildWorkflowEnvelope(run, ToolNames.DataImportSafe, JsonUtil.Serialize(importPayload));
            var preview = _dataExport.PreviewImport(_platform, doc, new DataImportPreviewRequest
            {
                DocumentKey = importPayload.DocumentKey,
                InputPath = importPayload.InputPath,
                Format = importPayload.Format,
                MatchParameterName = importPayload.MatchParameterName,
                MaxPreviewRows = 20
            });
            AddEvidence(run, "preview", preview);
            CompleteCheckpoint(run, "preview", $"{preview.MatchedElements} matched elements");

            var importDryRun = _mutation.PreviewDataImport(_platform, doc, importPayload, importDryRunEnvelope);
            importDryRun = _platform.FinalizePreviewResult(uiapp, doc, importDryRunEnvelope, importDryRun);
            CaptureMutationPreview(run, ToolNames.DataImportSafe, importPayload, importDryRun);
            CompleteCheckpoint(run, "dry_run", "preview artifact + approval ready");
            run.RequiresApproval = true;
            run.Status = "awaiting_approval";
            return;
        }

        var fillPayload = ParseJson<BatchFillParameterRequest>(run.InputJson);
        var fillDryRunEnvelope = BuildWorkflowEnvelope(run, ToolNames.ParameterBatchFillSafe, JsonUtil.Serialize(fillPayload));
        var completeness = _platform.ReviewParameterCompleteness(doc, new ReviewParameterCompletenessRequest
        {
            ElementIds = fillPayload.ElementIds,
            RequiredParameterNames = new List<string> { fillPayload.ParameterName }
        });
        AddEvidence(run, "completeness", completeness, completeness);
        CompleteCheckpoint(run, "completeness", $"{completeness.IssueCount} completeness issues");

        var fillDryRun = _mutation.PreviewBatchFillParameter(_platform, doc, fillPayload, fillDryRunEnvelope);
        fillDryRun = _platform.FinalizePreviewResult(uiapp, doc, fillDryRunEnvelope, fillDryRun);
        CaptureMutationPreview(run, ToolNames.ParameterBatchFillSafe, fillPayload, fillDryRun);
        CompleteCheckpoint(run, "dry_run", "preview artifact + approval ready");
        run.RequiresApproval = true;
        run.Status = "awaiting_approval";
    }

    private void PlanPenetration(UIApplication uiapp, Document doc, WorkflowRun run)
    {
        var payload = ParseJson<CreateRoundShadowBatchRequest>(run.InputJson);
        var inventory = _penetrationShadow.ReportInventory(uiapp, _platform, doc, new PenetrationInventoryRequest
        {
            DocumentKey = payload.DocumentKey,
            FamilyName = string.IsNullOrWhiteSpace(payload.SourceFamilyName) ? "Penetration Alpha" : payload.SourceFamilyName,
            MaxResults = payload.MaxResults
        });
        var plan = _penetrationShadow.PlanRoundShadow(uiapp, _platform, doc, new PenetrationRoundShadowPlanRequest
        {
            DocumentKey = payload.DocumentKey,
            SourceFamilyName = payload.SourceFamilyName,
            RoundFamilyName = payload.RoundFamilyName,
            PreferredReferenceMark = payload.PreferredReferenceMark,
            MaxResults = payload.MaxResults
        });

        AddEvidence(run, "inventory", inventory, inventory.Review);
        AddEvidence(run, "plan", plan, plan.Review);
        CompleteCheckpoint(run, "inventory", $"{inventory.Count} source items");
        CompleteCheckpoint(run, "plan", $"{plan.Count} planned round shadows");

        var dryRunEnvelope = BuildWorkflowEnvelope(run, ToolNames.BatchCreateRoundShadowSafe, JsonUtil.Serialize(payload));
        var dryRun = _penetrationShadow.PreviewCreateRoundShadowBatch(_platform, doc, payload, dryRunEnvelope);
        dryRun = _platform.FinalizePreviewResult(uiapp, doc, dryRunEnvelope, dryRun);
        CaptureMutationPreview(run, ToolNames.BatchCreateRoundShadowSafe, payload, dryRun);
        CompleteCheckpoint(run, "dry_run", "preview artifact + approval ready");
        run.RequiresApproval = true;
        run.Status = "awaiting_approval";
    }

    private void ApplyParameterRollout(UIApplication uiapp, Document doc, WorkflowRun run, WorkflowApplyRequest request)
    {
        EnsureWorkflowApproval(uiapp, doc, run, request);

        ExecutionResult result;
        if (string.Equals(run.MutationToolName, ToolNames.DataImportSafe, StringComparison.OrdinalIgnoreCase))
        {
            result = _mutation.ExecuteDataImport(_platform, doc, ParseJson<DataImportRequest>(run.MutationPayloadJson));
        }
        else
        {
            result = _mutation.ExecuteBatchFillParameter(_platform, doc, ParseJson<BatchFillParameterRequest>(run.MutationPayloadJson));
        }

        CaptureExecution(run, "apply", result);
    }

    private void ApplyPenetration(UIApplication uiapp, Document doc, WorkflowRun run, WorkflowApplyRequest request)
    {
        EnsureWorkflowApproval(uiapp, doc, run, request);
        var result = _penetrationShadow.ExecuteCreateRoundShadowBatch(_platform, doc, ParseJson<CreateRoundShadowBatchRequest>(run.MutationPayloadJson));
        CaptureExecution(run, "apply", result);
    }

    private void EnsureWorkflowApproval(UIApplication uiapp, Document doc, WorkflowRun run, WorkflowApplyRequest request)
    {
        if (!run.RequiresApproval)
        {
            return;
        }

        if (!request.AllowMutations)
        {
            throw new InvalidOperationException(StatusCodes.WorkflowApplyBlocked);
        }

        var approvalEnvelope = BuildWorkflowEnvelope(run, run.MutationToolName, run.MutationPayloadJson, request.ApprovalToken);
        var status = _platform.ValidateApprovalRequest(uiapp, doc, approvalEnvelope);
        if (!string.Equals(status, StatusCodes.Ok, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(status);
        }
    }

    private static T ParseJson<T>(string raw) where T : new()
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new T();
        }

        return JsonUtil.DeserializePayloadOrDefault<T>(raw);
    }

    private static bool TryParseDataImport(string raw, out DataImportRequest request)
    {
        request = ParseJson<DataImportRequest>(raw);
        return !string.IsNullOrWhiteSpace(request.InputPath);
    }

    private ToolRequestEnvelope BuildWorkflowEnvelope(WorkflowRun run, string toolName, string payloadJson, string approvalToken = "")
    {
        return new ToolRequestEnvelope
        {
            RequestId = Guid.NewGuid().ToString("N"),
            ToolName = toolName,
            PayloadJson = payloadJson,
            Caller = run.Caller,
            SessionId = run.SessionId,
            DryRun = string.IsNullOrWhiteSpace(approvalToken),
            ApprovalToken = approvalToken,
            ExpectedContextJson = run.ExpectedContextJson,
            PreviewRunId = run.PreviewRunId,
            TargetDocument = run.DocumentKey
        };
    }

    private static void CompleteCheckpoint(WorkflowRun run, string stepName, string message)
    {
        var checkpoint = run.Checkpoints.FirstOrDefault(x => string.Equals(x.StepName, stepName, StringComparison.OrdinalIgnoreCase));
        if (checkpoint == null)
        {
            return;
        }

        checkpoint.Status = "completed";
        checkpoint.Message = message;
        checkpoint.TimestampUtc = DateTime.UtcNow;
    }

    private static void MarkRemainingCheckpointsApplied(WorkflowRun run, string message)
    {
        foreach (var checkpoint in run.Checkpoints.Where(x => !string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase)))
        {
            checkpoint.Status = "completed";
            checkpoint.Message = message;
            checkpoint.TimestampUtc = DateTime.UtcNow;
        }
    }

    private void CaptureMutationPreview<TPayload>(WorkflowRun run, string toolName, TPayload payload, ExecutionResult dryRun)
    {
        run.MutationToolName = toolName;
        run.MutationPayloadJson = JsonUtil.Serialize(payload);
        run.ApprovalToken = dryRun.ApprovalToken;
        run.PreviewRunId = dryRun.PreviewRunId;
        run.ExpectedContextJson = JsonUtil.Serialize(dryRun.ResolvedContext ?? run.Fingerprint);
        run.Diagnostics = dryRun.Diagnostics.ToList();
        AddEvidence(run, "dry_run", dryRun, dryRun.ReviewSummary);
    }

    private void CaptureExecution(WorkflowRun run, string stepName, ExecutionResult result)
    {
        run.ChangedIds = result.ChangedIds.ToList();
        run.Diagnostics = result.Diagnostics.ToList();
        run.AppliedUtc = DateTime.UtcNow;
        run.Status = "completed";
        AddEvidence(run, stepName, result, result.ReviewSummary);

        var checkpoint = run.Checkpoints.FirstOrDefault(x => string.Equals(x.StepName, stepName, StringComparison.OrdinalIgnoreCase));
        if (checkpoint != null)
        {
            checkpoint.Status = "completed";
            checkpoint.Message = $"{result.ChangedIds.Count} changed";
            checkpoint.TimestampUtc = DateTime.UtcNow;
            checkpoint.ChangedIds = result.ChangedIds.ToList();
            checkpoint.ArtifactKeys = result.Artifacts.ToList();
        }
    }

    private static void AddEvidence(WorkflowRun run, string label, object payload, ReviewReport? review = null)
    {
        var artifactKey = $"{label}:{run.Evidence.ArtifactKeys.Count + 1}";
        run.Evidence.ArtifactKeys.Add(artifactKey);
        var serialized = JsonUtil.Serialize(payload);
        run.Evidence.ResultPayloads.Add(serialized);
        if (label.IndexOf("snapshot", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            run.Evidence.SnapshotPayloads.Add(serialized);
        }
        if (review != null)
        {
            run.Evidence.ReviewPayloads.Add(JsonUtil.Serialize(review));
        }
    }

    /// <summary>
    /// MEM-FIX: Evict completed runs cũ nhất khi dictionary vượt quá MaxRetainedRuns.
    /// Giữ lại các run chưa completed (awaiting_approval, planned) để không mất workflow đang chạy.
    /// </summary>
    private void EvictCompletedRunsIfNeeded()
    {
        if (_runs.Count <= MaxRetainedRuns)
        {
            return;
        }

        var completedRuns = _runs.Values
            .Where(r => string.Equals(r.Status, "completed", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.PlannedUtc)
            .ToList();

        var toRemove = _runs.Count - MaxRetainedRuns;
        foreach (var run in completedRuns.Take(toRemove))
        {
            _runs.TryRemove(run.RunId, out _);
        }
    }
}
