using System;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Core.Execution;
using BIM765T.Revit.Agent.Infrastructure.Failures;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using BIM765T.Revit.Agent.Infrastructure.Time;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class ToolExecutor
{
    private readonly IAgentLogger _logger;
    private readonly PlatformServices _platform;
    private readonly ToolRegistry _registry;
    private readonly ToolExecutionCore _core;
    private readonly AutomationDialogGuard? _dialogGuard;

    internal ToolExecutor(IAgentLogger logger, PlatformServices platform, ToolRegistry registry, AutomationDialogGuard? dialogGuard, ISystemClock clock)
    {
        _logger = logger;
        _platform = platform;
        _registry = registry;
        _dialogGuard = dialogGuard;
        _core = new ToolExecutionCore(() => clock.UtcNow, (message, ex) => _logger.Error(message, ex));
    }

    internal ToolResponseEnvelope Execute(UIApplication uiapp, ToolRequestEnvelope request)
    {
        using var logScope = _logger.BeginScope(request.CorrelationId, request.ToolName, "executor");

        _registry.TryGet(request.ToolName, out var registration);
        var manifest = registration != null
            ? new ToolExecutionManifestInfo
            {
                ToolName = registration.Manifest.ToolName,
                Enabled = registration.Manifest.Enabled,
                ApprovalRequirement = registration.Manifest.ApprovalRequirement
            }
            : null;

        var outcome = _core.Execute(
            request,
            manifest,
            () =>
            {
                if (registration == null)
                {
                    throw new InvalidOperationException("Tool registration was not resolved.");
                }

                using var dialogScope = _dialogGuard?.Enter(request.ToolName, request.RequestId);
                return registration.Handler(uiapp, request);
            });

        EnrichDocumentAndView(uiapp, request, outcome.JournalEntry);
        _platform.Journal.Record(outcome.JournalEntry);
        return outcome.Response;
    }

    private void EnrichDocumentAndView(UIApplication uiapp, ToolRequestEnvelope request, OperationJournalEntry journalEntry)
    {
        try
        {
            var activeDoc = uiapp.ActiveUIDocument?.Document;
            var activeView = activeDoc?.ActiveView;
            journalEntry.DocumentKey = !string.IsNullOrWhiteSpace(request.TargetDocument)
                ? request.TargetDocument
                : activeDoc != null ? _platform.GetDocumentKey(activeDoc) : string.Empty;
            journalEntry.ViewKey = !string.IsNullOrWhiteSpace(request.TargetView)
                ? request.TargetView
                : activeView != null ? _platform.GetViewKey(activeView) : string.Empty;
        }
        catch (Exception ex)
        {
            journalEntry.DocumentKey = request.TargetDocument;
            journalEntry.ViewKey = request.TargetView;
            _logger.Warn("Không thể enrich document/view key cho operation journal. Fallback sang target keys. Error: " + ex.Message);
        }
    }
}
