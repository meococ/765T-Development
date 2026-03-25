using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Context;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// Context building, fingerprinting, task context, and capabilities.
/// </summary>
internal sealed partial class PlatformServices
{
    internal BridgeCapabilities GetCapabilities(IEnumerable<ToolManifest> manifests)
    {
        return new BridgeCapabilities
        {
            SupportsBackgroundRead = Settings.AllowBackgroundOpenRead && !Policy.DenyBackgroundOpenRead,
            AllowWriteTools = Settings.AllowWriteTools,
            AllowSaveTools = Settings.AllowSaveTools,
            AllowSyncTools = Settings.AllowSyncTools,
            SupportsWorkflowRuntime = true,
            SupportsInspectorLane = true,
            SupportsMcpHost = true,
            BridgeProtocolVersion = BridgeProtocol.PipeV1,
            McpProtocolVersion = BridgeConstants.McpDefaultProtocolVersion,
            Tools = manifests.ToList(),
            EnabledCapabilityPacks = Settings.EnabledCapabilityPacks?.ToList() ?? new List<string>(),
            DefaultWorkerProfile = Settings.DefaultWorkerProfile ?? new WorkerProfile(),
            VisibleShellMode = string.IsNullOrWhiteSpace(Settings.VisibleShellMode) ? WorkerShellModes.Worker : Settings.VisibleShellMode
        };
    }

    internal CurrentContextDto GetActiveViewContext(UIApplication uiapp)
    {
        return CurrentContext.Read(uiapp);
    }

    internal SelectionSummaryDto GetSelection(UIApplication uiapp)
    {
        var uidoc = uiapp.ActiveUIDocument;
        if (uidoc == null)
        {
            return new SelectionSummaryDto();
        }

        var selectedElementIds = uidoc.Selection.GetElementIds();
        return new SelectionSummaryDto
        {
            DocumentKey = GetDocumentKey(uidoc.Document),
            ViewKey = GetViewKey(uidoc.Document.ActiveView),
            ElementIds = selectedElementIds.Select(x => checked((int)x.Value)).ToList(),
            Count = selectedElementIds.Count
        };
    }

