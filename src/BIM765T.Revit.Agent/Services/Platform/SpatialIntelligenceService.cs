using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// Phase B: Spatial Intelligence Service — clash detection, proximity, raycast,
/// geometry extraction, zone analysis, opening detection, DirectShape creation.
/// Competitive advantage: AI-driven spatial reasoning mà pyRevit/Dynamo không có sẵn.
/// </summary>
internal sealed class SpatialIntelligenceService
{
    // ═══════════════════════════════════════
    // B-1: Clash Detection (BBox-based fast clash)
    // ═══════════════════════════════════════

    internal ClashDetectResponse DetectClashes(PlatformServices services, Document doc, ClashDetectRequest request)
    {
        request ??= new ClashDetectRequest();
        var sw = Stopwatch.StartNew();
        var response = new ClashDetectResponse { DocumentKey = services.GetDocumentKey(doc) };

        var sourceElements = ResolveElements(doc, request.SourceCategoryNames, request.SourceElementIds);
        var targetElements = ResolveElements(doc, request.TargetCategoryNames, request.TargetElementIds);

        response.SourceCount = sourceElements.Count;
        response.TargetCount = targetElements.Count;

        // Pre-compute target bboxes for O(n*m) check
        var targetBboxes = new List<(Element elem, BoundingBoxXYZ bbox)>();
        foreach (var target in targetElements)
        {
            try
            {
                var bbox = target.get_BoundingBox(null);
                if (bbox != null) targetBboxes.Add((target, bbox));
            }
            catch { /* skip */ }
        }

        var tolerance = request.Tolerance;
        var maxResults = Math.Max(1, request.MaxResults);
        var sourceIdSet = new HashSet<long>(sourceElements.Select(e => e.Id.Value));

        foreach (var source in sourceElements)
        {
            if (response.Clashes.Count >= maxResults) break;

            BoundingBoxXYZ sourceBbox;
            try { sourceBbox = source.get_BoundingBox(null); }
            catch { continue; }
            if (sourceBbox == null) continue;

            // Expand source bbox by tolerance for soft clash detection
            var expandedMin = new XYZ(sourceBbox.Min.X - tolerance, sourceBbox.Min.Y - tolerance, sourceBbox.Min.Z - tolerance);
            var expandedMax = new XYZ(sourceBbox.Max.X + tolerance, sourceBbox.Max.Y + tolerance, sourceBbox.Max.Z + tolerance);

            foreach (var (target, targetBbox) in targetBboxes)
            {
                if (response.Clashes.Count >= maxResults) break;
                if (target.Id.Value == source.Id.Value) continue;
                if (sourceIdSet.Contains(target.Id.Value)) continue; // Skip self-set

                if (BBoxIntersects(expandedMin, expandedMax, targetBbox.Min, targetBbox.Max))
                {
                    var isHardClash = BBoxIntersects(sourceBbox.Min, sourceBbox.Max, targetBbox.Min, targetBbox.Max);
                    var center = MidPoint(sourceBbox.Min, sourceBbox.Max, targetBbox.Min, targetBbox.Max);

                    var clash = new ClashResult
                    {
                        SourceElementId = checked((int)source.Id.Value),
                        SourceCategory = source.Category?.Name ?? string.Empty,
                        TargetElementId = checked((int)target.Id.Value),
                        TargetCategory = target.Category?.Name ?? string.Empty,
                        ClashType = isHardClash ? "hard" : "soft",
                        ApproxDistance = isHardClash ? 0 : ComputeBBoxDistance(sourceBbox, targetBbox),
                        IntersectionPoint = center
                    };

                    response.Clashes.Add(clash);
                    if (isHardClash) response.HardClashCount++;
                    else response.SoftClashCount++;
                }
            }
        }

        response.ClashCount = response.Clashes.Count;
        response.TimingMs = sw.Elapsed.TotalMilliseconds;
        return response;
    }

    // ═══════════════════════════════════════
    // B-2: Proximity Search
    // ═══════════════════════════════════════

