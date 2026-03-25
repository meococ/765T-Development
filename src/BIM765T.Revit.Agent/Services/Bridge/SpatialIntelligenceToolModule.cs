using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

/// <summary>
/// Phase B: Spatial Intelligence Module — 10 tools.
/// Clash detection, proximity search, raycast, geometry extraction,
/// smart section box, zone analysis, element distances, level zones,
/// opening detection, DirectShape creation.
/// </summary>
internal sealed class SpatialIntelligenceToolModule : IToolModule
{
    private readonly ToolModuleContext _context;

    internal SpatialIntelligenceToolModule(ToolModuleContext context) { _context = context; }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var spatial = _context.SpatialIntelligence;
        var readDocument = ToolManifestPresets.Read("document");
        var mutationDocument = ToolManifestPresets.Mutation("document");

        // ── B-1: Clash Detection ──
        registry.Register(ToolNames.SpatialClashDetect,
            "Detect BoundingBox-based clashes between two sets of elements (source vs target). Returns hard and soft clashes with intersection points.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ClashDetectRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, spatial.DetectClashes(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"SourceCategoryNames\":[\"Structural Columns\"],\"SourceElementIds\":[],\"TargetCategoryNames\":[\"Ducts\"],\"TargetElementIds\":[],\"Tolerance\":0.5,\"MaxResults\":500}");

        // ── B-2: Proximity Search ──
        registry.Register(ToolNames.SpatialProximitySearch,
            "Find all elements within a given radius of source elements. Uses BoundingBoxIntersectsFilter for performance.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ProximitySearchRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, spatial.SearchProximity(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"SourceElementIds\":[12345],\"Radius\":3.0,\"TargetCategoryNames\":[\"Walls\"],\"MaxResults\":500}");

        // ── B-3: Raycast ──
        registry.Register(ToolNames.SpatialRaycast,
            "Cast a ray from a point in a direction and find all intersecting elements using ReferenceIntersector. Supports linked models.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<RaycastRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, spatial.ExecuteRaycast(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"Origin\":[0,0,5],\"Direction\":[1,0,0],\"View3DId\":0,\"FindReferencesInLinks\":false,\"MaxHits\":20}");

        // ── B-4: Geometry Extraction ──
        registry.Register(ToolNames.SpatialGeometryExtract,
            "Extract geometry metrics: volume, surface area, face count, centroid, solid count. AI uses this for quantity takeoff and spatial analysis.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<GeometryExtractRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, spatial.ExtractGeometry(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ElementIds\":[12345,67890],\"IncludeVolume\":true,\"IncludeSurfaceArea\":true,\"IncludeFaceCount\":false,\"IncludeCentroid\":true}");

        // ── B-5: Smart Section Box ──
        registry.Register(ToolNames.SpatialSectionBoxFromElements,
            "Compute optimal section box from a set of elements with padding. Use to auto-frame a 3D view around elements of interest.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<SectionBoxFromElementsRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, spatial.ComputeSectionBox(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ElementIds\":[12345,67890],\"Padding\":3.0,\"TargetView3DId\":0}");

        // ── B-6: Zone Summary ──
        registry.Register(ToolNames.SpatialZoneSummary,
            "Analyze element distribution across zones (by level, grid, or custom bbox). Returns per-zone element counts and category breakdowns.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ZoneSummaryRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, spatial.AnalyzeZones(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ZoneMode\":\"level\",\"CategoryNames\":[\"Walls\",\"Columns\"],\"MaxZones\":50}");

        // ── B-7: Element Distance Matrix ──
        registry.Register(ToolNames.SpatialElementDistances,
            "Compute pairwise distances between elements (centroid-based). Returns sorted distance pairs with min/max/avg statistics.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ElementDistanceRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, spatial.ComputeDistances(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"ElementIds\":[12345,67890,11111],\"DistanceMode\":\"centroid\",\"MaxPairs\":200}");

        // ── B-8: Level Zone Analysis ──
        registry.Register(ToolNames.SpatialLevelZoneAnalysis,
            "Analyze element distribution per level with optional category breakdown. Shows building composition by floor.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<LevelZoneAnalysisRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, spatial.AnalyzeLevelZones(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"CategoryNames\":[],\"IncludeBreakdown\":true}");

        // ── B-9: Opening Detection ──
        registry.Register(ToolNames.SpatialOpeningDetect,
            "Detect openings (doors, windows, voids) in host elements (walls, floors, ceilings). Returns host-insert pairs with type info.",
            PermissionLevel.Read, ApprovalRequirement.None, false, readDocument,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<OpeningDetectRequest>(request);
                var doc = platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey);
                return ToolResponses.Success(request, spatial.DetectOpenings(platform, doc, payload));
            },
            "{\"DocumentKey\":\"\",\"HostElementIds\":[],\"HostCategoryNames\":[\"Walls\"],\"MaxResults\":500}");

        // ── B-10: DirectShape Create (mutation) ──
        registry.RegisterMutationTool<DirectShapeCreateRequest>(
            ToolNames.DirectShapeCreateSafe,
            "Create a DirectShape element (box geometry) for visualization markers, clash indicators, or temporary analysis geometry.",
            ApprovalRequirement.ConfirmToken,
            "{\"DocumentKey\":\"\",\"ShapeType\":\"box\",\"Min\":[0,0,0],\"Max\":[10,10,10],\"Name\":\"BIM765T_Marker\",\"CategoryName\":\"Generic Models\"}",
            mutationDocument,
            () => platform.Settings.AllowWriteTools, StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, string.IsNullOrWhiteSpace(payload.DocumentKey) ? request.TargetDocument : payload.DocumentKey),
            null,
            (uiapp, services, doc, payload, request) => spatial.PreviewDirectShapeCreate(services, doc, payload, request),
            (uiapp, services, doc, payload) => spatial.ExecuteDirectShapeCreate(services, doc, payload));
    }
}
