using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class ScheduleExtractionService
{
    internal ScheduleExtractionResponse Extract(PlatformServices services, Document doc, ScheduleExtractionRequest request)
    {
        request ??= new ScheduleExtractionRequest();
        var schedule = ResolveSchedule(doc, request);
        var response = new ScheduleExtractionResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            ScheduleId = checked((int)schedule.Id.Value),
            ScheduleName = schedule.Name ?? string.Empty,
            CategoryName = Category.GetCategory(doc, schedule.Definition.CategoryId)?.Name ?? string.Empty,
            IsItemized = schedule.Definition.IsItemized
        };

        var tableData = schedule.GetTableData();
        var body = tableData.GetSectionData(SectionType.Body);
        var header = tableData.GetSectionData(SectionType.Header);
        var columnCount = body.NumberOfColumns;
        var totalBodyRows = Math.Max(0, body.NumberOfRows);

        var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int col = 0; col < columnCount; col++)
        {
            var heading = ReadColumnHeading(schedule, header, body, col);
            var key = CreateColumnKey(heading, col, usedKeys);
            response.Columns.Add(new ScheduleColumnInfo
            {
                Index = col,
                Key = key,
                Heading = heading
            });
        }

        response.ColumnCount = response.Columns.Count;
        response.TotalRowCount = Math.Max(0, totalBodyRows - 1);

        for (int row = 1; row < totalBodyRows && response.Rows.Count < Math.Max(1, request.MaxRows); row++)
        {
            var rowDto = new ScheduleExtractionRow { RowIndex = row };
            var hasValue = false;
            for (int col = 0; col < response.Columns.Count; col++)
            {
                var text = ReadCellText(schedule, SectionType.Body, row, col);
                rowDto.Cells[response.Columns[col].Key] = text;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    hasValue = true;
                }
            }

            if (hasValue || request.IncludeEmptyRows)
            {
                response.Rows.Add(rowDto);
            }
        }

        response.ReturnedRowCount = response.Rows.Count;
        response.GroupingSummary = schedule.Definition.IsItemized ? "itemized" : "grouped/non-itemized";
        response.Totals["count"] = response.TotalRowCount.ToString(CultureInfo.InvariantCulture);
        response.Summary = string.Format(
            CultureInfo.InvariantCulture,
            "Schedule `{0}` có {1} cột, {2} dòng body, trả về {3} dòng structured.",
            response.ScheduleName,
            response.ColumnCount,
            response.TotalRowCount,
            response.ReturnedRowCount);
        return response;
    }

    private static ViewSchedule ResolveSchedule(Document doc, ScheduleExtractionRequest request)
    {
        ViewSchedule? schedule = null;
        if (request.ScheduleId > 0)
        {
            schedule = doc.GetElement(new ElementId((long)request.ScheduleId)) as ViewSchedule;
        }

        if (schedule == null && !string.IsNullOrWhiteSpace(request.ScheduleName))
        {
            schedule = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(x => string.Equals(x.Name, request.ScheduleName, StringComparison.OrdinalIgnoreCase));
        }

        if (schedule == null)
        {
            throw new InvalidOperationException("Schedule not found for structured extraction.");
        }

        return schedule;
    }

    private static string ReadColumnHeading(ViewSchedule schedule, TableSectionData header, TableSectionData body, int column)
    {
        var heading = ReadCellText(schedule, SectionType.Body, 0, column);
        if (!string.IsNullOrWhiteSpace(heading))
        {
            return heading;
        }

        if (header != null && header.NumberOfRows > 0)
        {
            heading = ReadCellText(schedule, SectionType.Header, header.NumberOfRows - 1, column);
        }

        return string.IsNullOrWhiteSpace(heading) ? "Column" + column.ToString(CultureInfo.InvariantCulture) : heading.Trim();
    }

    private static string ReadCellText(ViewSchedule schedule, SectionType sectionType, int row, int column)
    {
        try
        {
            return schedule.GetCellText(sectionType, row, column) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string CreateColumnKey(string heading, int column, ISet<string> usedKeys)
    {
        var baseKey = string.IsNullOrWhiteSpace(heading)
            ? "column_" + column.ToString(CultureInfo.InvariantCulture)
            : new string(heading.Trim().Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '_').ToArray()).Trim('_');

        if (string.IsNullOrWhiteSpace(baseKey))
        {
            baseKey = "column_" + column.ToString(CultureInfo.InvariantCulture);
        }

        var key = baseKey;
        var suffix = 2;
        while (usedKeys.Contains(key))
        {
            key = baseKey + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            suffix++;
        }

        usedKeys.Add(key);
        return key;
    }
}
