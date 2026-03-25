using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class ModelStateGraphService
{
    private readonly DocumentCacheService _cache;

    internal ModelStateGraphService(DocumentCacheService cache)
    {
        _cache = cache;
    }

    internal DocumentStateGraphSnapshot CaptureHotGraph(UIApplication uiapp, PlatformServices platform, Document doc)
    {
        var fingerprint = platform.BuildContextFingerprint(uiapp, doc);
        var cacheKey = $"state_graph|{fingerprint.ViewKey}|{fingerprint.SelectionHash}|{fingerprint.ActiveDocEpoch}";
        return _cache.GetOrAdd(doc, cacheKey, () => BuildSnapshot(uiapp, platform, doc, fingerprint));
    }

    private static DocumentStateGraphSnapshot BuildSnapshot(UIApplication uiapp, PlatformServices platform, Document doc, ContextFingerprint fingerprint)
    {
        var selection = platform.GetSelection(uiapp);
        var recentChangedIds = platform.Journal.GetRecent()
            .Where(x => string.Equals(x.DocumentKey, platform.GetDocumentKey(doc), StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.ChangedIds)
            .Distinct()
            .Take(25)
            .ToList();

        var snapshot = new DocumentStateGraphSnapshot
        {
            DocumentKey = platform.GetDocumentKey(doc),
            ViewKey = fingerprint.ViewKey,
            ActiveDocEpoch = checked((int)fingerprint.ActiveDocEpoch),
            RefreshedUtc = DateTime.UtcNow,
            SelectionCount = selection.Count,
            RecentChangedIds = recentChangedIds,
            ElementCountEstimate = SafeCount(() => checked((int)new FilteredElementCollector(doc).WhereElementIsNotElementType().GetElementCount())),
            FamilyInstanceCountEstimate = SafeCount(() => checked((int)new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).GetElementCount()))
        };

        snapshot.Nodes.Add(new DocumentStateNode
        {
            NodeId = "document",
            Kind = "document",
            Label = doc.Title ?? snapshot.DocumentKey
        });

        if (!string.IsNullOrWhiteSpace(snapshot.ViewKey))
        {
            snapshot.Nodes.Add(new DocumentStateNode
            {
                NodeId = snapshot.ViewKey,
                Kind = "view",
                Label = doc.ActiveView?.Name ?? snapshot.ViewKey
            });
            snapshot.Edges.Add(new DocumentStateEdge
            {
                FromNodeId = "document",
                ToNodeId = snapshot.ViewKey,
                Relation = "active_view"
            });
        }

        var focusIds = selection.ElementIds.Count > 0 ? selection.ElementIds : recentChangedIds.Take(10).ToList();
        if (focusIds.Count > 0)
        {
            var graph = platform.BuildElementGraph(doc, new ElementGraphRequest
            {
                ElementIds = focusIds,
                MaxDepth = 1,
                IncludeDependents = true,
                IncludeHost = true,
                IncludeType = true,
                IncludeOwnerView = true
            });

            foreach (var node in graph.Nodes)
            {
                snapshot.Nodes.Add(new DocumentStateNode
                {
                    NodeId = "element:" + node.ElementId,
                    Kind = node.Kind,
                    Label = node.Label,
                    ElementId = node.ElementId
                });
            }

            foreach (var edge in graph.Edges)
            {
                snapshot.Edges.Add(new DocumentStateEdge
                {
                    FromNodeId = "element:" + edge.FromElementId,
                    ToNodeId = "element:" + edge.ToElementId,
                    Relation = edge.Relation
                });
            }
        }

        snapshot.Nodes = snapshot.Nodes
            .GroupBy(x => x.NodeId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
        snapshot.Edges = snapshot.Edges
            .GroupBy(x => $"{x.FromNodeId}|{x.ToNodeId}|{x.Relation}", StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
        return snapshot;
    }

    private static int SafeCount(Func<int> counter)
    {
        try
        {
            return counter();
        }
        catch
        {
            return 0;
        }
    }
}