    internal ProximitySearchResponse SearchProximity(PlatformServices services, Document doc, ProximitySearchRequest request)
    {
        request ??= new ProximitySearchRequest();
        var sw = Stopwatch.StartNew();
        var response = new ProximitySearchResponse { DocumentKey = services.GetDocumentKey(doc) };

        if (request.SourceElementIds == null || request.SourceElementIds.Count == 0)
        {
            response.TimingMs = sw.Elapsed.TotalMilliseconds;
            return response;
        }

        var sourceSet = new HashSet<long>(request.SourceElementIds.Select(id => (long)id));

        foreach (var srcId in request.SourceElementIds.Take(50)) // Cap sources at 50
        {
            var srcElement = doc.GetElement(new ElementId((long)srcId));
            if (srcElement == null) continue;

            var srcBbox = srcElement.get_BoundingBox(null);
            if (srcBbox == null) continue;

            // Create expanded bbox for proximity search
            var radius = request.Radius;
            var outline = new Outline(
                new XYZ(srcBbox.Min.X - radius, srcBbox.Min.Y - radius, srcBbox.Min.Z - radius),
                new XYZ(srcBbox.Max.X + radius, srcBbox.Max.Y + radius, srcBbox.Max.Z + radius));

            var collector = new FilteredElementCollector(doc);
            collector.WherePasses(new BoundingBoxIntersectsFilter(outline));
            collector.WhereElementIsNotElementType();

            if (request.TargetCategoryNames != null && request.TargetCategoryNames.Count > 0)
            {
                ApplyMultiCategoryFilter(collector, doc, request.TargetCategoryNames);
            }

            foreach (var nearby in collector.Take(Math.Max(1, request.MaxResults)))
            {
                if (sourceSet.Contains(nearby.Id.Value)) continue;

                var nearbyBbox = nearby.get_BoundingBox(null);
                if (nearbyBbox == null) continue;

                var dist = ComputeBBoxDistance(srcBbox, nearbyBbox);
                if (dist <= radius)
                {
                    response.Results.Add(new ProximityResult
                    {
                        SourceElementId = srcId,
                        NearbyElementId = checked((int)nearby.Id.Value),
                        CategoryName = nearby.Category?.Name ?? string.Empty,
                        ApproxDistance = dist
                    });
                }
            }
        }

        response.Results = response.Results.OrderBy(r => r.ApproxDistance).Take(Math.Max(1, request.MaxResults)).ToList();
        response.NearbyCount = response.Results.Count;
        response.TimingMs = sw.Elapsed.TotalMilliseconds;
        return response;
    }

    // ═══════════════════════════════════════
    // B-3: Raycast (ReferenceIntersector)
    // ═══════════════════════════════════════

    internal RaycastResponse ExecuteRaycast(PlatformServices services, Document doc, RaycastRequest request)
    {
        request ??= new RaycastRequest();
        var sw = Stopwatch.StartNew();
        var response = new RaycastResponse { DocumentKey = services.GetDocumentKey(doc) };

        if (request.Origin.Count < 3 || request.Direction.Count < 3)
        {
            response.TimingMs = sw.Elapsed.TotalMilliseconds;
            return response;
        }

        var origin = new XYZ(request.Origin[0], request.Origin[1], request.Origin[2]);
        var rawDir = new XYZ(request.Direction[0], request.Direction[1], request.Direction[2]);
        if (rawDir.GetLength() < 1e-9)
        {
            response.TimingMs = sw.Elapsed.TotalMilliseconds;
            return response; // zero vector, no raycast possible
        }
        var direction = rawDir.Normalize();

        View3D? view3D = null;
        if (request.View3DId > 0)
        {
            view3D = doc.GetElement(new ElementId((long)request.View3DId)) as View3D;
        }

        if (view3D == null)
        {
            // Find any non-template 3D view
            view3D = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate);
        }

