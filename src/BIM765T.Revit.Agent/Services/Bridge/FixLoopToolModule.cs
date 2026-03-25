using System;
using Autodesk.Revit.DB;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class FixLoopToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal FixLoopToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var fixLoop = _context.FixLoop;
        var fixLoopReview = ToolManifestPresets.Review("document", "view", "sheet").WithBatchMode("chunked").WithRiskTags("workflow");
        var fixLoopPlan = ToolManifestPresets.WorkflowRead("document");
        var fixLoopRun = ToolManifestPresets.WorkflowRead("document", "workflow_run");
        var fixLoopMutate = ToolManifestPresets.WorkflowMutate("document", "workflow_run");

        registry.Register(
            ToolNames.ReviewFixCandidates,
            "Review supervised fix candidates for parameter hygiene, safe cleanup, or template compliance without mutating the model.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            fixLoopReview,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<FixLoopPlanRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, fixLoop.ReviewCandidates(uiapp, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ScenarioName\":\"parameter_hygiene\",\"PlaybookName\":\"default.fix_loop_v1\",\"ElementIds\":[],\"CategoryNames\":[],\"RequiredParameterNames\":[],\"UseCurrentSelectionWhenEmpty\":true,\"ViewId\":null,\"SheetId\":null,\"MaxIssues\":200,\"MaxActions\":25,\"ImportFilePath\":\"\",\"MatchParameterName\":\"\"}");

        registry.Register(
            ToolNames.WorkflowFixLoopPlan,
            "Plan a supervised closed-loop fix run: detect, classify, propose actions, and capture verification criteria.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            fixLoopPlan,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<FixLoopPlanRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, fixLoop.Plan(uiapp, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ScenarioName\":\"parameter_hygiene\",\"PlaybookName\":\"default.fix_loop_v1\",\"ElementIds\":[],\"CategoryNames\":[],\"RequiredParameterNames\":[],\"UseCurrentSelectionWhenEmpty\":true,\"ViewId\":null,\"SheetId\":null,\"MaxIssues\":200,\"MaxActions\":25,\"ImportFilePath\":\"\",\"MatchParameterName\":\"\"}");

        registry.Register(
            ToolNames.WorkflowFixLoopApply,
            "Apply selected supervised fix-loop actions with dry-run, approval token, and post-apply verification.",
            PermissionLevel.Mutate,
            ApprovalRequirement.HighRiskToken,
            true,
            fixLoopMutate,
            (uiapp, request) =>
            {
                if (!platform.Settings.AllowWriteTools)
                {
                    return ToolResponses.Failure(request, StatusCodes.WriteDisabled);
                }

                var payload = ToolPayloads.Read<FixLoopApplyRequest>(request);
                FixLoopRun run;
                try
                {
                    run = fixLoop.GetRun(payload.RunId);
                }
                catch (InvalidOperationException ex) when (string.Equals(ex.Message, StatusCodes.FixLoopRunNotFound, StringComparison.OrdinalIgnoreCase))
                {
                    return ToolResponses.Failure(request, StatusCodes.FixLoopRunNotFound);
                }

                var doc = platform.ResolveDocument(uiapp, run.DocumentKey);
                if (request.DryRun)
                {
                    if (!platform.MatchesExpectedContext(uiapp, doc, run.ExpectedContextJson))
                    {
                        return ToolResponses.Failure(request, StatusCodes.ContextMismatch);
                    }

                    var preview = fixLoop.PreviewApply(uiapp, doc, payload);
                    return ToolResponses.ConfirmationRequired(request, platform.FinalizePreviewResult(uiapp, doc, request, preview));
                }

                if (!platform.MatchesExpectedContextStrict(uiapp, doc, run.ExpectedContextJson))
                {
                    return ToolResponses.Failure(request, StatusCodes.ContextMismatch, "Fix-loop execute requires the same document/view/selection fingerprint captured during planning.");
                }

                if (!payload.AllowMutations)
                {
                    return ToolResponses.Failure(request, StatusCodes.WorkflowApplyBlocked, "AllowMutations=false so execute is blocked.");
                }

                var approval = platform.ValidateApprovalRequest(uiapp, doc, request);
                if (!string.Equals(approval, StatusCodes.Ok, StringComparison.OrdinalIgnoreCase))
                {
                    return ToolResponses.Failure(request, approval);
                }

                var result = fixLoop.Apply(uiapp, doc, payload);
                return new ToolResponseEnvelope
                {
                    RequestId = request.RequestId,
                    ToolName = request.ToolName,
                    CorrelationId = request.CorrelationId,
                    Succeeded = true,
                    StatusCode = string.Equals(result.Verification.Status, "pass", StringComparison.OrdinalIgnoreCase) ? StatusCodes.ExecuteSucceeded : StatusCodes.FixLoopVerificationFailed,
                    PayloadJson = JsonUtil.Serialize(result),
                    ChangedIds = result.ChangedIds,
                    Diagnostics = result.Diagnostics,
                    Artifacts = result.Evidence.ArtifactKeys,
                    ExecutedAtUtc = DateTime.UtcNow
                };
            },
            "{\"RunId\":\"\",\"ApprovalToken\":\"\",\"ActionIds\":[],\"AllowMutations\":true}");

        registry.Register(
            ToolNames.WorkflowFixLoopVerify,
            "Re-run verification for an existing fix-loop run and report residual issues and blocked reasons.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            fixLoopRun,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<FixLoopVerifyRequest>(request);
                FixLoopRun run;
                try
                {
                    run = fixLoop.GetRun(payload.RunId);
                }
                catch (InvalidOperationException ex) when (string.Equals(ex.Message, StatusCodes.FixLoopRunNotFound, StringComparison.OrdinalIgnoreCase))
                {
                    return ToolResponses.Failure(request, StatusCodes.FixLoopRunNotFound);
                }

                var doc = platform.ResolveDocument(uiapp, run.DocumentKey);
                var verified = fixLoop.Verify(uiapp, doc, payload);
                return ToolResponses.Success(
                    request,
                    verified,
                    string.Equals(verified.Verification.Status, "pass", StringComparison.OrdinalIgnoreCase) ? StatusCodes.ReadSucceeded : StatusCodes.FixLoopVerificationFailed);
            },
            "{\"RunId\":\"\",\"MaxResidualIssues\":200}");
    }
}
