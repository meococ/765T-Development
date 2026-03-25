using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Core.Execution;
using BIM765T.Revit.Agent.Services.Bridge;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.Infrastructure.Logging;
using BIM765T.Revit.Agent.Infrastructure.Observability;
using BIM765T.Revit.Agent.Infrastructure.Time;
using BIM765T.Revit.Agent.Services.Context;
using BIM765T.Revit.Contracts.Context;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// Central facade for Revit platform operations.
/// Split into partial classes for maintainability:
///   PlatformServices.cs             - Constructor, properties, document/view resolution, settings/policy
///   PlatformServices.Query.cs       - Element querying, inspection, summarization
///   PlatformServices.Review.cs      - Review operations (warnings, health, worksets, sheets, snapshots)
///   PlatformServices.Context.cs     - Context building, fingerprinting, task context, capabilities
/// </summary>
internal sealed partial class PlatformServices
{
    internal PlatformServices(
        AgentSettings settings,
        BridgePolicy policy,
        IAgentLogger logger,
        CurrentContextService currentContextService,
        OperationJournalService journal,
        EventIndexService eventIndex,
        IDocumentResolver documentResolver,
        IApprovalGate approval,
        ISnapshotService snapshot,
        ISystemClock clock)
    {
        Settings = settings;
        Policy = policy;
        Logger = logger;
        CurrentContext = currentContextService;
        Journal = journal;
        EventIndex = eventIndex;
        Resolver = documentResolver;
        Approval = approval;
        Snapshot = snapshot;
        Clock = clock;
    }

    internal AgentSettings Settings { get; }
    internal BridgePolicy Policy { get; }
    internal IAgentLogger Logger { get; }
    internal CurrentContextService CurrentContext { get; }
    internal OperationJournalService Journal { get; }
    internal EventIndexService EventIndex { get; }
    internal IDocumentResolver Resolver { get; }
    internal IApprovalGate Approval { get; }
    internal ISnapshotService Snapshot { get; }
    internal ISystemClock Clock { get; }

    // ── Document / View Resolution ──────────────────────────────────────

    internal string GetDocumentKey(Document doc)
    {
        return Resolver.GetDocumentKey(doc);
    }

    internal string GetViewKey(View view)
    {
        return Resolver.GetViewKey(view);
    }

    internal View ResolveView(UIApplication uiapp, Document doc, string requestedView, int? requestedViewId = null)
    {
        return Resolver.ResolveView(uiapp, doc, requestedView, requestedViewId);
    }

    internal ViewSheet ResolveSheet(Document doc, SheetSummaryRequest request)
    {
        return Resolver.ResolveSheet(doc, request);
    }

    internal Document ResolveDocument(UIApplication uiapp, string requestedDocument)
    {
        return Resolver.ResolveDocument(uiapp, requestedDocument);
    }

    // ── Settings / Policy ───────────────────────────────────────────────

    internal bool IsToolEnabled(string toolName, PermissionLevel permissionLevel, ToolManifestMetadata? metadata = null)
    {
        var product = WorkerProductClassifier.Classify(
            toolName,
            permissionLevel,
            metadata?.CapabilityPack ?? string.Empty,
            metadata?.SkillGroup ?? string.Empty,
            metadata?.Audience ?? string.Empty,
            metadata?.Visibility ?? string.Empty);
        if (!IsProductSurfaceEnabled(product))
        {
            return false;
        }

        if (Policy.DisabledTools == null)
        {
            return IsRuntimeEnabled(toolName, permissionLevel);
        }

        return !Policy.DisabledTools.Any(x => string.Equals(x, toolName, StringComparison.OrdinalIgnoreCase))
            && IsRuntimeEnabled(toolName, permissionLevel);
    }

    private bool IsProductSurfaceEnabled(WorkerProductDescriptor product)
    {
        if (Policy.DisabledCapabilityPacks != null
            && Policy.DisabledCapabilityPacks.Any(x => string.Equals(x, product.CapabilityPack, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (Settings.EnabledCapabilityPacks != null
            && Settings.EnabledCapabilityPacks.Count > 0
            && !Settings.EnabledCapabilityPacks.Any(x => string.Equals(x, product.CapabilityPack, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (Policy.DisabledSkillGroups != null
            && Policy.DisabledSkillGroups.Any(x => string.Equals(x, product.SkillGroup, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (Settings.EnabledSkillGroups != null
            && Settings.EnabledSkillGroups.Count > 0
            && !Settings.EnabledSkillGroups.Any(x => string.Equals(x, product.SkillGroup, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private bool IsRuntimeEnabled(string toolName, PermissionLevel permissionLevel)
    {
        if (string.Equals(toolName, BIM765T.Revit.Contracts.Bridge.ToolNames.DocumentOpenBackgroundRead, StringComparison.OrdinalIgnoreCase))
        {
            return Settings.AllowBackgroundOpenRead && !Policy.DenyBackgroundOpenRead;
        }

        if (permissionLevel == PermissionLevel.Mutate)
        {
            if (!Settings.AllowWriteTools)
            {
                return false;
            }

            if (string.Equals(toolName, BIM765T.Revit.Contracts.Bridge.ToolNames.ElementDeleteSafe, StringComparison.OrdinalIgnoreCase))
            {
                return Settings.AllowDeleteTools;
            }
        }

        if (permissionLevel == PermissionLevel.FileLifecycle)
        {
            if (string.Equals(toolName, BIM765T.Revit.Contracts.Bridge.ToolNames.WorksharingSynchronizeWithCentral, StringComparison.OrdinalIgnoreCase))
            {
                return Settings.AllowSyncTools;
            }

            return Settings.AllowSaveTools;
        }

        return true;
    }

    // ── Document Summarization ──────────────────────────────────────────

    internal DocumentSummaryDto SummarizeDocument(UIApplication uiapp, Document doc)
    {
        return new DocumentSummaryDto
        {
            DocumentKey = GetDocumentKey(doc),
            Title = doc.Title,
            PathName = doc.PathName ?? string.Empty,
            IsActive = uiapp.ActiveUIDocument?.Document?.Equals(doc) == true,
            IsModified = doc.IsModified,
            IsWorkshared = doc.IsWorkshared,
            IsLinked = doc.IsLinked,
            IsFamilyDocument = doc.IsFamilyDocument,
            CanSave = !string.IsNullOrWhiteSpace(doc.PathName),
            CanSynchronize = doc.IsWorkshared && !doc.IsLinked
        };
    }

    internal DocumentListResponse ListOpenDocuments(UIApplication uiapp)
    {
        var response = new DocumentListResponse();
        foreach (Document doc in uiapp.Application.Documents)
        {
            response.Documents.Add(SummarizeDocument(uiapp, doc));
        }
        return response;
    }

    // ── Utilities ───────────────────────────────────────────────────────

    internal static T SafeValue<T>(Func<T> getter, T fallback)
    {
        try
        {
            return getter();
        }
        catch
        {
            return fallback;
        }
    }
}
