using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Context;

namespace BIM765T.Revit.Agent.Services.Context;

internal sealed class CurrentContextService
{
    internal CurrentContextDto Read(UIApplication uiapp)
    {
        var uidoc = uiapp.ActiveUIDocument;
        if (uidoc == null)
        {
            return new CurrentContextDto
            {
                DocumentName = "<none>",
                ViewName = "<none>",
                ViewType = "<none>",
                LevelMode = "NO_ACTIVE_UIDOC",
                Confidence = "LOW"
            };
        }

        var doc = uidoc.Document;
        var view = doc.ActiveView;
        var dto = new CurrentContextDto
        {
            DocumentName = doc.Title,
            ViewName = view?.Name ?? "<none>",
            ViewType = view?.ViewType.ToString() ?? "<none>",
            SelectedElementIds = uidoc.Selection.GetElementIds().Select(x => checked((int)x.Value)).ToList(),
            LevelMode = "UNKNOWN",
            Confidence = "LOW"
        };

        var viewLevel = view?.GenLevel;
        if (viewLevel != null)
        {
            dto.LevelName = viewLevel.Name;
            dto.LevelId = checked((int)viewLevel.Id.Value);
            dto.LevelElevation = viewLevel.Elevation;
            dto.LevelMode = "ACTIVE_VIEW_GENLEVEL";
            dto.Confidence = "EXACT";
            return dto;
        }

        var selectedLevel = uidoc.Selection.GetElementIds()
            .Select(id => doc.GetElement(id))
            .FirstOrDefault(e => e != null && e.LevelId != ElementId.InvalidElementId);

        if (selectedLevel != null)
        {
            var level = doc.GetElement(selectedLevel.LevelId) as Level;
            if (level != null)
            {
                dto.LevelName = level.Name;
                dto.LevelId = checked((int)level.Id.Value);
                dto.LevelElevation = level.Elevation;
                dto.LevelMode = "SELECTION_LEVEL";
                dto.Confidence = "HIGH";
                dto.Notes.Add("Suy luận level từ phần tử đang được chọn.");
                return dto;
            }
        }

        if (view is View3D v3 && !v3.IsTemplate)
        {
            var eyeZ = v3.GetOrientation().EyePosition.Z;
            var nearest = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => Math.Abs(x.Elevation - eyeZ))
                .FirstOrDefault();

            dto.CameraZ = eyeZ;
            if (nearest != null)
            {
                dto.LevelName = nearest.Name;
                dto.LevelId = checked((int)nearest.Id.Value);
                dto.LevelElevation = nearest.Elevation;
                dto.LevelMode = "VIEW3D_EYE_Z_NEAREST_LEVEL";
                dto.Confidence = "MEDIUM";
                dto.Notes.Add("Level ở 3D view là suy luận theo EyePosition.Z.");
            }
        }

        return dto;
    }
}
