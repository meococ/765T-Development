using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class SheetIntelligenceService
{
    private readonly ScheduleExtractionService _scheduleExtraction;

    internal SheetIntelligenceService(ScheduleExtractionService scheduleExtraction)
    {
        _scheduleExtraction = scheduleExtraction;
    }

    internal SheetCaptureIntelligenceResponse Capture(PlatformServices services, Document doc, SheetCaptureIntelligenceRequest request)
    {
        request ??= new SheetCaptureIntelligenceRequest();
        var sheet = services.ResolveSheet(doc, new SheetSummaryRequest
        {
            SheetId = request.SheetId,
            SheetNumber = request.SheetNumber
        });

        var response = new SheetCaptureIntelligenceResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            SheetId = checked((int)sheet.Id.Value),
            SheetNumber = sheet.SheetNumber ?? string.Empty,
            SheetName = sheet.Name ?? string.Empty,
            CurrentRevision = sheet.LookupParameter("Current Revision")?.AsValueString() ?? string.Empty
        };

        PopulateTitleBlockInfo(doc, sheet, response);
        PopulateSheetNotes(doc, sheet, response, request);
        PopulateViewports(doc, sheet, response, request);
        PopulateSchedules(services, doc, sheet, response, request);
        response.AnnotationCounts = BuildAnnotationCounts(response);
        response.LayoutMap = BuildLayoutMap(response);

        if (request.WriteArtifacts)
        {
            WriteArtifacts(response);
        }

        response.Summary = string.Format(
            CultureInfo.InvariantCulture,
            "Sheet `{0}` has {1} viewport(s), {2} schedule(s), {3} sheet note(s), and {4} title block parameter(s).",
            response.SheetNumber,
            response.Viewports.Count,
            response.Schedules.Count,
            response.SheetTextNotes.Count,
            response.TitleBlockParameters.Count);
        return response;
    }

    private static void PopulateTitleBlockInfo(Document doc, ViewSheet sheet, SheetCaptureIntelligenceResponse response)
    {
        var titleBlock = new FilteredElementCollector(doc, sheet.Id)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsNotElementType()
            .FirstElement();

        if (titleBlock == null)
        {
            response.TitleBlockName = string.Empty;
            return;
        }

        response.TitleBlockName = titleBlock.Name ?? string.Empty;
        response.TitleBlockParameters = titleBlock.Parameters
            .Cast<Parameter>()
            .Select(x => new SheetTitleBlockParameterInfo
            {
                Name = x.Definition?.Name ?? string.Empty,
                Value = ReadParameterValue(x)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Value))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(30)
            .ToList();
    }

    private static void PopulateSheetNotes(Document doc, ViewSheet sheet, SheetCaptureIntelligenceResponse response, SheetCaptureIntelligenceRequest request)
    {
        response.SheetTextNotes = new FilteredElementCollector(doc, sheet.Id)
            .OfClass(typeof(TextNote))
            .Cast<TextNote>()
            .OrderBy(x => x.Coord.Y)
            .ThenBy(x => x.Coord.X)
            .Take(Math.Max(1, request.MaxSheetTextNotes))
            .Select(x => new SheetTextNoteIntelligence
            {
                OwnerViewId = checked((int)sheet.Id.Value),
                OwnerViewName = sheet.Name ?? string.Empty,
                Text = x.Text ?? string.Empty,
                X = Math.Round(x.Coord.X, 4),
                Y = Math.Round(x.Coord.Y, 4)
            })
            .ToList();
    }

    private static void PopulateViewports(Document doc, ViewSheet sheet, SheetCaptureIntelligenceResponse response, SheetCaptureIntelligenceRequest request)
    {
        if (!request.IncludeViewportDetails)
        {
            return;
        }

        foreach (var viewportId in sheet.GetAllViewports().Take(Math.Max(1, request.MaxViewports)))
        {
            if (!(doc.GetElement(viewportId) is Viewport viewport))
            {
                continue;
            }

            var view = doc.GetElement(viewport.ViewId) as View;
            if (view == null)
            {
                continue;
            }

            var center = viewport.GetBoxCenter();
            var outline = viewport.GetBoxOutline();
            var width = outline.MaximumPoint.X - outline.MinimumPoint.X;
            var height = outline.MaximumPoint.Y - outline.MinimumPoint.Y;

            var viewportInfo = new SheetViewportIntelligence
            {
                ViewportId = checked((int)viewport.Id.Value),
                ViewId = checked((int)view.Id.Value),
                ViewName = view.Name ?? string.Empty,
                ViewType = view.ViewType.ToString(),
                Scale = view.Scale,
                CenterX = Math.Round(center.X, 4),
                CenterY = Math.Round(center.Y, 4),
                Width = Math.Round(width, 4),
                Height = Math.Round(height, 4)
            };

            try
            {
                viewportInfo.VisibleElementCount = new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .GetElementCount();
                viewportInfo.TextNoteCount = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .WhereElementIsNotElementType()
                    .GetElementCount();
                viewportInfo.TagCount = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(IndependentTag))
                    .WhereElementIsNotElementType()
                    .GetElementCount();
                viewportInfo.DimensionCount = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(Dimension))
                    .WhereElementIsNotElementType()
                    .GetElementCount();
                viewportInfo.TextPreview = new FilteredElementCollector(doc, view.Id)
                    .OfClass(typeof(TextNote))
                    .WhereElementIsNotElementType()
                    .Cast<TextNote>()
                    .Select(x => x.Text ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Take(Math.Max(1, request.MaxViewportTextNotes))
                    .ToList();
            }
            catch
            {
                viewportInfo.TextPreview = new List<string>();
            }

            response.Viewports.Add(viewportInfo);
        }
    }

    private void PopulateSchedules(PlatformServices services, Document doc, ViewSheet sheet, SheetCaptureIntelligenceResponse response, SheetCaptureIntelligenceRequest request)
    {
        var scheduleInstances = new FilteredElementCollector(doc, sheet.Id)
            .OfClass(typeof(ScheduleSheetInstance))
            .Cast<ScheduleSheetInstance>()
            .Take(Math.Max(1, request.MaxSchedules))
            .ToList();

        foreach (var instance in scheduleInstances)
        {
            var scheduleView = doc.GetElement(instance.ScheduleId) as ViewSchedule;
            if (scheduleView == null)
            {
                continue;
            }

            var box = instance.get_BoundingBox(sheet);
            var scheduleInfo = new SheetScheduleIntelligence
            {
                ScheduleInstanceId = checked((int)instance.Id.Value),
                ScheduleViewId = checked((int)scheduleView.Id.Value),
                ScheduleName = scheduleView.Name ?? string.Empty,
                CenterX = box != null ? Math.Round((box.Min.X + box.Max.X) / 2.0, 4) : 0,
                CenterY = box != null ? Math.Round((box.Min.Y + box.Max.Y) / 2.0, 4) : 0,
                Width = box != null ? Math.Round(box.Max.X - box.Min.X, 4) : 0,
                Height = box != null ? Math.Round(box.Max.Y - box.Min.Y, 4) : 0
            };

            if (request.IncludeScheduleData)
            {
                try
                {
                    var extracted = _scheduleExtraction.Extract(services, doc, new ScheduleExtractionRequest
                    {
                        ScheduleId = scheduleInfo.ScheduleViewId,
                        MaxRows = 1,
                        IncludeColumnMetadata = true
                    });

                    scheduleInfo.RowCount = extracted.TotalRowCount;
                    scheduleInfo.ColumnCount = extracted.ColumnCount;
                    scheduleInfo.Headers = extracted.Columns.Select(x => x.Heading).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                }
                catch
                {
                    scheduleInfo.Headers = new List<string>();
                }
            }

            response.Schedules.Add(scheduleInfo);
        }
    }

    private static List<CountByNameDto> BuildAnnotationCounts(SheetCaptureIntelligenceResponse response)
    {
        return new List<CountByNameDto>
        {
            new CountByNameDto { Name = "sheet_text_notes", Count = response.SheetTextNotes.Count },
            new CountByNameDto { Name = "viewports", Count = response.Viewports.Count },
            new CountByNameDto { Name = "schedules", Count = response.Schedules.Count },
            new CountByNameDto { Name = "viewport_text_notes", Count = response.Viewports.Sum(x => x.TextNoteCount) },
            new CountByNameDto { Name = "viewport_tags", Count = response.Viewports.Sum(x => x.TagCount) },
            new CountByNameDto { Name = "viewport_dimensions", Count = response.Viewports.Sum(x => x.DimensionCount) }
        };
    }

    private static string BuildLayoutMap(SheetCaptureIntelligenceResponse response)
    {
        var lines = new List<string>
        {
            string.Format(CultureInfo.InvariantCulture, "+--[{0}: {1}]--+", response.SheetNumber, response.SheetName)
        };

        foreach (var viewport in response.Viewports.OrderByDescending(x => x.CenterY).ThenBy(x => x.CenterX))
        {
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "| VP {0} [{1}] @ ({2:0.##},{3:0.##}) size=({4:0.##}x{5:0.##}) notes={6} tags={7} dims={8}",
                viewport.ViewName,
                viewport.Scale > 0 ? "1:" + viewport.Scale.ToString(CultureInfo.InvariantCulture) : viewport.ViewType,
                viewport.CenterX,
                viewport.CenterY,
                viewport.Width,
                viewport.Height,
                viewport.TextNoteCount,
                viewport.TagCount,
                viewport.DimensionCount));
        }

        foreach (var schedule in response.Schedules.OrderByDescending(x => x.CenterY).ThenBy(x => x.CenterX))
        {
            lines.Add(string.Format(
                CultureInfo.InvariantCulture,
                "| SCH {0} [{1}x{2}] @ ({3:0.##},{4:0.##})",
                schedule.ScheduleName,
                schedule.RowCount,
                schedule.ColumnCount,
                schedule.CenterX,
                schedule.CenterY));
        }

        lines.Add("+----------------------------+");
        return string.Join(Environment.NewLine, lines);
    }

    private static void WriteArtifacts(SheetCaptureIntelligenceResponse response)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var root = Path.Combine(appData, BridgeConstants.AppDataFolderName, "artifacts", "sheet-intelligence");
        Directory.CreateDirectory(root);

        var stem = SanitizeFileName(response.DocumentKey + "_" + response.SheetNumber);
        var txtPath = Path.Combine(root, stem + ".layout.txt");
        var jsonPath = Path.Combine(root, stem + ".json");

        File.WriteAllText(txtPath, response.LayoutMap);
        response.Artifacts.Add(new SheetArtifactReference
        {
            ArtifactType = "layout_map",
            Path = txtPath,
            Description = "ASCII-like layout map for sheet composition."
        });

        response.Artifacts.Add(new SheetArtifactReference
        {
            ArtifactType = "structured_json",
            Path = jsonPath,
            Description = "Structured sheet intelligence payload."
        });
        File.WriteAllText(jsonPath, JsonUtil.Serialize(response));
    }

    private static string ReadParameterValue(Parameter parameter)
    {
        if (parameter == null)
        {
            return string.Empty;
        }

        try
        {
            return parameter.AsValueString()
                   ?? parameter.AsString()
                   ?? parameter.AsInteger().ToString(CultureInfo.InvariantCulture);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((value ?? string.Empty)
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray());
    }
}
