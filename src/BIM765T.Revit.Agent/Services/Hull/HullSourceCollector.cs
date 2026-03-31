using System.Collections.Generic;
using Autodesk.Revit.DB;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Hull;

namespace BIM765T.Revit.Agent.Services.Hull;

internal sealed class HullSourceCollector
{
    internal HullCollectionResponse Collect(Document doc)
    {
        var view = doc.ActiveView;
        var response = new HullCollectionResponse
        {
            DocumentName = doc.Title,
            ViewName = view?.Name ?? "<none>"
        };

        if (view == null)
        {
            return response;
        }

        var bicIds = new List<ElementId>
        {
            new ElementId(BuiltInCategory.OST_Walls),
            new ElementId(BuiltInCategory.OST_Floors),
            new ElementId(BuiltInCategory.OST_Ceilings)
        };
        var filter = new ElementMulticategoryFilter(bicIds);
        var elements = new FilteredElementCollector(doc, view.Id)
            .WherePasses(filter)
            .WhereElementIsNotElementType()
            .ToElements();

        response.TotalScanned = elements.Count;
        foreach (var element in elements)
        {
            var info = BuildSourceInfo(doc, element);
            if (info.Eligible)
            {
                response.EligibleCount += 1;
            }
            response.Sources.Add(info);
        }

        return response;
    }

    private static HullSourceInfo BuildSourceInfo(Document doc, Element element)
    {
        var info = new HullSourceInfo
        {
            SourceId = checked((int)element.Id.Value),
            UniqueId = element.UniqueId,
            SourceKind = ResolveSourceKind(element),
            TypeName = doc.GetElement(element.GetTypeId())?.Name ?? string.Empty,
            FamilyName = ResolveFamilyName(doc, element),
            LevelId = ResolveLevelId(element),
            LevelName = ResolveLevelName(doc, element),
            CassetteId = ReadParam(element, "Mii_CassetteID"),
            PodId = ReadParam(element, "Mii_PodID"),
            Comments = ReadParam(element, "Comments"),
            Eligible = false
        };

        if (element is Wall wall && wall.WallType.Kind == WallKind.Curtain)
        {
            info.Diagnostics.Add(DiagnosticRecord.Create("SKIP_CURTAIN_WALL", DiagnosticSeverity.Info, "Curtain wall excluded from Hull flow.", info.SourceId));
            return info;
        }

        if (TryGetStructureThicknessInches(doc, element, out var thickness))
        {
            info.StructureThicknessInch = thickness;
            info.Eligible = true;
        }
        else
        {
            info.Diagnostics.Add(DiagnosticRecord.Create("MISSING_STRUCTURE_LAYER_1", DiagnosticSeverity.Warning, "Valid Structure[1] layer not found.", info.SourceId));
        }

        return info;
    }

    private static bool TryGetStructureThicknessInches(Document doc, Element element, out double thicknessInch)
    {
        thicknessInch = 0.0;
        var compound = GetCompoundStructure(doc, element);
        var layers = compound?.GetLayers();
        if (layers == null || layers.Count <= 1)
        {
            return false;
        }

        var width = layers[1].Width;
        if (width <= 0.0)
        {
            return false;
        }

        thicknessInch = UnitUtils.ConvertFromInternalUnits(width, UnitTypeId.Inches);
        return true;
    }

    private static CompoundStructure? GetCompoundStructure(Document doc, Element element)
    {
        if (element is Wall wall)
        {
            return wall.WallType?.GetCompoundStructure();
        }

        if (element is Floor floor)
        {
            var type = doc.GetElement(floor.GetTypeId()) as FloorType;
            return type?.GetCompoundStructure();
        }

        if (element is Ceiling ceiling)
        {
            var type = doc.GetElement(ceiling.GetTypeId()) as CeilingType;
            return type?.GetCompoundStructure();
        }

        return null;
    }

    private static string ResolveSourceKind(Element element)
    {
        if (element is Wall) return "Wall";
        if (element is Floor) return "Floor";
        if (element is Ceiling) return "Ceiling";
        return element.Category?.Name ?? element.GetType().Name;
    }

    private static string ResolveFamilyName(Document doc, Element element)
    {
        var type = doc.GetElement(element.GetTypeId()) as ElementType;
        return type?.FamilyName ?? string.Empty;
    }

    private static int? ResolveLevelId(Element element)
    {
        return element.LevelId != ElementId.InvalidElementId ? checked((int)element.LevelId.Value) : (int?)null;
    }

    private static string? ResolveLevelName(Document doc, Element element)
    {
        if (element.LevelId == ElementId.InvalidElementId)
        {
            return null;
        }

        return (doc.GetElement(element.LevelId) as Level)?.Name;
    }

    private static string? ReadParam(Element element, string name)
    {
        var p = element.LookupParameter(name);
        if (p == null || !p.HasValue)
        {
            return null;
        }

        return p.AsString() ?? p.AsValueString();
    }
}
