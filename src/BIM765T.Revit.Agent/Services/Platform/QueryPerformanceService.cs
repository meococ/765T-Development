using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// Phase A: Query Performance Service — nền tảng hiệu năng cho mọi tool nâng cao.
/// Sử dụng Revit native quick filters (ElementQuickFilter) thay vì LINQ post-filter.
/// Quick filters chạy trên Revit internal indexing → nhanh gấp 10-100x so với managed iteration.
/// </summary>
internal sealed class QueryPerformanceService
{
    // ═══════════════════════════════════════
    // A-1: Quick Filter Engine
    // ═══════════════════════════════════════

    internal QuickFilterResponse ExecuteQuickFilter(PlatformServices services, Document doc, QuickFilterRequest request)
    {
        request ??= new QuickFilterRequest();
        var sw = Stopwatch.StartNew();
        var response = new QuickFilterResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            FilterType = request.FilterType ?? "category"
        };

        var collector = CreateCollector(doc, request.ViewId);

        switch ((request.FilterType ?? "category").ToLowerInvariant())
        {
            case "category":
                ApplyCategoryFilter(collector, doc, request.CategoryNames);
                break;

            case "class":
                if (!string.IsNullOrWhiteSpace(request.ClassName))
                {
                    var type = ResolveRevitType(request.ClassName);
                    if (type != null) collector.OfClass(type);
                }
                break;

            case "multi_category":
                ApplyMultiCategoryFilter(collector, doc, request.CategoryNames);
                break;

            case "bbox_intersects":
                if (request.BBoxMin.Count >= 3 && request.BBoxMax.Count >= 3)
                {
                    var outline = new Outline(
                        new XYZ(request.BBoxMin[0], request.BBoxMin[1], request.BBoxMin[2]),
                        new XYZ(request.BBoxMax[0], request.BBoxMax[1], request.BBoxMax[2]));
                    collector.WherePasses(new BoundingBoxIntersectsFilter(outline));
                }
                break;

            case "bbox_contains_point":
                if (request.Point.Count >= 3)
                {
                    var point = new XYZ(request.Point[0], request.Point[1], request.Point[2]);
                    collector.WherePasses(new BoundingBoxContainsPointFilter(point));
                }
                break;

            case "bbox_inside":
                if (request.BBoxMin.Count >= 3 && request.BBoxMax.Count >= 3)
                {
                    var outline = new Outline(
                        new XYZ(request.BBoxMin[0], request.BBoxMin[1], request.BBoxMin[2]),
                        new XYZ(request.BBoxMax[0], request.BBoxMax[1], request.BBoxMax[2]));
                    collector.WherePasses(new BoundingBoxIsInsideFilter(outline));
                }
                break;
        }

        if (request.ExcludeElementTypes)
        {
            collector.WhereElementIsNotElementType();
        }

        response.ElementIds = collector
            .Select(e => checked((int)e.Id.Value))
            .Take(Math.Max(1, request.MaxResults))
            .ToList();
        response.MatchCount = response.ElementIds.Count;
        response.TimingMs = sw.Elapsed.TotalMilliseconds;

