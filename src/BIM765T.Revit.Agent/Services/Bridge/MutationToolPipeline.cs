using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Core.Execution;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class MutationToolPipeline
{
    private readonly PlatformServices _platform;

    internal MutationToolPipeline(PlatformServices platform)
    {
        _platform = platform;
    }

    internal Func<UIApplication, ToolRequestEnvelope, ToolResponseEnvelope> BuildHandler<TPayload>(
        Func<bool> isEnabled,
        string disabledStatusCode,
        Func<UIApplication, ToolRequestEnvelope, TPayload, Document> resolveDocument,
        Action<Document, TPayload>? normalizePayload,
        Func<UIApplication, PlatformServices, Document, TPayload, ToolRequestEnvelope, ExecutionResult> preview,
        Func<UIApplication, PlatformServices, Document, TPayload, ExecutionResult> execute)
    {
        return (uiapp, request) =>
        {
            if (!isEnabled())
            {
                return ToolResponses.Failure(request, disabledStatusCode);
            }

            var payload = ToolPayloads.Read<TPayload>(request);
            var doc = resolveDocument(uiapp, request, payload);
            normalizePayload?.Invoke(doc, payload);

            if (request.DryRun)
            {
                if (!_platform.MatchesExpectedContext(uiapp, doc, request.ExpectedContextJson))
                {
                    return ToolResponses.Failure(request, StatusCodes.ContextMismatch);
                }

                var previewResult = preview(uiapp, _platform, doc, payload, request);
                return ToolResponses.ConfirmationRequired(request, _platform.FinalizePreviewResult(uiapp, doc, request, previewResult));
            }

            if (!_platform.MatchesExpectedContextStrict(uiapp, doc, request.ExpectedContextJson))
            {
                return ToolResponses.Failure(
                    request,
                    StatusCodes.ContextMismatch,
                    "Mutation tools require expected_context during execute. Run dry-run first to capture a fingerprint.");
            }

            var approval = _platform.ValidateApprovalRequest(uiapp, doc, request);
            if (!string.Equals(approval, StatusCodes.Ok, StringComparison.OrdinalIgnoreCase))
            {
                return ToolResponses.Failure(request, approval);
            }

            return ToolResponses.FromExecutionResult(request, execute(uiapp, _platform, doc, payload));
        };
    }
}
