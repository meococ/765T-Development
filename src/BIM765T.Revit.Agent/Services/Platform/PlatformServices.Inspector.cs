using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// Inspector lane inspired by RevitLookup / RevitDBExplorer:
/// giải thích element, quan hệ phụ thuộc, trace parameter, usage của view/sheet.
/// </summary>
internal sealed partial class PlatformServices
{
    internal ElementExplainResponse ExplainElement(UIApplication uiapp, Document doc, ElementExplainRequest request)
    {
        var element = doc.GetElement(new ElementId((long)request.ElementId))
            ?? throw new InvalidOperationException($"Element Id={request.ElementId} không tồn tại.");

        var response = new ElementExplainResponse
        {
            DocumentKey = GetDocumentKey(doc),
            Element = SummarizeElement(doc, element, request.IncludeParameters)
        };

        if (request.ParameterNames.Count > 0)
        {
            response.Element.Parameters = response.Element.Parameters
                .Where(x => request.ParameterNames.Any(p => string.Equals(p, x.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        if (element.OwnerViewId != ElementId.InvalidElementId)
        {
            var ownerView = doc.GetElement(element.OwnerViewId) as View;
            if (ownerView != null)
            {
                response.OwnerViewId = checked((int)element.OwnerViewId.Value);
                response.OwnerViewKey = GetViewKey(ownerView);
            }
        }

        if (request.IncludeHostRelations && element is FamilyInstance familyInstance)
        {
            if (familyInstance.Host != null)
            {
                response.HostElementId = checked((int)familyInstance.Host.Id.Value);
                response.HostCategoryName = familyInstance.Host.Category?.Name ?? string.Empty;
            }

            if (familyInstance.SuperComponent != null)
            {
                response.SuperComponentElementId = checked((int)familyInstance.SuperComponent.Id.Value);
                response.SuperComponentCategoryName = familyInstance.SuperComponent.Category?.Name ?? string.Empty;
            }
        }

        if (request.IncludeDependents)
        {
            try
            {
                response.DependentElementIds = element.GetDependentElements(null)
                    .Select(x => checked((int)x.Value))
                    .Distinct()
                    .Take(100)
                    .ToList();
            }
            catch
            {
                response.DependentElementIds = new List<int>();
            }
        }

        response.Explanations.Add($"Element {response.Element.ElementId} thuộc category `{response.Element.CategoryName}`.");
        response.Explanations.Add($"Type `{response.Element.TypeName}` / Family `{response.Element.FamilyName}`.");
        if (!string.IsNullOrWhiteSpace(response.Element.WorksetName))
        {
            response.Explanations.Add($"Đang nằm trong workset `{response.Element.WorksetName}`.");
        }
        if (response.OwnerViewId.HasValue)
        {
            response.Explanations.Add($"Owner view: {response.OwnerViewKey}.");
        }
        if (response.HostElementId.HasValue)
        {
            response.Explanations.Add($"Hosted bởi element {response.HostElementId} ({response.HostCategoryName}).");
        }
        if (response.SuperComponentElementId.HasValue)
        {
            response.Explanations.Add($"Nested trong super component {response.SuperComponentElementId} ({response.SuperComponentCategoryName}).");
        }
        if (response.DependentElementIds.Count > 0)
        {
            response.Explanations.Add($"Có {response.DependentElementIds.Count} dependent elements.");
        }

        return response;
    }

    internal ElementGraphResponse BuildElementGraph(Document doc, ElementGraphRequest request)
    {
        request ??= new ElementGraphRequest();
        var response = new ElementGraphResponse
        {
            DocumentKey = GetDocumentKey(doc)
        };

        var visited = new HashSet<int>();
        var queue = new Queue<(Element Element, int Depth)>();
        foreach (var id in request.ElementIds.Distinct())
        {
            var element = doc.GetElement(new ElementId((long)id));
            if (element != null)
            {
                queue.Enqueue((element, 0));
            }
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var currentId = checked((int)current.Element.Id.Value);
            if (!visited.Add(currentId))
            {
                continue;
            }

            response.Nodes.Add(new GraphNodeDto
            {
                ElementId = currentId,
                Label = $"{current.Element.Category?.Name ?? current.Element.GetType().Name} • {current.Element.Name}",
                Kind = current.Element.GetType().Name
            });

            if (current.Depth >= Math.Max(0, request.MaxDepth))
            {
                continue;
            }

            if (request.IncludeType)
            {
                var typeId = current.Element.GetTypeId();
                if (typeId != ElementId.InvalidElementId && doc.GetElement(typeId) is ElementType type)
                {
                    AddGraphNode(response, checked((int)type.Id.Value), $"Type • {type.Name}", "ElementType");
                    response.Edges.Add(new GraphEdgeDto { FromElementId = currentId, ToElementId = checked((int)type.Id.Value), Relation = "type" });
                }
            }

            if (request.IncludeOwnerView && current.Element.OwnerViewId != ElementId.InvalidElementId && doc.GetElement(current.Element.OwnerViewId) is View ownerView)
            {
                var ownerId = checked((int)ownerView.Id.Value);
                AddGraphNode(response, ownerId, $"View • {ownerView.Name}", "View");
                response.Edges.Add(new GraphEdgeDto { FromElementId = currentId, ToElementId = ownerId, Relation = "owner_view" });
            }

            if (request.IncludeHost && current.Element is FamilyInstance familyInstance)
            {
                if (familyInstance.Host != null)
                {
                    var hostId = checked((int)familyInstance.Host.Id.Value);
                    AddGraphNode(response, hostId, $"Host • {familyInstance.Host.Name}", familyInstance.Host.GetType().Name);
                    response.Edges.Add(new GraphEdgeDto { FromElementId = currentId, ToElementId = hostId, Relation = "host" });
                    queue.Enqueue((familyInstance.Host, current.Depth + 1));
                }

                if (familyInstance.SuperComponent != null)
                {
                    var superId = checked((int)familyInstance.SuperComponent.Id.Value);
                    AddGraphNode(response, superId, $"SuperComponent • {familyInstance.SuperComponent.Name}", familyInstance.SuperComponent.GetType().Name);
                    response.Edges.Add(new GraphEdgeDto { FromElementId = currentId, ToElementId = superId, Relation = "super_component" });
                    queue.Enqueue((familyInstance.SuperComponent, current.Depth + 1));
                }
            }

            if (request.IncludeDependents)
            {
                try
                {
                    foreach (var dependentId in current.Element.GetDependentElements(null).Take(50))
                    {
                        var dependent = doc.GetElement(dependentId);
                        if (dependent == null)
                        {
                            continue;
                        }

                        var depInt = checked((int)dependent.Id.Value);
                        AddGraphNode(response, depInt, $"Dependent • {dependent.Name}", dependent.GetType().Name);
                        response.Edges.Add(new GraphEdgeDto { FromElementId = currentId, ToElementId = depInt, Relation = "dependent" });
                        queue.Enqueue((dependent, current.Depth + 1));
                    }
                }
                catch
                {
                    // ignore unsupported dependent traversal
                }
            }
        }

        response.Nodes = response.Nodes
            .GroupBy(x => x.ElementId)
            .Select(x => x.First())
            .OrderBy(x => x.ElementId)
            .ToList();
        response.Edges = response.Edges
            .GroupBy(x => $"{x.FromElementId}:{x.ToElementId}:{x.Relation}", StringComparer.Ordinal)
            .Select(x => x.First())
            .ToList();
        return response;
    }

    internal ParameterTraceResponse TraceParameter(UIApplication uiapp, Document doc, ParameterTraceRequest request)
    {
        request ??= new ParameterTraceRequest();
        var response = new ParameterTraceResponse
        {
            DocumentKey = GetDocumentKey(doc),
            ParameterName = request.ParameterName ?? string.Empty
        };

        IEnumerable<Element> elements;
        if (request.ElementIds.Count > 0)
        {
            elements = request.ElementIds
                .Select(x => doc.GetElement(new ElementId((long)x)))
                .Where(x => x != null)!;
        }
        else
        {
            elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements();
        }

        if (request.CategoryNames.Count > 0)
        {
            elements = elements.Where(x => x.Category != null && request.CategoryNames.Any(c => string.Equals(c, x.Category.Name, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var element in elements.Take(Math.Max(1, request.MaxResults * 4)))
        {
            var parameter = element.LookupParameter(request.ParameterName);
            if (parameter == null)
            {
                continue;
            }

            var value = ParameterValue(parameter);
            if (!request.IncludeEmptyValues && string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            response.Items.Add(new ParameterTraceItem
            {
                ElementId = checked((int)element.Id.Value),
                CategoryName = element.Category?.Name ?? string.Empty,
                ElementName = element.Name ?? string.Empty,
                Value = value,
                IsReadOnly = parameter.IsReadOnly,
                StorageType = parameter.StorageType.ToString()
            });

            if (response.Items.Count >= Math.Max(1, request.MaxResults))
            {
                break;
            }
        }

        response.Count = response.Items.Count;
        return response;
    }

    internal ViewUsageResponse DescribeViewUsage(UIApplication uiapp, Document doc, ViewUsageRequest request)
    {
        request ??= new ViewUsageRequest();
        var view = ResolveView(uiapp, doc, request.ViewName, request.ViewId);
        var response = new ViewUsageResponse
        {
            DocumentKey = GetDocumentKey(doc),
            View = new ViewSummaryDto
            {
                ViewKey = GetViewKey(view),
                ViewId = checked((int)view.Id.Value),
                Name = view.Name,
                ViewType = view.ViewType.ToString(),
                DocumentKey = GetDocumentKey(doc),
                LevelId = view.GenLevel != null ? checked((int)view.GenLevel.Id.Value) : (int?)null,
                LevelName = view.GenLevel?.Name ?? string.Empty,
                IsTemplate = view.IsTemplate
            }
        };

        var collector = new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType();
        var visible = collector.ToElements();
        response.VisibleElementCountEstimate = visible.Count;
        response.SampleElementIds = visible.Take(Math.Max(1, request.MaxSamples)).Select(x => checked((int)x.Id.Value)).ToList();

        if (request.IncludeFilters)
        {
            try
            {
                response.AppliedFilters = view.GetFilters()
                    .Select(id => doc.GetElement(id)?.Name ?? id.Value.ToString(CultureInfo.InvariantCulture))
                    .ToList();
            }
            catch
            {
                response.AppliedFilters = new List<string>();
            }
        }

        if (request.IncludeSheets)
        {
            var viewportMatches = new FilteredElementCollector(doc)
                .OfClass(typeof(Viewport))
                .Cast<Viewport>()
                .Where(vp => vp.ViewId == view.Id)
                .Select(vp => doc.GetElement(vp.SheetId) as ViewSheet)
                .Where(x => x != null)
                .Distinct()
                .Select(sheet => $"{sheet!.SheetNumber} • {sheet.Name}")
                .ToList();
            response.PlacedOnSheets = viewportMatches;
        }

        return response;
    }

    internal SheetDependenciesResponse DescribeSheetDependencies(Document doc, SheetDependenciesRequest request)
    {
        request ??= new SheetDependenciesRequest();
        ViewSheet? sheet = null;
        if (request.SheetId.HasValue && request.SheetId.Value > 0)
        {
            sheet = doc.GetElement(new ElementId((long)request.SheetId.Value)) as ViewSheet;
        }

        if (sheet == null && !string.IsNullOrWhiteSpace(request.SheetNumber))
        {
            sheet = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSheet))
                .Cast<ViewSheet>()
                .FirstOrDefault(x => string.Equals(x.SheetNumber, request.SheetNumber, StringComparison.OrdinalIgnoreCase));
        }

        sheet ??= doc.ActiveView as ViewSheet;
        if (sheet == null)
        {
            throw new InvalidOperationException("Không resolve được sheet để inspect dependencies.");
        }

        var response = new SheetDependenciesResponse
        {
            DocumentKey = GetDocumentKey(doc),
            SheetId = checked((int)sheet.Id.Value),
            SheetNumber = sheet.SheetNumber ?? string.Empty,
            SheetName = sheet.Name ?? string.Empty
        };

        foreach (var titleBlock in new FilteredElementCollector(doc, sheet.Id)
                     .OfCategory(BuiltInCategory.OST_TitleBlocks)
                     .WhereElementIsNotElementType()
                     .ToElements())
        {
            response.Dependencies.Add(new SheetDependencyItem
            {
                ElementId = checked((int)titleBlock.Id.Value),
                Kind = "title_block",
                Name = titleBlock.Name ?? string.Empty
            });
        }

        if (request.IncludeViewports)
        {
            foreach (var viewportId in sheet.GetAllViewports())
            {
                if (doc.GetElement(viewportId) is Viewport viewport)
                {
                    var view = doc.GetElement(viewport.ViewId) as View;
                    response.Dependencies.Add(new SheetDependencyItem
                    {
                        ElementId = checked((int)viewport.Id.Value),
                        Kind = "viewport",
                        Name = view?.Name ?? viewport.Id.Value.ToString(CultureInfo.InvariantCulture)
                    });
                }
            }
        }

        if (request.IncludeSchedules)
        {
            foreach (var schedule in new FilteredElementCollector(doc, sheet.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>())
            {
                var scheduleView = doc.GetElement(schedule.ScheduleId) as ViewSchedule;
                response.Dependencies.Add(new SheetDependencyItem
                {
                    ElementId = checked((int)schedule.Id.Value),
                    Kind = "schedule",
                    Name = scheduleView?.Name ?? schedule.Id.Value.ToString(CultureInfo.InvariantCulture)
                });
            }
        }

        return response;
    }

    private static void AddGraphNode(ElementGraphResponse response, int elementId, string label, string kind)
    {
        if (response.Nodes.Any(x => x.ElementId == elementId))
        {
            return;
        }

        response.Nodes.Add(new GraphNodeDto
        {
            ElementId = elementId,
            Label = label,
            Kind = kind
        });
    }
}
