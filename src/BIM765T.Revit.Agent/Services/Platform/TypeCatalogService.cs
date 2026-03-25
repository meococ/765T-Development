using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class TypeCatalogService
{
    private readonly DocumentCacheService _cache;

    internal TypeCatalogService(DocumentCacheService cache)
    {
        _cache = cache;
    }

    internal ElementTypeCatalogResponse ListElementTypes(PlatformServices services, Document doc, ElementTypeQueryRequest? request)
    {
        request ??= new ElementTypeQueryRequest();
        var categoryNames = request.CategoryNames ?? new List<string>();
        var usageMap = _cache.GetOrAdd(doc, "type-usage:generic", () => BuildGenericUsageMap(doc));

        IEnumerable<ElementType> types = new FilteredElementCollector(doc)
            .WhereElementIsElementType()
            .ToElements()
            .OfType<ElementType>();

        if (!string.IsNullOrWhiteSpace(request.ClassName))
        {
            types = types.Where(x => string.Equals(x.GetType().Name, request.ClassName, StringComparison.OrdinalIgnoreCase));
        }

        if (categoryNames.Count > 0)
        {
            types = types.Where(x => x.Category != null && categoryNames.Any(c => string.Equals(c, x.Category.Name, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(request.NameContains))
        {
            types = types.Where(x => (x.Name ?? string.Empty).IndexOf(request.NameContains, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        var result = new ElementTypeCatalogResponse
        {
            DocumentKey = services.GetDocumentKey(doc)
        };

        foreach (var type in types
                     .OrderBy(x => x.Category?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.FamilyName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                     .Take(Math.Max(1, request.MaxResults)))
        {
            var typeId = checked((int)type.Id.Value);
            var usageCount = usageMap.TryGetValue(typeId, out var count) ? count : 0;
            if (request.OnlyInUse && usageCount <= 0)
            {
                continue;
            }

            result.Items.Add(BuildTypeSummary(services, doc, type, usageCount, request.IncludeParameters));
        }

        result.Count = result.Items.Count;
        return result;
    }

    internal ElementTypeCatalogResponse ListTextNoteTypes(PlatformServices services, Document doc, ElementTypeQueryRequest? request)
    {
        request ??= new ElementTypeQueryRequest();
        request.ClassName = nameof(TextNoteType);
        return ListElementTypes(services, doc, request);
    }

    internal TextNoteTypeUsageResponse GetTextTypeUsage(PlatformServices services, Document doc, TextNoteTypeUsageRequest? request)
    {
        request ??= new TextNoteTypeUsageRequest();
        var usageMap = _cache.GetOrAdd(doc, $"type-usage:text:{Math.Max(1, request.MaxSampleTextNotesPerType)}", () => BuildTextNoteUsageMap(doc, request.MaxSampleTextNotesPerType));
        var result = new TextNoteTypeUsageResponse
        {
            DocumentKey = services.GetDocumentKey(doc)
        };

        IEnumerable<TextNoteType> types = new FilteredElementCollector(doc)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>();

        if (!string.IsNullOrWhiteSpace(request.NameContains))
        {
            types = types.Where(x => (x.Name ?? string.Empty).IndexOf(request.NameContains, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        foreach (var type in types
                     .OrderByDescending(x => usageMap.TryGetValue(checked((int)x.Id.Value), out var usage) ? usage.UsageCount : 0)
                     .ThenBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                     .Take(Math.Max(1, request.MaxResults)))
        {
            var typeId = checked((int)type.Id.Value);
            var usage = usageMap.TryGetValue(typeId, out var record) ? record : new TextTypeUsageRecord();
            if (request.OnlyInUse && usage.UsageCount <= 0)
            {
                continue;
            }

            result.Items.Add(new TextNoteTypeUsageDto
            {
                TypeId = typeId,
                TypeName = type.Name ?? string.Empty,
                FamilyName = type.FamilyName ?? string.Empty,
                UsageCount = usage.UsageCount,
                TextSize = type.LookupParameter("Text Size")?.AsValueString() ?? string.Empty,
                ColorValue = type.LookupParameter("Color")?.AsInteger() ?? 0,
                Font = type.LookupParameter("Text Font")?.AsString() ?? string.Empty,
                SampleTextNoteIds = usage.SampleTextNoteIds
            });
        }

        result.Count = result.Items.Count;
        return result;
    }

    private static ElementTypeSummaryDto BuildTypeSummary(PlatformServices services, Document doc, ElementType type, int usageCount, bool includeParameters)
    {
        var summary = new ElementTypeSummaryDto
        {
            TypeId = checked((int)type.Id.Value),
            UniqueId = type.UniqueId ?? string.Empty,
            DocumentKey = services.GetDocumentKey(doc),
            CategoryName = type.Category?.Name ?? string.Empty,
            ClassName = type.GetType().Name,
            TypeName = type.Name ?? string.Empty,
            FamilyName = type.FamilyName ?? string.Empty,
            UsageCount = usageCount,
            IsInUse = usageCount > 0
        };

        if (!includeParameters)
        {
            return summary;
        }

        foreach (Parameter parameter in type.Parameters)
        {
            summary.Parameters.Add(new ParameterValueDto
            {
                Name = parameter.Definition?.Name ?? string.Empty,
                StorageType = parameter.StorageType.ToString(),
                Value = PlatformServices.ParameterValue(parameter),
                IsReadOnly = parameter.IsReadOnly
            });
        }

        return summary;
    }

    private static Dictionary<int, int> BuildGenericUsageMap(Document doc)
    {
        // PERF: iterate collector trực tiếp qua IEnumerable<Element>, không materialize bằng ToElements()
        return new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .Select(x => x?.GetTypeId())
            .Where(id => id != null && id != ElementId.InvalidElementId)
            .GroupBy(id => checked((int)id!.Value))
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static Dictionary<int, TextTypeUsageRecord> BuildTextNoteUsageMap(Document doc, int maxSampleIds)
    {
        var result = new Dictionary<int, TextTypeUsageRecord>();
        foreach (var textNote in new FilteredElementCollector(doc).OfClass(typeof(TextNote)).Cast<TextNote>())
        {
            var typeId = checked((int)textNote.GetTypeId().Value);
            if (!result.TryGetValue(typeId, out var usage))
            {
                usage = new TextTypeUsageRecord();
                result[typeId] = usage;
            }

            usage.UsageCount++;
            if (usage.SampleTextNoteIds.Count < Math.Max(1, maxSampleIds))
            {
                usage.SampleTextNoteIds.Add(checked((int)textNote.Id.Value));
            }
        }

        return result;
    }

    private sealed class TextTypeUsageRecord
    {
        internal int UsageCount { get; set; }
        internal List<int> SampleTextNoteIds { get; } = new List<int>();
    }
}