    internal TaskContextResponse GetTaskContext(UIApplication uiapp, Document doc, TaskContextRequest request, IEnumerable<ToolManifest>? manifests = null)
    {
        request ??= new TaskContextRequest();
        var maxEvents = Math.Max(1, request.MaxRecentEvents);
        var maxOperations = Math.Max(1, request.MaxRecentOperations);
        var documentKey = GetDocumentKey(doc);

        var response = new TaskContextResponse
        {
            Document = SummarizeDocument(uiapp, doc),
            ActiveContext = GetActiveViewContext(uiapp),
            Selection = GetSelection(uiapp),
            Fingerprint = BuildContextFingerprint(uiapp, doc),
            RecentEvents = EventIndex.GetRecent()
                .Where(x => string.Equals(x.DocumentKey, documentKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.TimestampUtc)
                .Take(maxEvents)
                .ToList(),
            RecentOperations = Journal.GetRecent()
                .Where(x => string.IsNullOrWhiteSpace(x.DocumentKey) || string.Equals(x.DocumentKey, documentKey, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.StartedUtc)
                .Take(maxOperations)
                .ToList()
        };

        if (request.IncludeCapabilities)
        {
            response.Capabilities = GetCapabilities(manifests ?? new List<ToolManifest>());
        }

        if (request.IncludeToolCatalog && manifests != null)
        {
            response.Tools = manifests.OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Shortcut fields — agent đọc ngay mà không cần parse sâu Capabilities
        response.WriteEnabled = Settings.AllowWriteTools;
        response.BridgeAlive = true;

        return response;
    }

    internal ContextFingerprint BuildContextFingerprint(UIApplication uiapp, Document doc)
    {
        var activeUiDoc = uiapp.ActiveUIDocument;
        var isActive = activeUiDoc?.Document?.Equals(doc) == true;
        var selectedIds = isActive && activeUiDoc != null
            ? activeUiDoc.Selection.GetElementIds().Select(x => checked((int)x.Value)).OrderBy(x => x).ToList()
            : new List<int>();
        return new ContextFingerprint
        {
            DocumentKey = GetDocumentKey(doc),
            ViewKey = isActive && doc.ActiveView != null ? GetViewKey(doc.ActiveView) : string.Empty,
            SelectionCount = selectedIds.Count,
            SelectedElementIds = selectedIds,
            SelectionHash = BuildSelectionHash(selectedIds),
            ActiveDocEpoch = EventIndex.GetDocumentEpoch(GetDocumentKey(doc))
        };
    }

    /// <summary>
    /// So sánh expected context với context hiện tại.
    /// Read-only tools: expectedContextJson có thể rỗng → trả true.
    /// Mutate/file tools: PHẢI gọi MatchesExpectedContextStrict() thay vì hàm này.
    /// </summary>
    internal bool MatchesExpectedContext(UIApplication uiapp, Document doc, string expectedContextJson)
    {
        if (string.IsNullOrWhiteSpace(expectedContextJson))
        {
            return true;
        }

        return CompareFingerprints(uiapp, doc, expectedContextJson);
    }

    /// <summary>
    /// P0-2 FIX: Bắt buộc context binding cho mutation tools.
    /// Nếu expectedContextJson rỗng → FAIL (không cho qua).
    /// Đảm bảo preview (dry-run) và execute chạy trên cùng document/view/selection.
    /// </summary>
    internal bool MatchesExpectedContextStrict(UIApplication uiapp, Document doc, string expectedContextJson)
    {
        if (string.IsNullOrWhiteSpace(expectedContextJson))
        {
            return false;
        }

        return CompareFingerprints(uiapp, doc, expectedContextJson);
    }

    private bool CompareFingerprints(UIApplication uiapp, Document doc, string expectedContextJson)
    {
        var expected = BIM765T.Revit.Contracts.Serialization.JsonUtil.Deserialize<ContextFingerprint>(expectedContextJson);
        var current = BuildContextFingerprint(uiapp, doc);
        return string.Equals(expected.DocumentKey ?? string.Empty, current.DocumentKey ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && string.Equals(expected.ViewKey ?? string.Empty, current.ViewKey ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && CompareSelection(expected, current)
            && (expected.ActiveDocEpoch <= 0 || expected.ActiveDocEpoch == current.ActiveDocEpoch);
    }

    internal ExecutionResult FinalizePreviewResult(UIApplication uiapp, Document doc, ToolRequestEnvelope request, ExecutionResult result)
    {
        var context = BuildContextFingerprint(uiapp, doc);
        var previewRunId = string.IsNullOrWhiteSpace(request.PreviewRunId)
            ? Guid.NewGuid().ToString("N")
            : request.PreviewRunId;

        result.PreviewRunId = previewRunId;
        result.ResolvedContext = context;
        result.ApprovalToken = Approval.IssueToken(
            request.ToolName,
            Approval.BuildFingerprint(request, context.DocumentKey, context.ViewKey, context.SelectionHash, previewRunId),
            context.DocumentKey,
            context.ViewKey,
            context.SelectionHash,
            previewRunId,
            request.Caller,
            request.SessionId);
        return result;
    }

    internal string ValidateApprovalRequest(UIApplication uiapp, Document doc, ToolRequestEnvelope request)
    {
        var context = BuildContextFingerprint(uiapp, doc);
        return Approval.Validate(request, context.DocumentKey, context.ViewKey, context.SelectionHash);
    }

    private static bool CompareSelection(ContextFingerprint expected, ContextFingerprint current)
    {
        if (!string.IsNullOrWhiteSpace(expected.SelectionHash))
        {
            return string.Equals(expected.SelectionHash, current.SelectionHash, StringComparison.OrdinalIgnoreCase);
        }

        var expectedIds = (expected.SelectedElementIds ?? new List<int>()).OrderBy(x => x).ToList();
        var currentIds = (current.SelectedElementIds ?? new List<int>()).OrderBy(x => x).ToList();
        if (expectedIds.Count > 0 || currentIds.Count > 0)
        {
            return expectedIds.SequenceEqual(currentIds);
        }

        if (expected.SelectionCount > 0 || current.SelectionCount > 0)
        {
            return false;
        }

        return expected.SelectionCount == current.SelectionCount;
    }

    private static string BuildSelectionHash(IEnumerable<int> ids)
    {
        var ordered = ids.OrderBy(x => x).ToArray();
        if (ordered.Length == 0)
        {
            return string.Empty;
        }

        var raw = string.Join(",", ordered);
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return BitConverter.ToString(bytes).Replace("-", string.Empty);
    }
}