        if (view3D == null)
        {
            response.TimingMs = sw.Elapsed.TotalMilliseconds;
            return response;
        }

        try
        {
            var intersector = new ReferenceIntersector(view3D);
            intersector.FindReferencesInRevitLinks = request.FindReferencesInLinks;

            var results = intersector.Find(origin, direction);
            if (results != null)
            {
                foreach (var result in results.Take(Math.Max(1, request.MaxHits)))
                {
                    var reference = result.GetReference();
                    var linkedElem = reference.LinkedElementId != ElementId.InvalidElementId;
                    var elemId = linkedElem ? reference.LinkedElementId : reference.ElementId;
                    var elem = linkedElem
                        ? GetLinkedElement(doc, reference)
                        : doc.GetElement(elemId);

                    response.Hits.Add(new RaycastHit
                    {
                        ElementId = checked((int)elemId.Value),
                        CategoryName = elem?.Category?.Name ?? string.Empty,
                        Distance = result.Proximity,
                        HitPoint = new List<double>
                        {
                            origin.X + direction.X * result.Proximity,
                            origin.Y + direction.Y * result.Proximity,
                            origin.Z + direction.Z * result.Proximity
                        },
                        IsLinkedElement = linkedElem,
                        LinkName = linkedElem ? GetLinkName(doc, reference) : string.Empty
                    });
                }
            }
        }
        catch (Exception)
        {
            // ReferenceIntersector can fail on certain view states or malformed geometry
            response.Hits.Clear();
        }

