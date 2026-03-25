using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Core.Execution;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal interface IApprovalGate
{
    string IssueToken(string toolName, string requestFingerprint, string documentKey, string viewKey = "", string selectionHash = "", string previewRunId = "", string caller = "", string sessionId = "");
    string IssueToken(string toolName, string requestFingerprint, string documentKey, string caller, string sessionId);
    string BuildFingerprint(ToolRequestEnvelope request, string documentKey = "", string viewKey = "", string selectionHash = "", string previewRunId = "");
    string Validate(ToolRequestEnvelope request, string documentKey, string viewKey = "", string selectionHash = "");
    void FlushPendingPersist();
}

internal interface ISnapshotService
{
    ModelSnapshotSummary Take(Document doc, IEnumerable<int> elementIds, IEnumerable<string>? parameterNames = null);
    DiffSummary Diff(ModelSnapshotSummary before, ModelSnapshotSummary after);
}

internal interface IDocumentResolver
{
    string GetDocumentKey(Document doc);
    string GetViewKey(View view);
    Document ResolveDocument(UIApplication uiapp, string requestedDocument);
    View ResolveView(UIApplication uiapp, Document doc, string requestedView, int? requestedViewId = null);
    ViewSheet ResolveSheet(Document doc, SheetSummaryRequest request);
}

internal sealed class DocumentResolverService : IDocumentResolver
{
    private const string LookupCacheScope = "document-lookup:v1";
    private readonly DocumentCacheService _cache;

    internal DocumentResolverService(DocumentCacheService cache)
    {
        _cache = cache;
    }

    public string GetDocumentKey(Document doc)
    {
        var path = doc.PathName ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(path))
        {
            return "path:" + path.Trim().ToLowerInvariant();
        }

        return "title:" + doc.Title.Trim().ToLowerInvariant();
    }

    public string GetViewKey(View view)
    {
        return $"view:{view.Id.Value}";
    }

    public Document ResolveDocument(UIApplication uiapp, string requestedDocument)
    {
        var active = uiapp.ActiveUIDocument?.Document;
        if (string.IsNullOrWhiteSpace(requestedDocument))
        {
            if (active == null)
            {
                throw new RevitContextException("Không có active document.");
            }

            return active;
        }

        foreach (Document doc in uiapp.Application.Documents)
        {
            if (string.Equals(GetDocumentKey(doc), requestedDocument, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(doc.Title, requestedDocument, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(doc.PathName, requestedDocument, StringComparison.OrdinalIgnoreCase))
            {
                return doc;
            }
        }

        throw new InvalidOperationException("Không resolve được target document: " + requestedDocument);
    }

    public View ResolveView(UIApplication uiapp, Document doc, string requestedView, int? requestedViewId = null)
    {
        if (requestedViewId.HasValue)
        {
            var byId = doc.GetElement(new ElementId((long)requestedViewId.Value)) as View;
            if (byId != null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(requestedView))
        {
            var index = GetLookupIndex(doc);
            var normalizedRequestedView = requestedView.Trim();
            if (TryResolveView(index, normalizedRequestedView, out var resolvedViewId))
            {
                var resolvedView = doc.GetElement(new ElementId((long)resolvedViewId)) as View;
                if (resolvedView != null)
                {
                    return resolvedView;
                }
            }
        }

        if (uiapp.ActiveUIDocument?.Document?.Equals(doc) == true && doc.ActiveView != null)
        {
            return doc.ActiveView;
        }

        if (string.IsNullOrWhiteSpace(requestedView) && !requestedViewId.HasValue)
        {
            throw new RevitContextException("Không có active view.");
        }

        throw new InvalidOperationException("Không resolve được target view.");
    }

    public ViewSheet ResolveSheet(Document doc, SheetSummaryRequest request)
    {
        if (request.SheetId.HasValue)
        {
            var byId = doc.GetElement(new ElementId((long)request.SheetId.Value)) as ViewSheet;
            if (byId != null)
            {
                return byId;
            }
        }

        var index = GetLookupIndex(doc);
        if (!string.IsNullOrWhiteSpace(request.SheetNumber) &&
            index.SheetIdsByNumber.TryGetValue(request.SheetNumber.Trim(), out var byNumberId))
        {
            var byNumber = doc.GetElement(new ElementId((long)byNumberId)) as ViewSheet;
            if (byNumber != null)
            {
                return byNumber;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SheetName) &&
            index.SheetIdsByName.TryGetValue(request.SheetName.Trim(), out var byNameId))
        {
            var byName = doc.GetElement(new ElementId((long)byNameId)) as ViewSheet;
            if (byName != null)
            {
                return byName;
            }
        }

        throw new InvalidOperationException("Không resolve được sheet theo SheetId/SheetNumber/SheetName.");
    }

    private DocumentLookupIndex GetLookupIndex(Document doc)
    {
        return _cache.GetOrAdd(doc, LookupCacheScope, () => BuildLookupIndex(doc));
    }

    private static bool TryResolveView(DocumentLookupIndex index, string requestedView, out int viewId)
    {
        return index.ViewIdsByKey.TryGetValue(requestedView, out viewId)
            || index.ViewIdsByUniqueId.TryGetValue(requestedView, out viewId)
            || index.ViewIdsByName.TryGetValue(requestedView, out viewId);
    }

    private DocumentLookupIndex BuildLookupIndex(Document doc)
    {
        var index = new DocumentLookupIndex();
        foreach (View view in new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>())
        {
            var viewId = checked((int)view.Id.Value);
            AddIfMissing(index.ViewIdsByKey, GetViewKey(view), viewId);
            AddIfMissing(index.ViewIdsByUniqueId, view.UniqueId, viewId);
            AddIfMissing(index.ViewIdsByName, view.Name, viewId);

            if (view is ViewSheet sheet)
            {
                AddIfMissing(index.SheetIdsByNumber, sheet.SheetNumber, viewId);
                AddIfMissing(index.SheetIdsByName, sheet.Name, viewId);
            }
        }

        return index;
    }

    private static void AddIfMissing(IDictionary<string, int> map, string key, int value)
    {
        if (string.IsNullOrWhiteSpace(key) || map.ContainsKey(key))
        {
            return;
        }

        map[key.Trim()] = value;
    }

    private sealed class DocumentLookupIndex
    {
        internal Dictionary<string, int> ViewIdsByKey { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, int> ViewIdsByUniqueId { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, int> ViewIdsByName { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, int> SheetIdsByNumber { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, int> SheetIdsByName { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }
}
