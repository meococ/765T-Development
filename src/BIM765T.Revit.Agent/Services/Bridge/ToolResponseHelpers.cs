using System;
using System.Collections.Generic;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.Contracts.Validation;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal static class ToolResponses
{
    internal static ToolResponseEnvelope Success<T>(ToolRequestEnvelope request, T payload, string statusCode = StatusCodes.ReadSucceeded, ReviewReport? reviewSummary = null, IEnumerable<string>? artifacts = null)
    {
        var response = CreateBaseResponse(request);
        response.Succeeded = true;
        response.StatusCode = statusCode;
        response.PayloadJson = JsonUtil.Serialize(payload);
        response.ReviewSummaryJson = reviewSummary != null ? JsonUtil.Serialize(reviewSummary) : string.Empty;
        response.Artifacts = artifacts != null ? new List<string>(artifacts) : new List<string>();
        response.Stage = WorkerStages.Done;
        response.Progress = 100;
        response.HeartbeatUtc = DateTime.UtcNow;
        return response;
    }

    internal static ToolResponseEnvelope ConfirmationRequired(ToolRequestEnvelope request, ExecutionResult result)
    {
        var response = CreateBaseResponse(request);
        response.Succeeded = true;
        response.StatusCode = StatusCodes.ConfirmationRequired;
        response.PayloadJson = JsonUtil.Serialize(result);
        response.ConfirmationRequired = true;
        response.ApprovalToken = result.ApprovalToken;
        response.PreviewRunId = result.PreviewRunId;
        response.ChangedIds = result.ChangedIds;
        response.DiffSummaryJson = JsonUtil.Serialize(result.DiffSummary);
        response.ReviewSummaryJson = result.ReviewSummary != null ? JsonUtil.Serialize(result.ReviewSummary) : string.Empty;
        response.Diagnostics = new List<DiagnosticRecord>(result.Diagnostics);
        response.Artifacts = new List<string>(result.Artifacts);
        response.Stage = WorkerStages.Approval;
        response.Progress = 60;
        response.HeartbeatUtc = DateTime.UtcNow;
        return response;
    }

    internal static ToolResponseEnvelope FromExecutionResult(ToolRequestEnvelope request, ExecutionResult result, string statusCode = StatusCodes.ExecuteSucceeded)
    {
        var response = CreateBaseResponse(request);
        response.Succeeded = true;
        response.StatusCode = statusCode;
        response.PayloadJson = JsonUtil.Serialize(result);
        response.ChangedIds = result.ChangedIds;
        response.PreviewRunId = result.PreviewRunId;
        response.DiffSummaryJson = JsonUtil.Serialize(result.DiffSummary);
        response.ReviewSummaryJson = result.ReviewSummary != null ? JsonUtil.Serialize(result.ReviewSummary) : string.Empty;
        response.Diagnostics = new List<DiagnosticRecord>(result.Diagnostics);
        response.Artifacts = new List<string>(result.Artifacts);
        response.Stage = WorkerStages.Done;
        response.Progress = 100;
        response.HeartbeatUtc = DateTime.UtcNow;
        return response;
    }

    internal static ToolResponseEnvelope Failure(ToolRequestEnvelope request, string statusCode, params DiagnosticRecord[] diagnostics)
    {
        var response = CreateBaseResponse(request);
        response.Succeeded = false;
        response.StatusCode = statusCode;
        response.Diagnostics = new List<DiagnosticRecord>(diagnostics);
        response.Stage = WorkerStages.Recovery;
        response.HeartbeatUtc = DateTime.UtcNow;
        return response;
    }

    internal static ToolResponseEnvelope Failure(ToolRequestEnvelope request, string statusCode, IEnumerable<DiagnosticRecord> diagnostics)
    {
        var response = CreateBaseResponse(request);
        response.Succeeded = false;
        response.StatusCode = statusCode;
        response.Diagnostics = new List<DiagnosticRecord>(diagnostics);
        return response;
    }

    internal static ToolResponseEnvelope Failure(ToolRequestEnvelope request, string statusCode, string message)
    {
        return Failure(request, statusCode, DiagnosticRecord.Create(statusCode, DiagnosticSeverity.Error, message));
    }

    private static ToolResponseEnvelope CreateBaseResponse(ToolRequestEnvelope request)
    {
        return new ToolResponseEnvelope
        {
            RequestId = request.RequestId,
            ToolName = request.ToolName,
            CorrelationId = request.CorrelationId,
            ProtocolVersion = BridgeProtocol.NormalizeOrDefault(request.ProtocolVersion),
            ExecutedAtUtc = DateTime.UtcNow
        };
    }
}

internal static class ToolPayloads
{
    internal static T Read<T>(ToolRequestEnvelope request)
    {
        var payload = JsonUtil.DeserializePayloadOrDefault<T>(request.PayloadJson);
        ToolPayloadValidator.Validate(payload);
        return payload;
    }
}