        response.HitCount = response.Hits.Count;
        response.TimingMs = sw.Elapsed.TotalMilliseconds;
        return response;
    }

    // ═══════════════════════════════════════
    // B-4: Geometry Extraction
    // ═══════════════════════════════════════

    internal GeometryExtractResponse ExtractGeometry(PlatformServices services, Document doc, GeometryExtractRequest request)
    {
        request ??= new GeometryExtractRequest();
        var sw = Stopwatch.StartNew();
        var response = new GeometryExtractResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            RequestedCount = request.ElementIds?.Count ?? 0
        };

        if (request.ElementIds == null || request.ElementIds.Count == 0)
        {
            response.TimingMs = sw.Elapsed.TotalMilliseconds;
            return response;
        }

        var options = new Options
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = false,
            DetailLevel = ViewDetailLevel.Fine
        };

        foreach (var eid in request.ElementIds.Take(200))
        {
            try
            {
                var elem = doc.GetElement(new ElementId((long)eid));
                if (elem == null) continue;

                var bbox = elem.get_BoundingBox(null);
                var info = new GeometryInfo
                {
                    ElementId = eid,
                    CategoryName = elem.Category?.Name ?? string.Empty,
                    BBoxMin = bbox != null ? new List<double> { bbox.Min.X, bbox.Min.Y, bbox.Min.Z } : new List<double>(),
                    BBoxMax = bbox != null ? new List<double> { bbox.Max.X, bbox.Max.Y, bbox.Max.Z } : new List<double>()
                };

                var geomElement = elem.get_Geometry(options);
                if (geomElement != null)
                {
                    var solids = ExtractSolids(geomElement);
                    info.SolidCount = solids.Count;

                    if (request.IncludeVolume)
                    {
                        info.Volume = solids.Sum(s => { try { return s.Volume; } catch { return 0; } });
                    }

                    if (request.IncludeSurfaceArea)
                    {
                        info.SurfaceArea = solids.Sum(s => { try { return s.SurfaceArea; } catch { return 0; } });
                    }

                    if (request.IncludeFaceCount)
                    {
                        info.FaceCount = solids.Sum(s => { try { return s.Faces.Size; } catch { return 0; } });
                    }

                    if (request.IncludeCentroid && bbox != null)
                    {
                        info.Centroid = new List<double>
                        {
                            (bbox.Min.X + bbox.Max.X) / 2.0,
                            (bbox.Min.Y + bbox.Max.Y) / 2.0,
                            (bbox.Min.Z + bbox.Max.Z) / 2.0
                        };
                    }
                }

                response.Elements.Add(info);
            }
            catch { /* skip problematic elements */ }
        }

        response.ResolvedCount = response.Elements.Count;
        response.TimingMs = sw.Elapsed.TotalMilliseconds;
        return response;
    }

    // ═══════════════════════════════════════
    // B-5: Smart Section Box
    // ═══════════════════════════════════════

    internal SectionBoxFromElementsResponse ComputeSectionBox(PlatformServices services, Document doc, SectionBoxFromElementsRequest request)
    {
        request ??= new SectionBoxFromElementsRequest();
        var sw = Stopwatch.StartNew();
        var response = new SectionBoxFromElementsResponse { DocumentKey = services.GetDocumentKey(doc) };

        if (request.ElementIds == null || request.ElementIds.Count == 0)
        {
            response.TimingMs = sw.Elapsed.TotalMilliseconds;
            return response;
        }

        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        int covered = 0;

        foreach (var eid in request.ElementIds)
        {
            try
            {
                var elem = doc.GetElement(new ElementId((long)eid));
                if (elem == null) continue;
                var bbox = elem.get_BoundingBox(null);
                if (bbox == null) continue;

                minX = Math.Min(minX, bbox.Min.X); minY = Math.Min(minY, bbox.Min.Y); minZ = Math.Min(minZ, bbox.Min.Z);
                maxX = Math.Max(maxX, bbox.Max.X); maxY = Math.Max(maxY, bbox.Max.Y); maxZ = Math.Max(maxZ, bbox.Max.Z);
                covered++;
            }
            catch { /* skip */ }
        }

        if (covered > 0)
        {
            var pad = request.Padding;
            response.BBoxMin = new List<double> { minX - pad, minY - pad, minZ - pad };
            response.BBoxMax = new List<double> { maxX + pad, maxY + pad, maxZ + pad };
            response.ElementsCovered = covered;
        }

        response.TimingMs = sw.Elapsed.TotalMilliseconds;
        return response;
    }

    // ═══════════════════════════════════════
    // B-6: Zone Summary
    // ═══════════════════════════════════════

    internal ZoneSummaryResponse AnalyzeZones(PlatformServices services, Document doc, ZoneSummaryRequest request)
    {
        request ??= new ZoneSummaryRequest();
        var sw = Stopwatch.StartNew();
        var response = new ZoneSummaryResponse { DocumentKey = services.GetDocumentKey(doc) };

        if (string.Equals(request.ZoneMode, "level", StringComparison.OrdinalIgnoreCase))
        {
            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Take(Math.Max(1, request.MaxZones))
                .ToList();

            // PERF: Collect 1 lần, group by LevelId — tránh tạo collector mới cho mỗi level
            var allElementsCollector = new FilteredElementCollector(doc);
            allElementsCollector.WhereElementIsNotElementType();
            if (request.CategoryNames != null && request.CategoryNames.Count > 0)
            {
                ApplyMultiCategoryFilter(allElementsCollector, doc, request.CategoryNames);
            }

            var elementsByLevel = allElementsCollector.ToList()
                .GroupBy(e => e.LevelId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var level in levels)
            {
                var zone = new ZoneInfo { ZoneName = level.Name };

                if (elementsByLevel.TryGetValue(level.Id, out var elementsOnLevel))
                {
                    zone.ElementCount = elementsOnLevel.Count;

                    zone.CategoryBreakdown = elementsOnLevel
                        .GroupBy(e => e.Category?.Name ?? "(None)")
                        .Select(g => new CategoryCountItem { CategoryName = g.Key, Count = g.Count() })
                        .OrderByDescending(c => c.Count)
                        .ToList();
                }

                zone.BBoxMin = new List<double> { -100000, -100000, level.Elevation };
                zone.BBoxMax = new List<double> { 100000, 100000, level.Elevation + 20 };

                response.Zones.Add(zone);
                response.TotalElements += zone.ElementCount;
            }
        }

        response.ZoneCount = response.Zones.Count;
        response.TimingMs = sw.Elapsed.TotalMilliseconds;
        return response;
    }

    // ═══════════════════════════════════════
    // B-7: Element Distance Matrix
    // ═══════════════════════════════════════

    internal ElementDistanceResponse ComputeDistances(PlatformServices services, Document doc, ElementDistanceRequest request)
    {
        request ??= new ElementDistanceRequest();
        var sw = Stopwatch.StartNew();
        var response = new ElementDistanceResponse { DocumentKey = services.GetDocumentKey(doc) };

        if (request.ElementIds == null || request.ElementIds.Count < 2)
        {
            response.TimingMs = sw.Elapsed.TotalMilliseconds;
            return response;
        }

        // Compute centroids
        var centroids = new List<(int id, XYZ center)>();
        foreach (var eid in request.ElementIds.Take(100)) // Cap at 100 to avoid O(n^2) explosion
        {
            try
            {
                var elem = doc.GetElement(new ElementId((long)eid));
                if (elem == null) continue;
                var bbox = elem.get_BoundingBox(null);
                if (bbox == null) continue;
                centroids.Add((eid, new XYZ(
                    (bbox.Min.X + bbox.Max.X) / 2,
                    (bbox.Min.Y + bbox.Max.Y) / 2,
                    (bbox.Min.Z + bbox.Max.Z) / 2)));
            }
            catch { /* skip */ }
        }

        var allDistances = new List<DistancePair>();
        for (int i = 0; i < centroids.Count; i++)
        {
            for (int j = i + 1; j < centroids.Count; j++)
            {
                var dist = centroids[i].center.DistanceTo(centroids[j].center);
                allDistances.Add(new DistancePair
                {
                    ElementIdA = centroids[i].id,
                    ElementIdB = centroids[j].id,
                    Distance = dist
                });
            }
        }

        response.Pairs = allDistances.OrderBy(p => p.Distance).Take(Math.Max(1, request.MaxPairs)).ToList();
        response.PairCount = response.Pairs.Count;

        if (allDistances.Count > 0)
        {
            response.MinDistance = allDistances.Min(p => p.Distance);
            response.MaxDistance = allDistances.Max(p => p.Distance);
            response.AvgDistance = allDistances.Average(p => p.Distance);
        }

        response.TimingMs = sw.Elapsed.TotalMilliseconds;
        return response;
    }

    // ═══════════════════════════════════════
    // B-8: Level Zone Analysis
    // ═══════════════════════════════════════

    internal LevelZoneAnalysisResponse AnalyzeLevelZones(PlatformServices services, Document doc, LevelZoneAnalysisRequest request)
    {
        request ??= new LevelZoneAnalysisRequest();
        var sw = Stopwatch.StartNew();
        var response = new LevelZoneAnalysisResponse { DocumentKey = services.GetDocumentKey(doc) };

        var levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .ToList();

        var allElements = new FilteredElementCollector(doc);
        allElements.WhereElementIsNotElementType();
        if (request.CategoryNames != null && request.CategoryNames.Count > 0)
        {
            ApplyMultiCategoryFilter(allElements, doc, request.CategoryNames);
        }

        var elementsByLevel = allElements.ToList()
            .GroupBy(e => e.LevelId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var level in levels)
        {
            var levelInfo = new LevelZoneInfo
            {
                LevelName = level.Name,
                Elevation = level.Elevation
            };

            if (elementsByLevel.TryGetValue(level.Id, out var elems))
            {
                levelInfo.ElementCount = elems.Count;
                if (request.IncludeBreakdown)
                {
                    levelInfo.CategoryBreakdown = elems
                        .GroupBy(e => e.Category?.Name ?? "(None)")
                        .Select(g => new CategoryCountItem { CategoryName = g.Key, Count = g.Count() })
                        .OrderByDescending(c => c.Count)
                        .ToList();
                }
            }

            response.Levels.Add(levelInfo);
            response.TotalElements += levelInfo.ElementCount;
        }

        response.LevelCount = response.Levels.Count;
        response.TimingMs = sw.Elapsed.TotalMilliseconds;
        return response;
    }

    // ═══════════════════════════════════════
    // B-9: Opening Detection
    // ═══════════════════════════════════════

    internal OpeningDetectResponse DetectOpenings(PlatformServices services, Document doc, OpeningDetectRequest request)
    {
        request ??= new OpeningDetectRequest();
        var sw = Stopwatch.StartNew();
        var response = new OpeningDetectResponse { DocumentKey = services.GetDocumentKey(doc) };

        IEnumerable<Element> hosts;
        if (request.HostElementIds != null && request.HostElementIds.Count > 0)
        {
            hosts = request.HostElementIds
                .Select(id => doc.GetElement(new ElementId((long)id)))
                .Where(e => e != null)!;
        }
        else
        {
            var collector = new FilteredElementCollector(doc);
            collector.WhereElementIsNotElementType();
            if (request.HostCategoryNames != null && request.HostCategoryNames.Count > 0)
            {
                ApplyMultiCategoryFilter(collector, doc, request.HostCategoryNames);
            }
            else
            {
                // Default: walls, floors, ceilings
                var defaultCats = new List<ElementFilter>
                {
                    new ElementCategoryFilter(BuiltInCategory.OST_Walls),
                    new ElementCategoryFilter(BuiltInCategory.OST_Floors),
                    new ElementCategoryFilter(BuiltInCategory.OST_Ceilings)
                };
                collector.WherePasses(new LogicalOrFilter(defaultCats));
            }
            hosts = collector.ToList();
        }

        // PERF: Collect ALL doors+windows 1 lần, group by Host.Id thay vì tạo collector mới cho mỗi host
        var allInsertsByHost = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .WherePasses(new LogicalOrFilter(new List<ElementFilter>
            {
                new ElementCategoryFilter(BuiltInCategory.OST_Doors),
                new ElementCategoryFilter(BuiltInCategory.OST_Windows)
            }))
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(fi => fi.Host != null)
            .GroupBy(fi => fi.Host.Id.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var processedHosts = new HashSet<long>();
        foreach (var host in hosts.Take(Math.Max(1, request.MaxResults)))
        {
            if (!processedHosts.Add(host.Id.Value)) continue;

            // Lookup inserts từ pre-computed dictionary
            if (allInsertsByHost.TryGetValue(host.Id.Value, out var inserts))
            {
                foreach (var fi in inserts)
                {
                    try
                    {
                        var typeId = fi.GetTypeId();
                        var type = typeId != ElementId.InvalidElementId ? doc.GetElement(typeId) as ElementType : null;

                        response.Openings.Add(new OpeningInfo
                        {
                            HostElementId = checked((int)host.Id.Value),
                            HostCategory = host.Category?.Name ?? string.Empty,
                            InsertElementId = checked((int)fi.Id.Value),
                            InsertType = fi.Category?.Name?.ToLowerInvariant().Contains("door") == true ? "door"
                                       : fi.Category?.Name?.ToLowerInvariant().Contains("window") == true ? "window"
                                       : "other",
                            InsertFamilyName = type?.FamilyName ?? string.Empty,
                            InsertTypeName = type?.Name ?? string.Empty
                        });
                    }
                    catch { /* skip */ }
                }
            }
        }

        response.HostCount = processedHosts.Count;
        response.OpeningCount = response.Openings.Count;
        response.TimingMs = sw.Elapsed.TotalMilliseconds;
        return response;
    }

    // ═══════════════════════════════════════
    // B-10: DirectShape Create (mutation - wraps in preview service for dry-run)
    // ═══════════════════════════════════════

    internal ExecutionResult PreviewDirectShapeCreate(PlatformServices services, Document doc, DirectShapeCreateRequest request, Contracts.Bridge.ToolRequestEnvelope envelope)
    {
        request ??= new DirectShapeCreateRequest();
        var result = new ExecutionResult { OperationName = "directshape.create_safe" };

        var desc = $"Will create DirectShape '{request.Name}' ({request.ShapeType}) in category '{request.CategoryName}'";
        if (request.Min.Count >= 3 && request.Max.Count >= 3)
        {
            desc += $" from ({request.Min[0]:F2}, {request.Min[1]:F2}, {request.Min[2]:F2}) to ({request.Max[0]:F2}, {request.Max[1]:F2}, {request.Max[2]:F2})";
        }

        result.Diagnostics.Add(new Contracts.Common.DiagnosticRecord { Code = "PREVIEW", Message = desc, Severity = Contracts.Common.DiagnosticSeverity.Info });
        return result;
    }

    internal ExecutionResult ExecuteDirectShapeCreate(PlatformServices services, Document doc, DirectShapeCreateRequest request)
    {
        request ??= new DirectShapeCreateRequest();
        var result = new ExecutionResult { OperationName = "directshape.create_safe" };

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::directshape.create_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Create DirectShape");
        transaction.Start();

        try
        {
            var categoryId = ResolveCategoryId(doc, request.CategoryName) ?? new ElementId(BuiltInCategory.OST_GenericModel);
            var ds = DirectShape.CreateElement(doc, categoryId);
            ds.Name = request.Name ?? "BIM765T_Marker";

            if (string.Equals(request.ShapeType, "box", StringComparison.OrdinalIgnoreCase)
                && request.Min.Count >= 3 && request.Max.Count >= 3)
            {
                var solid = CreateBoxSolid(
                    new XYZ(request.Min[0], request.Min[1], request.Min[2]),
                    new XYZ(request.Max[0], request.Max[1], request.Max[2]));
                if (solid != null)
                {
                    ds.SetShape(new GeometryObject[] { solid });
                }
            }

            doc.Regenerate();
            transaction.Commit();
            group.Assimilate();

            result.ChangedIds.Add(checked((int)ds.Id.Value));
            result.Diagnostics.Add(new Contracts.Common.DiagnosticRecord
            {
                Code = "CREATED",
                Message = $"Created DirectShape '{ds.Name}' (Id: {ds.Id.Value})",
                Severity = Contracts.Common.DiagnosticSeverity.Info
            });
        }
        catch (Exception ex)
        {
            if (transaction.HasStarted()) transaction.RollBack();
            if (group.HasStarted()) group.RollBack();
            result.Diagnostics.Add(new Contracts.Common.DiagnosticRecord
            {
                Code = "CREATE_FAILED",
                Message = $"DirectShape creation failed: {ex.Message}",
                Severity = Contracts.Common.DiagnosticSeverity.Error
            });
        }

        return result;
    }

    // ═══════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════

    private static List<Element> ResolveElements(Document doc, List<string>? categoryNames, List<int>? elementIds)
    {
        if (elementIds != null && elementIds.Count > 0)
        {
            return elementIds
                .Select(id => doc.GetElement(new ElementId((long)id)))
                .Where(e => e != null)
                .ToList()!;
        }

        if (categoryNames != null && categoryNames.Count > 0)
        {
            var collector = new FilteredElementCollector(doc);
            collector.WhereElementIsNotElementType();
            ApplyMultiCategoryFilter(collector, doc, categoryNames);
            return collector.ToList();
        }

        return new List<Element>();
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
        if (bics.Count == 1) collector.OfCategory(bics[0]);
        else if (bics.Count > 1) collector.WherePasses(new ElementMulticategoryFilter(bics));
    }

    private static BuiltInCategory? ResolveCategoryByName(Document doc, string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return null;
        if (Enum.TryParse<BuiltInCategory>("OST_" + categoryName.Replace(" ", ""), true, out var bic)) return bic;
        foreach (Category cat in doc.Settings.Categories)
        {
            if (string.Equals(cat.Name, categoryName, StringComparison.OrdinalIgnoreCase))
            {
                var val = cat.Id.Value;
                if (val >= int.MinValue && val <= int.MaxValue)
                    return (BuiltInCategory)(int)val;
                return null;
            }
        }
        return null;
    }

    private static ElementId? ResolveCategoryId(Document doc, string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return null;
        var bic = ResolveCategoryByName(doc, categoryName!);
        return bic.HasValue ? new ElementId(bic.Value) : null;
    }

    private static bool BBoxIntersects(XYZ minA, XYZ maxA, XYZ minB, XYZ maxB)
    {
        return minA.X <= maxB.X && maxA.X >= minB.X
            && minA.Y <= maxB.Y && maxA.Y >= minB.Y
            && minA.Z <= maxB.Z && maxA.Z >= minB.Z;
    }

    private static double ComputeBBoxDistance(BoundingBoxXYZ a, BoundingBoxXYZ b)
    {
        var centerA = new XYZ((a.Min.X + a.Max.X) / 2, (a.Min.Y + a.Max.Y) / 2, (a.Min.Z + a.Max.Z) / 2);
        var centerB = new XYZ((b.Min.X + b.Max.X) / 2, (b.Min.Y + b.Max.Y) / 2, (b.Min.Z + b.Max.Z) / 2);
        return centerA.DistanceTo(centerB);
    }

    private static List<double> MidPoint(XYZ minA, XYZ maxA, XYZ minB, XYZ maxB)
    {
        return new List<double>
        {
            (minA.X + maxA.X + minB.X + maxB.X) / 4.0,
            (minA.Y + maxA.Y + minB.Y + maxB.Y) / 4.0,
            (minA.Z + maxA.Z + minB.Z + maxB.Z) / 4.0
        };
    }

    private static List<Solid> ExtractSolids(GeometryElement geomElement)
    {
        var solids = new List<Solid>();
        foreach (var geomObj in geomElement)
        {
            if (geomObj is Solid solid && solid.Volume > 0)
            {
                solids.Add(solid);
            }
            else if (geomObj is GeometryInstance gi)
            {
                var instanceGeom = gi.GetInstanceGeometry();
                if (instanceGeom != null)
                {
                    solids.AddRange(ExtractSolids(instanceGeom));
                }
            }
        }
        return solids;
    }

    private static Solid? CreateBoxSolid(XYZ min, XYZ max)
    {
        try
        {
            var profileLoops = new List<CurveLoop>();
            var loop = new CurveLoop();
            loop.Append(Line.CreateBound(new XYZ(min.X, min.Y, min.Z), new XYZ(max.X, min.Y, min.Z)));
            loop.Append(Line.CreateBound(new XYZ(max.X, min.Y, min.Z), new XYZ(max.X, max.Y, min.Z)));
            loop.Append(Line.CreateBound(new XYZ(max.X, max.Y, min.Z), new XYZ(min.X, max.Y, min.Z)));
            loop.Append(Line.CreateBound(new XYZ(min.X, max.Y, min.Z), new XYZ(min.X, min.Y, min.Z)));
            profileLoops.Add(loop);

            var height = max.Z - min.Z;
            if (height <= 0) height = 1.0;
            return GeometryCreationUtilities.CreateExtrusionGeometry(profileLoops, XYZ.BasisZ, height);
        }
        catch { return null; }
    }

    private static Element? GetLinkedElement(Document doc, Reference reference)
    {
        try
        {
            var linkInstance = doc.GetElement(reference.ElementId) as RevitLinkInstance;
            return linkInstance?.GetLinkDocument()?.GetElement(reference.LinkedElementId);
        }
        catch { return null; }
    }

    private static string GetLinkName(Document doc, Reference reference)
    {
        try
        {
            var linkInstance = doc.GetElement(reference.ElementId) as RevitLinkInstance;
            return linkInstance?.Name ?? string.Empty;
        }
        catch { return string.Empty; }
    }
}
