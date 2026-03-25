using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// Data export/import — xuất dữ liệu parameter từ Revit ra JSON/CSV, đọc schedule data.
/// Đây là killer feature: AI đọc data → phân tích → đề xuất fix → batch update.
/// </summary>
internal sealed class DataExportService
{
    // ── Export elements + parameters → JSON/CSV ──
    internal DataExportResult ExportData(PlatformServices services, Document doc, DataExportRequest request)
    {
        request ??= new DataExportRequest();
        var result = new DataExportResult
        {
            DocumentKey = services.GetDocumentKey(doc),
            Format = request.Format
        };

        var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
        var elements = new List<Element>();

        if (request.CategoryNames.Count > 0)
        {
            // PERF: Resolve category names → BuiltInCategory, dùng quick filter thay vì iterate + string compare
            var catIds = new List<BuiltInCategory>();
            foreach (var name in request.CategoryNames)
            {
                foreach (Category cat in doc.Settings.Categories)
                {
                    if (string.Equals(cat.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        catIds.Add((BuiltInCategory)cat.Id.Value);
                        break;
                    }
                }
            }
            if (catIds.Count == 1) collector.OfCategory(catIds[0]);
            else if (catIds.Count > 1) collector.WherePasses(new ElementMulticategoryFilter(catIds));

            elements = collector.Take(request.MaxResults).ToList();
        }
        else
        {
            elements = collector.Take(request.MaxResults).ToList();
        }

        // Filter by parameter value nếu có
        if (!string.IsNullOrWhiteSpace(request.FilterParameterName) && !string.IsNullOrWhiteSpace(request.FilterValue))
        {
            elements = elements.Where(e =>
            {
                var p = e.LookupParameter(request.FilterParameterName);
                return p != null && (p.AsValueString() ?? p.AsString() ?? string.Empty)
                    .IndexOf(request.FilterValue, StringComparison.OrdinalIgnoreCase) >= 0;
            }).ToList();
        }

        // Xác định parameter names cần export
        var paramNames = request.ParameterNames.Count > 0
            ? request.ParameterNames
            : AutoDetectParameterNames(elements.Take(10).ToList());

        foreach (var elem in elements)
        {
            var item = new DataExportItem
            {
                ElementId = checked((int)elem.Id.Value),
                Category = elem.Category?.Name ?? string.Empty,
                FamilyName = (elem is FamilyInstance fi) ? (fi.Symbol?.Family?.Name ?? string.Empty) : string.Empty,
                TypeName = doc.GetElement(elem.GetTypeId())?.Name ?? string.Empty
            };

            foreach (var pName in paramNames)
            {
                var param = elem.LookupParameter(pName);
                var value = param != null ? (param.AsValueString() ?? param.AsString() ?? string.Empty) : string.Empty;
                item.Parameters[pName] = value;
            }
            result.Items.Add(item);
        }

        result.Count = result.Items.Count;

        // Write to file nếu có outputPath
        if (!string.IsNullOrWhiteSpace(request.OutputPath))
        {
            // SEC-FIX: Validate output path
            if (!ValidateFilePath(request.OutputPath, out var sanitizedOutput, out var outputError))
            {
                throw new InvalidOperationException(outputError);
            }

            var dir = Path.GetDirectoryName(sanitizedOutput);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (string.Equals(request.Format, "csv", StringComparison.OrdinalIgnoreCase))
                WriteCsv(sanitizedOutput, paramNames, result.Items);
            else
                WriteJson(sanitizedOutput, result);

            result.OutputPath = sanitizedOutput;
        }

        return result;
    }

    // ── Export schedule table data ──
    internal ScheduleExportResult ExportScheduleData(PlatformServices services, Document doc, ExportScheduleRequest request)
    {
        var result = new ScheduleExportResult { DocumentKey = services.GetDocumentKey(doc) };

        ViewSchedule? schedule = null;
        if (request.ScheduleId > 0)
            schedule = doc.GetElement(new ElementId((long)request.ScheduleId)) as ViewSchedule;
        if (schedule == null && !string.IsNullOrWhiteSpace(request.ScheduleName))
            schedule = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .FirstOrDefault(s => string.Equals(s.Name, request.ScheduleName, StringComparison.OrdinalIgnoreCase));
        if (schedule == null)
            throw new InvalidOperationException($"Schedule not found: Id={request.ScheduleId}, Name='{request.ScheduleName}'");

        result.ScheduleName = schedule.Name ?? string.Empty;

        var tableData = schedule.GetTableData();
        var body = tableData.GetSectionData(SectionType.Body);
        var header = tableData.GetSectionData(SectionType.Header);

        // Column headers
        var columnCount = body.NumberOfColumns;
        for (int col = 0; col < columnCount; col++)
        {
            try
            {
                var cellText = schedule.GetCellText(SectionType.Body, 0, col);
                result.ColumnHeaders.Add(cellText ?? $"Column{col}");
            }
            catch
            {
                result.ColumnHeaders.Add($"Column{col}");
            }
        }

        // Data rows (skip header row)
        var rowCount = body.NumberOfRows;
        for (int row = 1; row < rowCount; row++)
        {
            var rowData = new List<string>();
            for (int col = 0; col < columnCount; col++)
            {
                try
                {
                    rowData.Add(schedule.GetCellText(SectionType.Body, row, col) ?? string.Empty);
                }
                catch
                {
                    rowData.Add(string.Empty);
                }
            }
            result.Rows.Add(rowData);
        }

        result.RowCount = result.Rows.Count;
        return result;
    }

    // ── Shared parameter listing ──
    internal SharedParameterListResponse ListSharedParameters(PlatformServices services, Document doc, SharedParameterListRequest request)
    {
        request ??= new SharedParameterListRequest();
        var result = new SharedParameterListResponse { DocumentKey = services.GetDocumentKey(doc) };

        var bindings = doc.ParameterBindings;
        var iterator = bindings.ForwardIterator();

        while (iterator.MoveNext())
        {
            var definition = iterator.Key;
            if (definition == null) continue;

            var name = definition.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(request.NameContains) &&
                name.IndexOf(request.NameContains, StringComparison.OrdinalIgnoreCase) < 0) continue;

            var binding = bindings.get_Item(definition);
            var categories = new List<string>();
            var isInstance = binding is InstanceBinding;
            var bindingType = isInstance ? "Instance" : "Type";

            if (binding is ElementBinding elemBinding)
            {
                foreach (Category cat in elemBinding.Categories)
                    categories.Add(cat.Name ?? string.Empty);
            }

            var groupName = definition.GetGroupTypeId()?.ToString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(request.GroupNameContains) &&
                groupName.IndexOf(request.GroupNameContains, StringComparison.OrdinalIgnoreCase) < 0) continue;

            result.Parameters.Add(new SharedParameterItem
            {
                Name = name,
                GroupName = groupName,
                ParameterType = definition.GetGroupTypeId()?.ToString() ?? string.Empty,
                BindingType = bindingType,
                BoundCategories = categories,
                IsInstance = isInstance
            });

            if (result.Parameters.Count >= request.MaxResults) break;
        }

