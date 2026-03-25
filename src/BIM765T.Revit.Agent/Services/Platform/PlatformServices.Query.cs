using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// Element querying, inspection, and summarization methods.
/// </summary>
internal sealed partial class PlatformServices
{
    internal ElementQueryResponse QueryElements(UIApplication uiapp, Document doc, ElementQueryRequest request)
    {
        request ??= new ElementQueryRequest();
        var elementIds = request.ElementIds ?? new List<int>();
        var categoryNames = request.CategoryNames ?? new List<string>();
        IEnumerable<Element> elements;
        if (elementIds.Count > 0)
        {
            elements = elementIds
                .Select(id => doc.GetElement(new ElementId((long)id)))
                .Where(x => x != null)!;
        }
        else if (request.SelectedOnly && uiapp.ActiveUIDocument?.Document.Equals(doc) == true)
        {
            elements = uiapp.ActiveUIDocument.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(x => x != null)!;
        }
        else
        {
            FilteredElementCollector collector = request.ViewScopeOnly && uiapp.ActiveUIDocument?.Document.Equals(doc) == true
                ? new FilteredElementCollector(doc, doc.ActiveView.Id)
                : new FilteredElementCollector(doc);

            collector = collector.WhereElementIsNotElementType();
            elements = collector.ToElements();
        }

        if (categoryNames.Count > 0)
        {
            elements = elements.Where(x => x.Category != null && categoryNames.Any(c => string.Equals(c, x.Category.Name, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(request.ClassName))
        {
            elements = elements.Where(x => string.Equals(x.GetType().Name, request.ClassName, StringComparison.OrdinalIgnoreCase));
        }

        var result = new ElementQueryResponse
        {
            DocumentKey = GetDocumentKey(doc)
        };

        foreach (var element in elements.Take(Math.Max(1, request.MaxResults)))
        {
            try
            {
                result.Items.Add(SummarizeElement(doc, element, request.IncludeParameters));
            }
            catch (Exception ex)
            {
                Logger.Error($"SummarizeElement failed for element {SafeValue(() => checked((int)element.Id.Value), -1)}.", ex);
                result.Items.Add(BuildFallbackElementSummary(doc, element));
            }
        }

        result.Count = result.Items.Count;
        return result;
    }

    internal ElementSummaryDto InspectElement(Document doc, Element element, bool includeParameters)
    {
        return SummarizeElement(doc, element, includeParameters);
    }

    private ElementSummaryDto SummarizeElement(Document doc, Element element, bool includeParameters)
    {
        var typeId = ElementId.InvalidElementId;
        ElementType? type = null;
        try
        {
            typeId = element.GetTypeId();
            if (typeId != ElementId.InvalidElementId)
            {
                type = doc.GetElement(typeId) as ElementType;
            }
        }
        catch
        {
            typeId = ElementId.InvalidElementId;
        }

        var summary = new ElementSummaryDto
        {
            ElementId = SafeValue(() => checked((int)element.Id.Value), -1),
            UniqueId = SafeValue(() => element.UniqueId ?? string.Empty, string.Empty),
            DocumentKey = GetDocumentKey(doc),
            CategoryName = SafeValue(() => element.Category?.Name ?? string.Empty, string.Empty),
            ClassName = SafeValue(() => element.GetType().Name, string.Empty),
            Name = SafeValue(() => element.Name ?? string.Empty, string.Empty),
            TypeId = typeId != ElementId.InvalidElementId ? checked((int)typeId.Value) : -1,
            TypeName = SafeValue(() => type?.Name ?? string.Empty, string.Empty),
            FamilyName = SafeValue(() => type?.FamilyName ?? string.Empty, string.Empty),
            LevelId = SafeValue(() => element.LevelId != ElementId.InvalidElementId ? checked((int)element.LevelId.Value) : (int?)null, null),
            LevelName = SafeValue(() => ResolveLevelName(doc, element), string.Empty),
            BoundingBox = SafeValue(() => ResolveBoundingBox(doc, element), null),
            FamilyPlacementType = SafeValue(() => ResolveFamilyPlacementType(element, type), string.Empty),
            WorksetId = SafeValue(() => element.WorksetId != WorksetId.InvalidWorksetId ? checked((int)element.WorksetId.IntegerValue) : (int?)null, null),
            WorksetName = SafeValue(() => ResolveWorksetName(doc, element), string.Empty)
        };

        PopulateLocationSummary(element, summary);

        if (includeParameters)
        {
            try
            {
                foreach (Parameter parameter in element.Parameters)
                {
                    summary.Parameters.Add(new ParameterValueDto
                    {
                        Name = SafeValue(() => parameter.Definition?.Name ?? string.Empty, string.Empty),
                        StorageType = SafeValue(() => parameter.StorageType.ToString(), string.Empty),
                        Value = SafeValue(() => ParameterValue(parameter), string.Empty),
                        IsReadOnly = SafeValue(() => parameter.IsReadOnly, false)
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"Parameter enumeration failed for element {summary.ElementId}: {ex.Message}");
            }
        }

        return summary;
    }

    private ElementSummaryDto BuildFallbackElementSummary(Document doc, Element element)
    {
        return new ElementSummaryDto
        {
            ElementId = SafeValue(() => checked((int)element.Id.Value), -1),
            UniqueId = SafeValue(() => element.UniqueId ?? string.Empty, string.Empty),
            DocumentKey = GetDocumentKey(doc),
            CategoryName = SafeValue(() => element.Category?.Name ?? string.Empty, string.Empty),
            ClassName = SafeValue(() => element.GetType().Name, string.Empty),
            Name = SafeValue(() => element.Name ?? string.Empty, "<unresolved>"),
            TypeId = -1,
            TypeName = string.Empty,
            FamilyName = string.Empty,
            LevelId = null,
            LevelName = string.Empty,
            BoundingBox = null,
            FamilyPlacementType = string.Empty,
            WorksetId = SafeValue(() => element.WorksetId != WorksetId.InvalidWorksetId ? checked((int)element.WorksetId.IntegerValue) : (int?)null, null),
            WorksetName = SafeValue(() => ResolveWorksetName(doc, element), string.Empty)
        };
    }

    internal static string ParameterValue(Parameter parameter)
    {
        switch (parameter.StorageType)
        {
            case StorageType.String:
                return parameter.AsString() ?? string.Empty;
            case StorageType.Integer:
                return parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
            case StorageType.Double:
                return parameter.AsValueString() ?? parameter.AsDouble().ToString(CultureInfo.InvariantCulture);
            case StorageType.ElementId:
                return parameter.AsElementId().Value.ToString(CultureInfo.InvariantCulture);
            default:
                return parameter.AsValueString() ?? string.Empty;
        }
    }

    private static string ResolveLevelName(Document doc, Element element)
    {
        if (element.LevelId == ElementId.InvalidElementId)
        {
            return string.Empty;
        }

        return (doc.GetElement(element.LevelId) as Level)?.Name ?? string.Empty;
    }

    private static string ResolveWorksetName(Document doc, Element element)
    {
        if (element.WorksetId == WorksetId.InvalidWorksetId)
        {
            return string.Empty;
        }

        return doc.GetWorksetTable().GetWorkset(element.WorksetId)?.Name ?? string.Empty;
    }

    private static BoundingBoxDto? ResolveBoundingBox(Document doc, Element element)
    {
        var bbox = element.get_BoundingBox(doc.ActiveView) ?? element.get_BoundingBox(null);
        if (bbox == null)
        {
            return null;
        }

        return new BoundingBoxDto
        {
            MinX = bbox.Min.X,
            MinY = bbox.Min.Y,
            MinZ = bbox.Min.Z,
            MaxX = bbox.Max.X,
            MaxY = bbox.Max.Y,
            MaxZ = bbox.Max.Z
        };
    }

    private static void PopulateLocationSummary(Element element, ElementSummaryDto summary)
    {
        if (element.Location is LocationPoint locationPoint)
        {
            summary.LocationPoint = ToAxisVector(locationPoint.Point);
            return;
        }

        if (element.Location is LocationCurve locationCurve)
        {
            var curve = locationCurve.Curve;
            if (curve == null)
            {
                return;
            }

            summary.LocationCurveStart = ToAxisVector(curve.GetEndPoint(0));
            summary.LocationCurveEnd = ToAxisVector(curve.GetEndPoint(1));
            summary.LocationCurveLength = curve.Length;

            try
            {
                summary.LocationPoint = ToAxisVector(curve.Evaluate(0.5, true));
            }
            catch
            {
                // ignore if curve evaluation is unsupported
            }
        }
    }

    private static AxisVectorDto ToAxisVector(XYZ point)
    {
        return new AxisVectorDto
        {
            X = point.X,
            Y = point.Y,
            Z = point.Z
        };
    }

    private static string ResolveFamilyPlacementType(Element element, ElementType? type)
    {
        if (type is FamilySymbol symbol)
        {
            return symbol.Family?.FamilyPlacementType.ToString() ?? string.Empty;
        }

        if (element is FamilyInstance instance)
        {
            return instance.Symbol?.Family?.FamilyPlacementType.ToString() ?? string.Empty;
        }

        return string.Empty;
    }
}
