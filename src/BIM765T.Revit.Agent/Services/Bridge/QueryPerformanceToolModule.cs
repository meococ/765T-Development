using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

/// <summary>
/// Phase A: Query Performance Module — 8 tools.
/// Nền tảng hiệu năng: quick filters, parameter filters, spatial pre-screen,
/// element index cache, multi-category query, fast element count, batch inspect.
/// </summary>
internal sealed class QueryPerformanceToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal QueryPerformanceToolModule(ToolModuleContext context) { _context = context; }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var queryPerformance = _context.QueryPerformance;
        var readDocument = ToolManifestPresets.Read("document");

        // ═══════════════════════════════════════
        // A-1: Quick Filter Engine
        // ═══════════════════════════════════════

        registry.Register(ToolNames.QueryQuickFilter,
            "Execute high-performance quick filter (category, class, bounding box) using Revit native indexing — 10-100x faster than LINQ. Returns ElementId[] with timing.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<QuickFilterRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, queryPerformance.ExecuteQuickFilter(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"FilterType\":\"category\",\"CategoryNames\":[\"Walls\"],\"ClassName\":\"\",\"BBoxMin\":[],\"BBoxMax\":[],\"Point\":[],\"ViewId\":0,\"ExcludeElementTypes\":true,\"MaxResults\":10000}");

        // ═══════════════════════════════════════
        // A-2: Parameter Filter
        // ═══════════════════════════════════════

        registry.Register(ToolNames.QueryParameterFilter,
            "Filter elements by parameter value server-side using Revit native ElementParameterFilter. Supports: equals, contains, starts_with, greater, less, has_value, has_no_value.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ParameterFilterRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, queryPerformance.ExecuteParameterFilter(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ParameterName\":\"Mark\",\"Operator\":\"equals\",\"Value\":\"ABC\",\"CategoryNames\":[],\"ExcludeElementTypes\":true,\"MaxResults\":5000}");

        // ═══════════════════════════════════════
        // A-3: Logical Compound Filter
        // ═══════════════════════════════════════

        registry.Register(ToolNames.QueryLogicalCompound,
            "Build compound filter trees (AND/OR) from multiple rules: category + parameter + bounding box. Express complex BIM queries in a single call.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<CompoundFilterRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, queryPerformance.ExecuteCompoundFilter(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"Logic\":\"and\",\"Rules\":[{\"Type\":\"category\",\"CategoryNames\":[\"Walls\"]},{\"Type\":\"parameter\",\"ParameterName\":\"Mark\",\"Operator\":\"contains\",\"Value\":\"EXT\"}],\"ExcludeElementTypes\":true,\"MaxResults\":5000}");

        // ═══════════════════════════════════════
        // A-4: Spatial Pre-screen
        // ═══════════════════════════════════════

        registry.Register(ToolNames.QuerySpatialPrescreen,
            "Find elements within a spatial region (bounding box, point, level range). Returns element summaries with bbox coordinates. Prerequisite for clash detection.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<SpatialPrescreenRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, queryPerformance.ExecuteSpatialPrescreen(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"Mode\":\"bbox_intersects\",\"BBoxMin\":[0,0,0],\"BBoxMax\":[100,100,30],\"Point\":[],\"LevelFrom\":\"\",\"LevelTo\":\"\",\"CategoryNames\":[],\"MaxResults\":10000}");

        // ═══════════════════════════════════════
        // A-5: Element Index Cache
        // ═══════════════════════════════════════

        registry.Register(ToolNames.CacheElementIndex,
            "Manage element index cache: build category→element map, query cached data, check cache validity. Speeds up repeated queries 5-10x.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ElementIndexRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, queryPerformance.ManageElementIndex(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"Action\":\"build\",\"CategoryName\":\"\",\"ParameterName\":\"\",\"ParameterValue\":\"\"}");

        // ═══════════════════════════════════════
        // A-6: Multi-Category Batch Query
        // ═══════════════════════════════════════

        registry.Register(ToolNames.QueryMultiCategory,
            "Query multiple categories/classes in a single call using ElementMulticategoryFilter. Groups results by category. Reduces round-trips for cross-category analysis.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<MultiCategoryQueryRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, queryPerformance.ExecuteMultiCategoryQuery(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"CategoryNames\":[\"Walls\",\"Floors\",\"Columns\"],\"ClassNames\":[],\"ExcludeElementTypes\":true,\"MaxResults\":10000,\"GroupByCategory\":true}");

        // ═══════════════════════════════════════
        // A-7: Fast Element Count
        // ═══════════════════════════════════════

        registry.Register(ToolNames.QueryElementCount,
            "Get element count without loading elements into memory — uses GetElementCount() for maximum speed. Supports per-category breakdown.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ElementCountRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, queryPerformance.ExecuteElementCount(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"CategoryNames\":[\"Walls\",\"Columns\"],\"ClassName\":\"\",\"ExcludeElementTypes\":true,\"BreakdownByCategory\":true}");

        // ═══════════════════════════════════════
        // A-8: Batch Element Inspect
        // ═══════════════════════════════════════

        registry.Register(ToolNames.QueryBatchInspect,
            "Inspect multiple elements in a single call with optional column selection (specific parameters only). Includes optional bounding box and dependency info.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<BatchInspectRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, queryPerformance.ExecuteBatchInspect(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ElementIds\":[12345,67890],\"ParameterNames\":[\"Mark\",\"Comments\"],\"IncludeBoundingBox\":true,\"IncludeDependents\":false,\"MaxResults\":500}");
    }
}
