using System;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class WorkflowInspectorToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal WorkflowInspectorToolModule(ToolModuleContext context)
    {
        _context = context;
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var workflow = _context.WorkflowRuntime;
        var elementRead = ToolManifestPresets.Read("document");
        var viewRead = ToolManifestPresets.Read("document", "view").WithTouchesActiveView();
        var sheetRead = ToolManifestPresets.Read("document", "sheet");
        var workflowCatalogRead = ToolManifestPresets.Read().WithRiskTags("workflow");
        var workflowPlanRead = ToolManifestPresets.WorkflowRead("document");
        var workflowRunRead = ToolManifestPresets.WorkflowRead("workflow_run");
        var workflowMutate = ToolManifestPresets.WorkflowMutate("document", "workflow_run");

        registry.Register(ToolNames.ElementExplain, "Explain why an element was selected: host, owner view, dependents, key parameters.", PermissionLevel.Read, ApprovalRequirement.None, false, elementRead,
            (uiapp, request) =>
            {
                try
                {
                    var payload = ToolPayloads.Read<ElementExplainRequest>(request);
                    var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                    return ToolResponses.Success(request, platform.ExplainElement(uiapp, doc, payload));
                }
                catch (InvalidOperationException ex)
                {
                    return ToolResponses.Failure(request, StatusCodes.TargetElementsNotFound, ex.Message);
                }
            },
            "{\"DocumentKey\":\"\",\"ElementId\":0,\"IncludeParameters\":true,\"ParameterNames\":[],\"IncludeDependents\":true,\"IncludeHostRelations\":true}");

        registry.Register(ToolNames.ElementGraph, "Build a lightweight dependency graph for elements (type/host/owner-view/dependents).", PermissionLevel.Read, ApprovalRequirement.None, false, elementRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ElementGraphRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, platform.BuildElementGraph(doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ElementIds\":[],\"MaxDepth\":1,\"IncludeDependents\":true,\"IncludeHost\":true,\"IncludeType\":true,\"IncludeOwnerView\":true}");

        registry.Register(ToolNames.ParameterTrace, "Trace a parameter value across elements/categories to explain where values are missing or inconsistent.", PermissionLevel.Read, ApprovalRequirement.None, false, elementRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ParameterTraceRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, platform.TraceParameter(uiapp, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ParameterName\":\"Comments\",\"ElementIds\":[],\"CategoryNames\":[],\"MaxResults\":200,\"IncludeEmptyValues\":false}");

        registry.Register(ToolNames.ViewUsage, "Explain how a view is used: visible count, filters, sample elements, sheets.", PermissionLevel.Read, ApprovalRequirement.None, false, viewRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ViewUsageRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, platform.DescribeViewUsage(uiapp, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ViewId\":null,\"ViewName\":\"\",\"IncludeSheets\":true,\"IncludeFilters\":true,\"MaxSamples\":20}");

        registry.Register(ToolNames.SheetDependencies, "List what a sheet depends on: title blocks, viewports, schedules.", PermissionLevel.Read, ApprovalRequirement.None, false, sheetRead,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<SheetDependenciesRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, platform.DescribeSheetDependencies(doc, payload));
            },
            "{\"DocumentKey\":\"\",\"SheetId\":null,\"SheetNumber\":\"\",\"IncludeSchedules\":true,\"IncludeViewports\":true}");

        registry.Register(ToolNames.WorkflowList, "List built-in BIM workflows with plan/apply metadata.", PermissionLevel.Read, ApprovalRequirement.None, false, workflowCatalogRead,
            (uiapp, request) => ToolResponses.Success(request, new WorkflowListResponse { Workflows = workflow.ListDefinitions() }));

        registry.Register(ToolNames.WorkflowPlan, "Create a fixed workflow plan artifact and preview evidence for a BIM workflow.", PermissionLevel.Review, ApprovalRequirement.None, false, workflowPlanRead,
            (uiapp, request) =>
            {
                try
                {
                    var payload = ToolPayloads.Read<WorkflowPlanRequest>(request);
                    var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                    return ToolResponses.Success(request, workflow.Plan(uiapp, doc, payload, request.Caller, request.SessionId));
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromWorkflowException(request, ex);
                }
            },
            "{\"WorkflowName\":\"workflow.model_health\",\"DocumentKey\":\"\",\"InputJson\":\"{}\"}");

        registry.Register(ToolNames.WorkflowGetRun, "Get a workflow run artifact/evidence by run id.", PermissionLevel.Read, ApprovalRequirement.None, false, workflowRunRead,
            (uiapp, request) =>
            {
                try
                {
                    var payload = ToolPayloads.Read<WorkflowGetRunRequest>(request);
                    return ToolResponses.Success(request, workflow.GetRun(payload.RunId));
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromWorkflowException(request, ex);
                }
            },
            "{\"RunId\":\"\"}");

        registry.Register(ToolNames.WorkflowApply, "Apply a previously planned workflow run. Mutation workflows require approval token + preview_run_id.", PermissionLevel.Mutate, ApprovalRequirement.ConfirmToken, false, workflowMutate,
            (uiapp, request) =>
            {
                try
                {
                    var payload = ToolPayloads.Read<WorkflowApplyRequest>(request);
                    if (string.IsNullOrWhiteSpace(payload.ApprovalToken))
                    {
                        payload.ApprovalToken = request.ApprovalToken;
                    }
                    return ToolResponses.Success(request, workflow.Apply(uiapp, payload));
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromWorkflowException(request, ex);
                }
            },
            "{\"RunId\":\"\",\"ApprovalToken\":\"\",\"AllowMutations\":false}");

        registry.Register(ToolNames.WorkflowResume, "Resume or retry a workflow run from the last checkpoint.", PermissionLevel.Mutate, ApprovalRequirement.ConfirmToken, false, workflowMutate,
            (uiapp, request) =>
            {
                try
                {
                    var payload = ToolPayloads.Read<WorkflowApplyRequest>(request);
                    if (string.IsNullOrWhiteSpace(payload.ApprovalToken))
                    {
                        payload.ApprovalToken = request.ApprovalToken;
                    }
                    return ToolResponses.Success(request, workflow.Resume(uiapp, payload));
                }
                catch (InvalidOperationException ex)
                {
                    return FailureFromWorkflowException(request, ex);
                }
            },
            "{\"RunId\":\"\",\"ApprovalToken\":\"\",\"AllowMutations\":false}");
    }

    private static ToolResponseEnvelope FailureFromWorkflowException(ToolRequestEnvelope request, InvalidOperationException ex)
    {
        var statusCode = ex.Message switch
        {
            StatusCodes.WorkflowNotFound => StatusCodes.WorkflowNotFound,
            StatusCodes.WorkflowRunNotFound => StatusCodes.WorkflowRunNotFound,
            StatusCodes.WorkflowApplyBlocked => StatusCodes.WorkflowApplyBlocked,
            StatusCodes.WorkflowAlreadyCompleted => StatusCodes.WorkflowAlreadyCompleted,
            StatusCodes.ContextMismatch => StatusCodes.ContextMismatch,
            StatusCodes.ApprovalMismatch => StatusCodes.ApprovalMismatch,
            StatusCodes.ApprovalInvalid => StatusCodes.ApprovalInvalid,
            StatusCodes.ApprovalExpired => StatusCodes.ApprovalExpired,
            StatusCodes.PreviewRunRequired => StatusCodes.PreviewRunRequired,
            _ => StatusCodes.InternalError
        };

        return ToolResponses.Failure(request, statusCode,
            DiagnosticRecord.Create(statusCode, DiagnosticSeverity.Error,
                statusCode == StatusCodes.InternalError ? ex.Message : $"Workflow failed: {statusCode}"));
    }
}