        result.Count = result.Parameters.Count;
        return result;
    }

    // ── Data Import Preview (read-only preview) ──
    internal DataImportPreviewResult PreviewImport(PlatformServices services, Document doc, DataImportPreviewRequest request)
    {
        var result = new DataImportPreviewResult { DocumentKey = services.GetDocumentKey(doc) };

        // SEC-FIX: Validate và normalize file path để chống path traversal.
        if (!ValidateFilePath(request.InputPath, out var sanitizedPath, out var pathError))
        {
            result.Warnings.Add(pathError);
            return result;
        }

        if (!File.Exists(sanitizedPath))
        {
            result.Warnings.Add($"File not found: {sanitizedPath}");
            return result;
        }

        // Read raw lines count
        var lines = File.ReadAllLines(sanitizedPath);
        result.TotalRows = Math.Max(0, lines.Length - 1); // exclude header
        result.Warnings.Add($"Preview of {sanitizedPath}: {result.TotalRows} data rows found.");

        // Detect parameter names from header
        if (lines.Length > 0)
        {
            var headers = lines[0].Split(',');
            result.ParameterNames = headers.Select(h => h.Trim().Trim('"')).ToList();
        }

        return result;
    }

    // ── Private helpers ──
    private static List<string> AutoDetectParameterNames(List<Element> sampleElements)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var elem in sampleElements)
        {
            foreach (Parameter p in elem.Parameters)
            {
                if (!p.IsReadOnly && p.HasValue && !string.IsNullOrWhiteSpace(p.Definition?.Name))
                    names.Add(p.Definition!.Name);
            }
        }
        return names.OrderBy(n => n).Take(30).ToList();
    }

    private static void WriteCsv(string path, List<string> paramNames, List<DataExportItem> items)
    {
        using var writer = new StreamWriter(path, false, System.Text.Encoding.UTF8);
        var headers = new List<string> { "ElementId", "Category", "FamilyName", "TypeName" };
        headers.AddRange(paramNames);
        writer.WriteLine(string.Join(",", headers.Select(h => $"\"{h}\"")));

        foreach (var item in items)
        {
            var cells = new List<string>
            {
                item.ElementId.ToString(CultureInfo.InvariantCulture),
                $"\"{item.Category}\"",
                $"\"{item.FamilyName}\"",
                $"\"{item.TypeName}\""
            };
            foreach (var pName in paramNames)
            {
                item.Parameters.TryGetValue(pName, out var val);
                cells.Add($"\"{val ?? string.Empty}\"");
            }
            writer.WriteLine(string.Join(",", cells));
        }
    }

    private static void WriteJson(string path, DataExportResult result)
    {
        var json = BIM765T.Revit.Contracts.Serialization.JsonUtil.Serialize(result);
        File.WriteAllText(path, json, System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// SEC-FIX: Validate và sanitize file path để chống path traversal attack.
    /// - Phải là absolute path
    /// - Không cho phép ".." component
    /// - Không cho phép system directories (Windows, Program Files)
    /// - Normalize để loại bỏ các tricks như "C:\data\..\Windows\system32"
    /// </summary>
    internal static bool ValidateFilePath(string? rawPath, out string sanitizedPath, out string error)
    {
        sanitizedPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            error = "File path không được rỗng.";
            return false;
        }

        if (!Path.IsPathRooted(rawPath))
        {
            error = $"File path phải là absolute path: {rawPath}";
            return false;
        }

        // Normalize path (resolve ".." và "." segments)
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath);
        }
        catch (Exception ex)
        {
            error = $"File path không hợp lệ: {ex.Message}";
            return false;
        }

        string normalizedPath;
        try
        {
            normalizedPath = NormalizePathForComparison(fullPath);
        }
        catch (Exception ex)
        {
            error = $"File path kh?ng h?p l? sau normalize: {ex.Message}";
            return false;
        }

        foreach (var blockedRoot in GetBlockedRoots(fullPath))
        {
            if (IsPathUnderRoot(normalizedPath, blockedRoot))
            {
                error = $"File path kh?ng ???c tr? v?o system directory: {fullPath}";
                return false;
            }
        }

        try
        {
            var driveRoot = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrWhiteSpace(driveRoot))
            {
                var drive = new DriveInfo(driveRoot);
                if (drive.DriveType == DriveType.Removable)
                {
                    error = $"File path kh?ng ???c tr? v?o removable drive: {fullPath}";
                    return false;
                }
            }
        }
        catch
        {
        }

        sanitizedPath = fullPath;
        return true;
    }

    private static IEnumerable<string> GetBlockedRoots(string fullPath)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var normalizedPath = NormalizePathForComparison(path!);
                roots.Add(normalizedPath);
            }
            catch
            {
            }
        }

        AddDirectory(Environment.SystemDirectory);
        AddDirectory(Path.GetDirectoryName(Environment.SystemDirectory));
        AddDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddDirectory(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));

        var driveRoot = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrWhiteSpace(driveRoot))
        {
            AddDirectory(Path.Combine(driveRoot, "$RECYCLE.BIN"));
            AddDirectory(Path.Combine(driveRoot, "System Volume Information"));
            AddDirectory(Path.Combine(driveRoot, "Recovery"));
        }

        return roots;
    }

    private static bool IsPathUnderRoot(string normalizedPath, string normalizedRoot)
    {
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathForComparison(string path)
    {
        var normalized = Path.GetFullPath(path).Normalize(NormalizationForm.FormKC);
        return normalized.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || normalized.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? normalized
            : normalized + Path.DirectorySeparatorChar;
    }
}