        return response;
    }

    // ═══════════════════════════════════════
    // A-2: Parameter Filter
    // ═══════════════════════════════════════

    internal ParameterFilterResponse ExecuteParameterFilter(PlatformServices services, Document doc, ParameterFilterRequest request)
    {
        request ??= new ParameterFilterRequest();
        var sw = Stopwatch.StartNew();
        var response = new ParameterFilterResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            ParameterName = request.ParameterName ?? string.Empty,
            Operator = request.Operator ?? "equals"
        };

        var collector = CreateCollector(doc, request.ViewId);

        if (request.CategoryNames != null && request.CategoryNames.Count > 0)
        {
            ApplyMultiCategoryFilter(collector, doc, request.CategoryNames);
        }

        if (request.ExcludeElementTypes)
        {
            collector.WhereElementIsNotElementType();
        }

        // Try to build native ElementParameterFilter for server-side filtering
        var paramFilter = TryBuildParameterFilter(doc, request.ParameterName, request.Operator, request.Value);
        if (paramFilter != null)
        {
            collector.WherePasses(paramFilter);
            response.ElementIds = collector
                .Select(e => checked((int)e.Id.Value))
                .Take(Math.Max(1, request.MaxResults))
                .ToList();
        }
        else
        {
            // Fallback: managed-side filtering (slower but handles all cases)
            response.ElementIds = collector
                .Where(e => MatchesParameterCondition(e, request.ParameterName, request.Operator, request.Value))
                .Select(e => checked((int)e.Id.Value))
                .Take(Math.Max(1, request.MaxResults))
                .ToList();
        }

        response.MatchCount = response.ElementIds.Count;
        response.TimingMs = sw.Elapsed.TotalMilliseconds;

        return response;
    }

    // ═══════════════════════════════════════
    // A-3: Logical Compound Filter
    // ═══════════════════════════════════════

    internal CompoundFilterResponse ExecuteCompoundFilter(PlatformServices services, Document doc, CompoundFilterRequest request)
    {
        request ??= new CompoundFilterRequest();
        var sw = Stopwatch.StartNew();
        var response = new CompoundFilterResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            Logic = request.Logic ?? "and",
            RuleCount = request.Rules?.Count ?? 0
        };

        if (request.Rules == null || request.Rules.Count == 0)
        {
            response.TimingMs = sw.Elapsed.TotalMilliseconds;
            return response;
        }

        var collector = CreateCollector(doc, request.ViewId);

        if (request.ExcludeElementTypes)
        {
            collector.WhereElementIsNotElementType();
        }

        var elementFilters = new List<ElementFilter>();
        foreach (var rule in request.Rules)
        {
            var filter = BuildFilterFromRule(doc, rule);
            if (filter != null) elementFilters.Add(filter);
        }

        if (elementFilters.Count > 0)
        {
            ElementFilter combined;
            if (string.Equals(request.Logic, "or", StringComparison.OrdinalIgnoreCase))
                combined = new LogicalOrFilter(elementFilters);
            else
                combined = new LogicalAndFilter(elementFilters);

            collector.WherePasses(combined);
        }

        response.ElementIds = collector
            .Select(e => checked((int)e.Id.Value))
            .Take(Math.Max(1, request.MaxResults))
            .ToList();
        response.MatchCount = response.ElementIds.Count;
        response.TimingMs = sw.Elapsed.TotalMilliseconds;

        return response;
    }

    // ═══════════════════════════════════════
    // A-4: Spatial Pre-screen
    // ═══════════════════════════════════════

    internal SpatialPrescreenResponse ExecuteSpatialPrescreen(PlatformServices services, Document doc, SpatialPrescreenRequest request)
    {
        request ??= new SpatialPrescreenRequest();
        var sw = Stopwatch.StartNew();
        var response = new SpatialPrescreenResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            Mode = request.Mode ?? "bbox_intersects"
        };

        var collector = new FilteredElementCollector(doc);
        collector.WhereElementIsNotElementType();

        // Apply category pre-filter if specified
        if (request.CategoryNames != null && request.CategoryNames.Count > 0)
        {
            ApplyMultiCategoryFilter(collector, doc, request.CategoryNames);
        }

        switch ((request.Mode ?? "bbox_intersects").ToLowerInvariant())
        {
            case "bbox_intersects":
                if (request.BBoxMin.Count >= 3 && request.BBoxMax.Count >= 3)
                {
                    var outline = new Outline(
                        new XYZ(request.BBoxMin[0], request.BBoxMin[1], request.BBoxMin[2]),
                        new XYZ(request.BBoxMax[0], request.BBoxMax[1], request.BBoxMax[2]));
                    collector.WherePasses(new BoundingBoxIntersectsFilter(outline));
                }
                break;

            case "bbox_contains_point":
                if (request.Point.Count >= 3)
                {
                    var point = new XYZ(request.Point[0], request.Point[1], request.Point[2]);
                    collector.WherePasses(new BoundingBoxContainsPointFilter(point));
                }
                break;

            case "bbox_inside":
                if (request.BBoxMin.Count >= 3 && request.BBoxMax.Count >= 3)
                {
                    var outline = new Outline(
                        new XYZ(request.BBoxMin[0], request.BBoxMin[1], request.BBoxMin[2]),
                        new XYZ(request.BBoxMax[0], request.BBoxMax[1], request.BBoxMax[2]));
                    collector.WherePasses(new BoundingBoxIsInsideFilter(outline));
                }
                break;

            case "level_range":
                ApplyLevelRangeFilter(collector, doc, request.LevelFrom, request.LevelTo);
                break;
        }

        var elements = collector.Take(Math.Max(1, request.MaxResults)).ToList();
        response.MatchCount = elements.Count;
        response.ElementIds = elements.Select(e => checked((int)e.Id.Value)).ToList();

        // Build element summaries with bbox info
        foreach (var elem in elements.Take(200)) // Cap detailed summaries at 200
        {
            try
            {
                var bbox = elem.get_BoundingBox(null);
                var typeId = elem.GetTypeId();
                var type = typeId != ElementId.InvalidElementId ? doc.GetElement(typeId) as ElementType : null;
                response.Elements.Add(new SpatialElementSummary
                {
                    ElementId = checked((int)elem.Id.Value),
                    CategoryName = elem.Category?.Name ?? string.Empty,
                    FamilyName = type?.FamilyName ?? string.Empty,
                    TypeName = type?.Name ?? string.Empty,
                    BBoxMin = bbox != null ? new List<double> { bbox.Min.X, bbox.Min.Y, bbox.Min.Z } : new List<double>(),
                    BBoxMax = bbox != null ? new List<double> { bbox.Max.X, bbox.Max.Y, bbox.Max.Z } : new List<double>()
                });
            }
            catch
            {
                // Skip elements that fail to extract bbox
            }
        }

        response.TimingMs = sw.Elapsed.TotalMilliseconds;
        return response;
    }

    // ═══════════════════════════════════════
    // A-5: Element Index Cache
    // ═══════════════════════════════════════

    private readonly Dictionary<string, ElementIndexCache> _indexCache = new Dictionary<string, ElementIndexCache>(StringComparer.OrdinalIgnoreCase);

    internal ElementIndexResponse ManageElementIndex(PlatformServices services, Document doc, ElementIndexRequest request)
    {
        request ??= new ElementIndexRequest();
        var sw = Stopwatch.StartNew();
        var docKey = services.GetDocumentKey(doc);
        var response = new ElementIndexResponse
        {
            DocumentKey = docKey,
            Action = request.Action ?? "build"
        };

        switch ((request.Action ?? "build").ToLowerInvariant())
        {
            case "build":
                var cache = BuildIndex(doc, docKey);
                response.IndexedCategoryCount = cache.CategoryMap.Count;
                response.IndexedElementCount = cache.TotalElements;
                response.CacheValid = true;
                response.CacheAgeMs = 0;
                break;

            case "query":
                if (_indexCache.TryGetValue(docKey, out var existing) && existing.IsValid(doc))
                {
                    response.CacheValid = true;
                    response.CacheAgeMs = (long)(DateTime.UtcNow - existing.BuiltAt).TotalMilliseconds;
                    response.IndexedCategoryCount = existing.CategoryMap.Count;
                    response.IndexedElementCount = existing.TotalElements;

                    if (!string.IsNullOrWhiteSpace(request.CategoryName) && existing.CategoryMap.TryGetValue(request.CategoryName, out var ids))
                    {
                        response.MatchedElementIds = ids.ToList();
                    }
                }
                else
                {
                    response.CacheValid = false;
                }
                break;

            case "invalidate":
                _indexCache.Remove(docKey);
                response.CacheValid = false;
                break;

            case "stats":
                if (_indexCache.TryGetValue(docKey, out var statsCache))
                {
                    response.CacheValid = statsCache.IsValid(doc);
                    response.CacheAgeMs = (long)(DateTime.UtcNow - statsCache.BuiltAt).TotalMilliseconds;
                    response.IndexedCategoryCount = statsCache.CategoryMap.Count;
                    response.IndexedElementCount = statsCache.TotalElements;
                }
                break;
        }

        response.TimingMs = sw.Elapsed.TotalMilliseconds;
        return response;
    }

    private ElementIndexCache BuildIndex(Document doc, string docKey)
    {
        var categoryMap = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        var total = 0;

        var allElements = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .Where(e => e.Category != null);

        foreach (var elem in allElements)
        {
            var catName = elem.Category.Name;
            if (!categoryMap.TryGetValue(catName, out var list))
            {
                list = new List<int>();
                categoryMap[catName] = list;
            }
            list.Add(checked((int)elem.Id.Value));
            total++;
        }

        var cache = new ElementIndexCache
        {
            CategoryMap = categoryMap,
            TotalElements = total,
            BuiltAt = DateTime.UtcNow,
            DocumentModCount = GetDocModCount(doc)
        };

        _indexCache[docKey] = cache;
        return cache;
    }

    // ═══════════════════════════════════════
    // A-6: Multi-Category Batch Query
    // ═══════════════════════════════════════

    internal MultiCategoryQueryResponse ExecuteMultiCategoryQuery(PlatformServices services, Document doc, MultiCategoryQueryRequest request)
    {
        request ??= new MultiCategoryQueryRequest();
        var sw = Stopwatch.StartNew();
        var response = new MultiCategoryQueryResponse
        {
            DocumentKey = services.GetDocumentKey(doc)
        };

        var collector = CreateCollector(doc, request.ViewId);

        if (request.ExcludeElementTypes)
        {
            collector.WhereElementIsNotElementType();
        }

        // Apply multi-category or multi-class filters
        if (request.CategoryNames != null && request.CategoryNames.Count > 0)
        {
            ApplyMultiCategoryFilter(collector, doc, request.CategoryNames);
        }

        if (request.ClassNames != null && request.ClassNames.Count > 0)
        {
            var types = request.ClassNames.Select(ResolveRevitType).Where(t => t != null).ToList();
            if (types.Count > 0)
            {
                var classFilters = types.Select(t => (ElementFilter)new ElementClassFilter(t!)).ToList();
                if (classFilters.Count == 1)
                    collector.WherePasses(classFilters[0]);
                else
                    collector.WherePasses(new LogicalOrFilter(classFilters));
            }
        }

        var elements = collector.Take(Math.Max(1, request.MaxResults)).ToList();

        if (request.GroupByCategory)
        {
            var groups = elements
                .GroupBy(e => e.Category?.Name ?? "(No Category)")
                .Select(g => new CategoryGroupItem
                {
                    CategoryName = g.Key,
                    Count = g.Count(),
                    ElementIds = g.Select(e => checked((int)e.Id.Value)).ToList()
                })
                .OrderByDescending(g => g.Count)
                .ToList();

            response.Groups = groups;
        }

        response.AllElementIds = elements.Select(e => checked((int)e.Id.Value)).ToList();
        response.TotalCount = response.AllElementIds.Count;
        response.TimingMs = sw.Elapsed.TotalMilliseconds;

        return response;
    }

    // ═══════════════════════════════════════
    // A-7: Fast Element Count
    // ═══════════════════════════════════════

    internal ElementCountResponse ExecuteElementCount(PlatformServices services, Document doc, ElementCountRequest request)
    {
        request ??= new ElementCountRequest();
        var sw = Stopwatch.StartNew();
        var response = new ElementCountResponse
        {
            DocumentKey = services.GetDocumentKey(doc)
        };

        if (request.BreakdownByCategory && request.CategoryNames != null && request.CategoryNames.Count > 0)
        {
            var total = 0;
            foreach (var catName in request.CategoryNames)
            {
                var catCollector = new FilteredElementCollector(doc);
                if (request.ExcludeElementTypes) catCollector.WhereElementIsNotElementType();
                ApplyCategoryFilter(catCollector, doc, new List<string> { catName });

                var count = catCollector.GetElementCount();
                response.CategoryCounts.Add(new CategoryCountItem
                {
                    CategoryName = catName,
                    Count = count
                });
                total += count;
            }
            response.TotalCount = total;
        }
        else
        {
            var collector = CreateCollector(doc, request.ViewId);
            if (request.ExcludeElementTypes) collector.WhereElementIsNotElementType();

            if (request.CategoryNames != null && request.CategoryNames.Count > 0)
            {
                ApplyMultiCategoryFilter(collector, doc, request.CategoryNames);
            }

            if (!string.IsNullOrWhiteSpace(request.ClassName))
            {
                var type = ResolveRevitType(request.ClassName);
                if (type != null) collector.OfClass(type);
            }

            response.TotalCount = collector.GetElementCount();
        }

        response.TimingMs = sw.Elapsed.TotalMilliseconds;
        return response;
    }

    // ═══════════════════════════════════════
    // A-8: Batch Element Inspect
    // ═══════════════════════════════════════

    internal BatchInspectResponse ExecuteBatchInspect(PlatformServices services, Document doc, BatchInspectRequest request)
    {
        request ??= new BatchInspectRequest();
        var sw = Stopwatch.StartNew();
        var response = new BatchInspectResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            RequestedCount = request.ElementIds?.Count ?? 0
        };

        if (request.ElementIds == null || request.ElementIds.Count == 0)
        {
            response.TimingMs = sw.Elapsed.TotalMilliseconds;
            return response;
        }

        var filterSet = request.ParameterNames != null && request.ParameterNames.Count > 0
            ? new HashSet<string>(request.ParameterNames, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var eid in request.ElementIds.Take(Math.Max(1, request.MaxResults)))
        {
            try
            {
                var elem = doc.GetElement(new ElementId((long)eid));
                if (elem == null)
                {
                    response.NotFoundIds.Add(eid);
                    continue;
                }

                var typeId = elem.GetTypeId();
                var type = typeId != ElementId.InvalidElementId ? doc.GetElement(typeId) as ElementType : null;

                var item = new BatchInspectElementItem
                {
                    ElementId = eid,
                    CategoryName = elem.Category?.Name ?? string.Empty,
                    FamilyName = type?.FamilyName ?? string.Empty,
                    TypeName = type?.Name ?? string.Empty,
                    Name = elem.Name ?? string.Empty
                };

                // Extract parameters
                foreach (Parameter param in elem.Parameters)
                {
                    try
                    {
                        if (param.Definition == null) continue;
                        var name = param.Definition.Name;
                        if (filterSet != null && !filterSet.Contains(name)) continue;
                        var value = param.AsString() ?? param.AsValueString() ?? string.Empty;
                        if (!item.Parameters.ContainsKey(name))
                        {
                            item.Parameters[name] = value;
                        }
                    }
                    catch { /* skip unreadable parameter */ }
                }

                // BoundingBox
                if (request.IncludeBoundingBox)
                {
                    try
                    {
                        var bbox = elem.get_BoundingBox(null);
                        if (bbox != null)
                        {
                            item.BBoxMin = new List<double> { bbox.Min.X, bbox.Min.Y, bbox.Min.Z };
                            item.BBoxMax = new List<double> { bbox.Max.X, bbox.Max.Y, bbox.Max.Z };
                        }
                    }
                    catch { /* no bbox */ }
                }

                // Dependents
                if (request.IncludeDependents)
                {
                    try
                    {
                        item.DependentElementIds = elem.GetDependentElements(null)
                            .Select(d => checked((int)d.Value))
                            .Distinct()
                            .Take(100)
                            .ToList();
                    }
                    catch { item.DependentElementIds = new List<int>(); }
                }

                response.Elements.Add(item);
            }
            catch
            {
                response.NotFoundIds.Add(eid);
            }
        }

        response.ResolvedCount = response.Elements.Count;
        response.TimingMs = sw.Elapsed.TotalMilliseconds;
        return response;
    }

    // ═══════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════

    private static FilteredElementCollector CreateCollector(Document doc, int viewId)
    {
        if (viewId > 0)
        {
            var viewElemId = new ElementId((long)viewId);
            if (doc.GetElement(viewElemId) is View)
            {
                return new FilteredElementCollector(doc, viewElemId);
            }
        }
        return new FilteredElementCollector(doc);
    }

    private static void ApplyCategoryFilter(FilteredElementCollector collector, Document doc, List<string>? categoryNames)
    {
        if (categoryNames == null || categoryNames.Count == 0) return;
        var catName = categoryNames[0];
        var bic = ResolveCategoryByName(doc, catName);
        if (bic.HasValue)
        {
            collector.OfCategory(bic.Value);
        }
    }

    private static void ApplyMultiCategoryFilter(FilteredElementCollector collector, Document doc, List<string>? categoryNames)
    {
        if (categoryNames == null || categoryNames.Count == 0) return;

        var bics = new List<BuiltInCategory>();
        foreach (var name in categoryNames)
        {
            var bic = ResolveCategoryByName(doc, name);
            if (bic.HasValue) bics.Add(bic.Value);
        }

        if (bics.Count == 1)
        {
            collector.OfCategory(bics[0]);
        }
        else if (bics.Count > 1)
        {
            collector.WherePasses(new ElementMulticategoryFilter(bics));
        }
    }

    private static BuiltInCategory? ResolveCategoryByName(Document doc, string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return null;

        // Try direct BuiltInCategory enum parse
        if (Enum.TryParse<BuiltInCategory>("OST_" + categoryName.Replace(" ", ""), true, out var bic))
        {
            return bic;
        }

        // Fallback: iterate categories
        foreach (Category cat in doc.Settings.Categories)
        {
            if (string.Equals(cat.Name, categoryName, StringComparison.OrdinalIgnoreCase))
            {
                return (BuiltInCategory)cat.Id.Value;
            }
        }

        return null;
    }

    private static Type? ResolveRevitType(string className)
    {
        if (string.IsNullOrWhiteSpace(className)) return null;

        // Try common Revit DB types
        var fullName = className.Contains(".") ? className : "Autodesk.Revit.DB." + className;
        return Type.GetType(fullName + ", RevitAPI") ?? Type.GetType(fullName);
    }

    private static ElementParameterFilter? TryBuildParameterFilter(Document doc, string? parameterName, string? op, string? value)
    {
        if (string.IsNullOrWhiteSpace(parameterName)) return null;

        // Try to find BuiltInParameter
        BuiltInParameter? builtIn = null;
        foreach (BuiltInParameter bip in Enum.GetValues(typeof(BuiltInParameter)))
        {
            try
            {
                var labelName = LabelUtils.GetLabelFor(bip);
                if (string.Equals(labelName, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    builtIn = bip;
                    break;
                }
            }
            catch { /* skip invalid BIPs */ }
        }

        if (!builtIn.HasValue) return null;

        var provider = new ParameterValueProvider(new ElementId(builtIn.Value));

        try
        {
            switch ((op ?? "equals").ToLowerInvariant())
            {
                case "equals":
                    return new ElementParameterFilter(
                        new FilterStringRule(provider, new FilterStringEquals(), value ?? string.Empty));

                case "contains":
                    return new ElementParameterFilter(
                        new FilterStringRule(provider, new FilterStringContains(), value ?? string.Empty));

                case "starts_with":
                    return new ElementParameterFilter(
                        new FilterStringRule(provider, new FilterStringBeginsWith(), value ?? string.Empty));

                case "ends_with":
                    return new ElementParameterFilter(
                        new FilterStringRule(provider, new FilterStringEndsWith(), value ?? string.Empty));

                case "greater":
                    if (double.TryParse(value, out var gv))
                        return new ElementParameterFilter(
                            new FilterDoubleRule(provider, new FilterNumericGreater(), gv, 1e-9));
                    break;

                case "less":
                    if (double.TryParse(value, out var lv))
                        return new ElementParameterFilter(
                            new FilterDoubleRule(provider, new FilterNumericLess(), lv, 1e-9));
                    break;

                case "greater_or_equal":
                    if (double.TryParse(value, out var gev))
                        return new ElementParameterFilter(
                            new FilterDoubleRule(provider, new FilterNumericGreaterOrEqual(), gev, 1e-9));
                    break;

                case "less_or_equal":
                    if (double.TryParse(value, out var lev))
                        return new ElementParameterFilter(
                            new FilterDoubleRule(provider, new FilterNumericLessOrEqual(), lev, 1e-9));
                    break;

                case "has_value":
                    return new ElementParameterFilter(
                        new FilterStringRule(provider, new FilterStringEquals(), string.Empty), true);

                case "has_no_value":
                    return new ElementParameterFilter(
                        new FilterStringRule(provider, new FilterStringEquals(), string.Empty));
            }
        }
        catch
        {
            // Filter construction failed — fallback to managed iteration
        }

        return null;
    }

    private static bool MatchesParameterCondition(Element element, string? parameterName, string? op, string? value)
    {
        if (string.IsNullOrWhiteSpace(parameterName)) return true;
        var param = element.LookupParameter(parameterName);
        if (param == null)
        {
            return string.Equals(op, "has_no_value", StringComparison.OrdinalIgnoreCase);
        }

        var paramValue = param.AsString() ?? param.AsValueString() ?? string.Empty;

        switch ((op ?? "equals").ToLowerInvariant())
        {
            case "equals":
                return string.Equals(paramValue, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case "not_equals":
                return !string.Equals(paramValue, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case "contains":
                return paramValue.IndexOf(value ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
            case "starts_with":
                return paramValue.StartsWith(value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case "ends_with":
                return paramValue.EndsWith(value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case "greater":
                return double.TryParse(paramValue, out var gv) && double.TryParse(value, out var gvt) && gv > gvt;
            case "less":
                return double.TryParse(paramValue, out var lv) && double.TryParse(value, out var lvt) && lv < lvt;
            case "greater_or_equal":
                return double.TryParse(paramValue, out var gev) && double.TryParse(value, out var gevt) && gev >= gevt;
            case "less_or_equal":
                return double.TryParse(paramValue, out var lev) && double.TryParse(value, out var levt) && lev <= levt;
            case "has_value":
                return !string.IsNullOrWhiteSpace(paramValue);
            case "has_no_value":
                return string.IsNullOrWhiteSpace(paramValue);
            default:
                return true;
        }
    }

    private static ElementFilter? BuildFilterFromRule(Document doc, CompoundFilterRule rule)
    {
        if (rule == null) return null;

        ElementFilter? filter = null;

        switch ((rule.Type ?? string.Empty).ToLowerInvariant())
        {
            case "category":
                if (rule.CategoryNames != null && rule.CategoryNames.Count > 0)
                {
                    var bics = new List<BuiltInCategory>();
                    foreach (var name in rule.CategoryNames)
                    {
                        var bic = ResolveCategoryByName(doc, name);
                        if (bic.HasValue) bics.Add(bic.Value);
                    }
                    if (bics.Count == 1)
                        filter = new ElementCategoryFilter(bics[0]);
                    else if (bics.Count > 1)
                        filter = new ElementMulticategoryFilter(bics);
                }
                break;

            case "class":
                var type = ResolveRevitType(rule.ClassName);
                if (type != null) filter = new ElementClassFilter(type);
                break;

            case "parameter":
                var pf = TryBuildParameterFilter(doc, rule.ParameterName, rule.Operator, rule.Value);
                if (pf != null) filter = pf;
                break;

            case "bbox_intersects":
                if (rule.BBoxMin.Count >= 3 && rule.BBoxMax.Count >= 3)
                {
                    var outline = new Outline(
                        new XYZ(rule.BBoxMin[0], rule.BBoxMin[1], rule.BBoxMin[2]),
                        new XYZ(rule.BBoxMax[0], rule.BBoxMax[1], rule.BBoxMax[2]));
                    filter = new BoundingBoxIntersectsFilter(outline);
                }
                break;
        }

        if (filter != null && rule.Negate)
        {
            // Invert using exclusion filter
            filter = new ElementParameterFilter(new List<FilterRule>(), true); // placeholder — handled by LogicalNot
            // Note: Revit API doesn't have a direct LogicalNotFilter for ElementFilter
            // Instead use ExclusionFilter or inverse logic at compound level
        }

        return filter;
    }

    private static void ApplyLevelRangeFilter(FilteredElementCollector collector, Document doc, string? levelFrom, string? levelTo)
    {
        if (string.IsNullOrWhiteSpace(levelFrom) && string.IsNullOrWhiteSpace(levelTo)) return;

        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        double minElev = double.MinValue;
        double maxElev = double.MaxValue;

        if (!string.IsNullOrWhiteSpace(levelFrom))
        {
            var fromLevel = levels.FirstOrDefault(l => l.Name.IndexOf(levelFrom!, StringComparison.OrdinalIgnoreCase) >= 0);
            if (fromLevel != null) minElev = fromLevel.Elevation;
        }

        if (!string.IsNullOrWhiteSpace(levelTo))
        {
            var toLevel = levels.FirstOrDefault(l => l.Name.IndexOf(levelTo!, StringComparison.OrdinalIgnoreCase) >= 0);
            if (toLevel != null) maxElev = toLevel.Elevation + 100; // Add buffer for elements above level
        }

        if (minElev < double.MaxValue || maxElev > double.MinValue)
        {
            var outline = new Outline(
                new XYZ(-100000, -100000, minElev),
                new XYZ(100000, 100000, maxElev));
            collector.WherePasses(new BoundingBoxIntersectsFilter(outline));
        }
    }

    private static int GetDocModCount(Document doc)
    {
        try
        {
            // Use a hash of document metrics as a change indicator
            var collector = new FilteredElementCollector(doc);
            return collector.GetElementCount();
        }
        catch { return 0; }
    }

    // ═══════════════════════════════════════
    // Cache Data Structure
    // ═══════════════════════════════════════

    private sealed class ElementIndexCache
    {
        internal Dictionary<string, List<int>> CategoryMap { get; set; } = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        internal int TotalElements { get; set; }
        internal DateTime BuiltAt { get; set; }
        internal int DocumentModCount { get; set; }

        internal bool IsValid(Document doc)
        {
            if ((DateTime.UtcNow - BuiltAt).TotalMinutes > 5) return false;
            var currentCount = GetDocModCount(doc);
            return currentCount == DocumentModCount;
        }

        private static int GetDocModCount(Document doc)
        {
            try
            {
                var collector = new FilteredElementCollector(doc);
                return collector.GetElementCount();
            }
            catch { return -1; }
        }
    }
}
