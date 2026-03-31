using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Contracts.Validation;

public static partial class ToolPayloadValidator
{
    private static void ValidateWorkerMessage(WorkerMessageRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
        {
            diagnostics.Add(DiagnosticRecord.Create("WORKER_MESSAGE_REQUIRED", DiagnosticSeverity.Error, "Message must not be empty."));
        }

        if (!string.IsNullOrWhiteSpace(request.ClientSurface)
            && !IsAllowedValue(request.ClientSurface, WorkerClientSurfaces.Ui, WorkerClientSurfaces.Mcp))
        {
            diagnostics.Add(DiagnosticRecord.Create("WORKER_CLIENT_SURFACE_INVALID", DiagnosticSeverity.Error, "ClientSurface must be 'ui' or 'mcp'."));
        }
    }

    private static void ValidateWorkerSession(WorkerSessionRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
        {
            diagnostics.Add(DiagnosticRecord.Create("WORKER_SESSION_REQUIRED", DiagnosticSeverity.Error, "SessionId must not be empty."));
        }
    }

    private static void ValidateWorkerListSessions(WorkerListSessionsRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxResults <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("WORKER_MAX_RESULTS_INVALID", DiagnosticSeverity.Error, "MaxResults must be greater than 0."));
        }
    }

    private static void ValidateWorkerSetPersona(WorkerSetPersonaRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        ValidateWorkerSession(new WorkerSessionRequest { SessionId = request.SessionId }, diagnostics);
        if (string.IsNullOrWhiteSpace(request.PersonaId))
        {
            diagnostics.Add(DiagnosticRecord.Create("WORKER_PERSONA_REQUIRED", DiagnosticSeverity.Error, "PersonaId must not be empty."));
        }
    }

    private static void ValidateWorkerContext(WorkerContextRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.MaxRecentOperations <= 0 || request.MaxRecentEvents <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("WORKER_CONTEXT_LIMIT_INVALID", DiagnosticSeverity.Error, "MaxRecentOperations/MaxRecentEvents must be greater than 0."));
        }
    }

    private static void ValidateFixLoopPlan(FixLoopPlanRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.ScenarioName))
        {
            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_SCENARIO_REQUIRED", DiagnosticSeverity.Error, "ScenarioName must not be empty."));
        }

        if (request.MaxIssues <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_MAX_ISSUES_INVALID", DiagnosticSeverity.Error, "MaxIssues must be greater than 0."));
        }

        if (request.MaxActions <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_MAX_ACTIONS_INVALID", DiagnosticSeverity.Error, "MaxActions must be greater than 0."));
        }

        if (request.ElementIds != null && request.ElementIds.Any(x => x <= 0))
        {
            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_ELEMENT_ID_INVALID", DiagnosticSeverity.Error, "ElementIds must only contain values greater than 0."));
        }
    }

    private static void ValidateFixLoopApply(FixLoopApplyRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_RUN_REQUIRED", DiagnosticSeverity.Error, "RunId must not be empty."));
        }
    }

    private static void ValidateFixLoopVerify(FixLoopVerifyRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_RUN_REQUIRED", DiagnosticSeverity.Error, "RunId must not be empty."));
        }

        if (request.MaxResidualIssues <= 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_MAX_RESIDUAL_INVALID", DiagnosticSeverity.Error, "MaxResidualIssues must be greater than 0."));
        }
    }

    private static void ValidateExternalTaskIntake(ExternalTaskIntakeRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentKey))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_DOCUMENT_KEY_REQUIRED", DiagnosticSeverity.Error, "DocumentKey must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.TaskKind))
        {
            diagnostics.Add(DiagnosticRecord.Create("TASK_KIND_REQUIRED", DiagnosticSeverity.Error, "TaskKind must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.TaskName) && string.IsNullOrWhiteSpace(request.Envelope?.Title))
        {
            diagnostics.Add(DiagnosticRecord.Create("EXTERNAL_TASK_NAME_REQUIRED", DiagnosticSeverity.Error, "TaskName or Envelope.Title must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.Envelope?.ExternalSystem))
        {
            diagnostics.Add(DiagnosticRecord.Create("EXTERNAL_SYSTEM_REQUIRED", DiagnosticSeverity.Error, "Envelope.ExternalSystem must not be empty."));
        }

        if (string.IsNullOrWhiteSpace(request.Envelope?.ExternalTaskRef))
        {
            diagnostics.Add(DiagnosticRecord.Create("EXTERNAL_TASK_REF_REQUIRED", DiagnosticSeverity.Error, "Envelope.ExternalTaskRef must not be empty."));
        }
    }
}
