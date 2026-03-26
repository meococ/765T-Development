using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed partial class PlatformServices
{
    internal CadGenericModelOverlapResponse ReviewCadGenericModelOverlap(UIApplication uiapp, Document doc, CadGenericModelOverlapRequest request, string requestedView)
    {
        request ??= new CadGenericModelOverlapRequest();

        var resolvedViewName = string.IsNullOrWhiteSpace(request.ViewName) ? requestedView : request.ViewName;
        var view = ResolveView(uiapp, doc, resolvedViewName, request.ViewId);
        var projection = ViewProjection.Create(view);

        var response = new CadGenericModelOverlapResponse
        {
            DocumentKey = GetDocumentKey(doc),
            ViewKey = GetViewKey(view),
            ViewId = checked((int)view.Id.Value),
            ViewName = view.Name,
            ToleranceFeet = request.ToleranceFeet,
            SamplingStepFeet = request.SamplingStepFeet
        };

        var cadImports = CollectCadImports(doc, view, request).ToList();
        var genericModels = CollectGenericModels(doc, view, request).ToList();

        response.ImportCadCount = cadImports.Count;
        response.GenericModelCount = genericModels.Count;
        response.ImportCadScopeTruncated = response.ImportCadCount > request.MaxElementsPerSide;
        response.GenericModelScopeTruncated = response.GenericModelCount > request.MaxElementsPerSide;

        var cadSnapshot = BuildProjectedSnapshot(doc, view, cadImports.Take(request.MaxElementsPerSide), request, projection);
        var genericSnapshot = BuildProjectedSnapshot(doc, view, genericModels.Take(request.MaxElementsPerSide), request, projection);

        response.ProcessedImportCadCount = cadSnapshot.ProcessedElementCount;
        response.ProcessedGenericModelCount = genericSnapshot.ProcessedElementCount;
        response.CadSampleLimitHit = cadSnapshot.SampleLimitHit;
        response.GenericModelSampleLimitHit = genericSnapshot.SampleLimitHit;
        response.CadSamplePointCount = cadSnapshot.PointKeys.Count;
        response.GenericModelSamplePointCount = genericSnapshot.PointKeys.Count;
        response.CadProjectedBounds = cadSnapshot.Bounds.ToDto();
        response.GenericModelProjectedBounds = genericSnapshot.Bounds.ToDto();
        response.CadElements = cadSnapshot.Elements;
        response.GenericModelElements = genericSnapshot.Elements;

        var sharedKeys = new HashSet<string>(cadSnapshot.PointKeys, StringComparer.Ordinal);
        sharedKeys.IntersectWith(genericSnapshot.PointKeys);
        response.SharedSamplePointCount = sharedKeys.Count;

        var cadOnlyKeys = new HashSet<string>(cadSnapshot.PointKeys, StringComparer.Ordinal);
        cadOnlyKeys.ExceptWith(genericSnapshot.PointKeys);
        response.CadOnlySamplePointCount = cadOnlyKeys.Count;
        response.CadOnlyPreviewPoints = cadOnlyKeys
            .Take(Math.Max(0, request.MaxPreviewPoints))
            .Select(x => ProjectedPoint2dDtoFromKey(x, request.ToleranceFeet))
            .ToList();

        var genericOnlyKeys = new HashSet<string>(genericSnapshot.PointKeys, StringComparer.Ordinal);
        genericOnlyKeys.ExceptWith(cadSnapshot.PointKeys);
        response.GenericModelOnlySamplePointCount = genericOnlyKeys.Count;
        response.GenericModelOnlyPreviewPoints = genericOnlyKeys
            .Take(Math.Max(0, request.MaxPreviewPoints))
            .Select(x => ProjectedPoint2dDtoFromKey(x, request.ToleranceFeet))
            .ToList();

        var unionCount = cadSnapshot.PointKeys.Count + genericSnapshot.PointKeys.Count - sharedKeys.Count;
        response.OverlapRatio = unionCount > 0 ? (double)sharedKeys.Count / unionCount : 0;
        response.CadCoverageRatio = cadSnapshot.PointKeys.Count > 0 ? (double)sharedKeys.Count / cadSnapshot.PointKeys.Count : 0;
        response.GenericModelCoverageRatio = genericSnapshot.PointKeys.Count > 0 ? (double)sharedKeys.Count / genericSnapshot.PointKeys.Count : 0;

        var comparisonBlockedByScope = response.ImportCadScopeTruncated
            || response.GenericModelScopeTruncated
            || response.CadSampleLimitHit
            || response.GenericModelSampleLimitHit;

        response.IsExactMatch =
            !comparisonBlockedByScope
            && response.ImportCadCount > 0
            && response.GenericModelCount > 0
            && response.CadOnlySamplePointCount == 0
            && response.GenericModelOnlySamplePointCount == 0;

        response.Status = ResolveCadGenericCompareStatus(response);
        response.Summary = BuildCadGenericCompareSummary(response);
        response.Review = BuildCadGenericCompareReview(response);

        return response;
    }

    private static IEnumerable<ImportInstance> CollectCadImports(Document doc, View view, CadGenericModelOverlapRequest request)
    {
        var collector = new FilteredElementCollector(doc, view.Id)
            .OfClass(typeof(ImportInstance))
            .WhereElementIsNotElementType()
            .Cast<ImportInstance>();

        if (!string.IsNullOrWhiteSpace(request.ImportNameContains))
        {
            collector = collector.Where(x => ContainsIgnoreCase(x.Name, request.ImportNameContains));
        }

        return collector.OrderBy(x => x.Name).ThenBy(x => x.Id.Value);
    }

    private static IEnumerable<Element> CollectGenericModels(Document doc, View view, CadGenericModelOverlapRequest request)
    {
        var collector = new FilteredElementCollector(doc, view.Id)
            .OfCategory(BuiltInCategory.OST_GenericModel)
            .WhereElementIsNotElementType()
            .ToElements();

        return collector
            .Where(x => MatchesGenericModelFilters(doc, x, request))
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id.Value);
    }

    private static bool MatchesGenericModelFilters(Document doc, Element element, CadGenericModelOverlapRequest request)
    {
        var type = ResolveElementType(doc, element);
        var instanceName = element.Name ?? string.Empty;
        var typeName = type?.Name ?? string.Empty;
        var familyName = ResolveFamilyName(type);

        if (!string.IsNullOrWhiteSpace(request.GenericModelNameContains)
            && !ContainsIgnoreCase(instanceName, request.GenericModelNameContains)
            && !ContainsIgnoreCase(typeName, request.GenericModelNameContains))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.GenericModelFamilyNameContains)
            && !ContainsIgnoreCase(familyName, request.GenericModelFamilyNameContains))
        {
            return false;
        }

        return true;
    }

    private static CadGenericSideSnapshot BuildProjectedSnapshot(
        Document doc,
        View view,
        IEnumerable<Element> elements,
        CadGenericModelOverlapRequest request,
        ViewProjection projection)
    {
        var options = new Options
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = false,
            DetailLevel = ViewDetailLevel.Fine,
            View = view
        };

        var snapshot = new CadGenericSideSnapshot(request.MaxSamplePointsPerSide);

        foreach (var element in elements)
        {
            if (snapshot.SampleLimitHit)
            {
                break;
            }

            var localCollector = new ProjectedPointCollector(projection, request.ToleranceFeet);
            var geometry = SafeValue(() => element.get_Geometry(options), null);

            if (geometry != null)
            {
                var addedWithoutSolids = CollectProjectedPoints(geometry, Transform.Identity, localCollector, request.SamplingStepFeet, includeSolidEdges: false);
                if (addedWithoutSolids == 0)
                {
                    CollectProjectedPoints(geometry, Transform.Identity, localCollector, request.SamplingStepFeet, includeSolidEdges: true);
                }
            }

            if (localCollector.Count == 0)
            {
                AddBoundingBoxFallbackPoints(element, view, localCollector);
            }

            if (localCollector.Count == 0)
            {
                continue;
            }

            snapshot.AddElement(BuildElementDigest(doc, element, localCollector));
            snapshot.Merge(localCollector);
        }

        return snapshot;
    }

    private static CadGenericElementDigestDto BuildElementDigest(Document doc, Element element, ProjectedPointCollector collector)
    {
        var type = ResolveElementType(doc, element);

        return new CadGenericElementDigestDto
        {
            ElementId = checked((int)element.Id.Value),
            CategoryName = element.Category?.Name ?? string.Empty,
            ClassName = element.GetType().Name,
            Name = element.Name ?? string.Empty,
            FamilyName = ResolveFamilyName(type),
            TypeName = type?.Name ?? string.Empty,
            SamplePointCount = collector.Count,
            ProjectedBounds = collector.Bounds.ToDto()
        };
    }

    private static ElementType? ResolveElementType(Document doc, Element element)
    {
        try
        {
            var typeId = element.GetTypeId();
            if (typeId == ElementId.InvalidElementId)
            {
                return null;
            }

            return doc.GetElement(typeId) as ElementType;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveFamilyName(ElementType? type)
    {
        if (type is FamilySymbol symbol)
        {
            return symbol.Family?.Name ?? type.FamilyName ?? string.Empty;
        }

        return type?.FamilyName ?? string.Empty;
    }

    private static int CollectProjectedPoints(GeometryElement geometry, Transform transform, ProjectedPointCollector collector, double samplingStepFeet, bool includeSolidEdges)
    {
        var added = 0;
        foreach (GeometryObject geometryObject in geometry)
        {
            added += CollectProjectedPoints(geometryObject, transform, collector, samplingStepFeet, includeSolidEdges);
        }

        return added;
    }

    private static int CollectProjectedPoints(GeometryObject geometryObject, Transform transform, ProjectedPointCollector collector, double samplingStepFeet, bool includeSolidEdges)
    {
        if (geometryObject is GeometryInstance instance)
        {
            var instanceGeometry = SafeGetInstanceGeometry(instance);
            if (instanceGeometry == null)
            {
                return 0;
            }

            return CollectProjectedPoints(instanceGeometry, transform.Multiply(instance.Transform), collector, samplingStepFeet, includeSolidEdges);
        }

        if (geometryObject is Curve curve)
        {
            return SampleCurve(curve, transform, collector, samplingStepFeet);
        }

        if (geometryObject is PolyLine polyLine)
        {
            return SamplePolyLine(polyLine, transform, collector, samplingStepFeet);
        }

        if (includeSolidEdges && geometryObject is Solid solid && solid.Edges != null && solid.Edges.Size > 0)
        {
            var added = 0;
            foreach (Edge edge in solid.Edges)
            {
                var edgeCurve = SafeValue(edge.AsCurve, null);
                if (edgeCurve == null)
                {
                    continue;
                }

                added += SampleCurve(edgeCurve, transform, collector, samplingStepFeet);
            }

            return added;
        }

        return 0;
    }

    private static GeometryElement? SafeGetInstanceGeometry(GeometryInstance instance)
    {
        try
        {
            return instance.GetInstanceGeometry();
        }
        catch
        {
            return null;
        }
    }

    private static int SampleCurve(Curve curve, Transform transform, ProjectedPointCollector collector, double samplingStepFeet)
    {
        try
        {
            if (!curve.IsBound)
            {
                return SampleCurveFromTessellation(curve, transform, collector, samplingStepFeet);
            }

            var length = SafeValue(() => curve.Length, 0.0);
            if (length <= 0)
            {
                return SampleCurveFromTessellation(curve, transform, collector, samplingStepFeet);
            }

            var segmentCount = Math.Max(1, (int)Math.Ceiling(length / samplingStepFeet));
            segmentCount = Math.Min(segmentCount, 2048);

            var added = 0;
            for (var i = 0; i <= segmentCount; i++)
            {
                var parameter = (double)i / segmentCount;
                var point = curve.Evaluate(parameter, true);
                if (collector.Add(transform.OfPoint(point)))
                {
                    added++;
                }
            }

            return added;
        }
        catch
        {
            return SampleCurveFromTessellation(curve, transform, collector, samplingStepFeet);
        }
    }

    private static int SampleCurveFromTessellation(Curve curve, Transform transform, ProjectedPointCollector collector, double samplingStepFeet)
    {
        var tessellated = SafeValue(curve.Tessellate, null);
        if (tessellated == null || tessellated.Count == 0)
        {
            return 0;
        }

        var added = 0;
        for (var i = 0; i < tessellated.Count - 1; i++)
        {
            added += SampleSegment(tessellated[i], tessellated[i + 1], transform, collector, samplingStepFeet);
        }

        if (tessellated.Count == 1 && collector.Add(transform.OfPoint(tessellated[0])))
        {
            added++;
        }

        return added;
    }

    private static int SamplePolyLine(PolyLine polyLine, Transform transform, ProjectedPointCollector collector, double samplingStepFeet)
    {
        var coordinates = SafeValue(polyLine.GetCoordinates, null);
        if (coordinates == null || coordinates.Count == 0)
        {
            return 0;
        }

        var added = 0;
        if (coordinates.Count == 1)
        {
            if (collector.Add(transform.OfPoint(coordinates[0])))
            {
                added++;
            }

            return added;
        }

        for (var i = 0; i < coordinates.Count - 1; i++)
        {
            added += SampleSegment(coordinates[i], coordinates[i + 1], transform, collector, samplingStepFeet);
        }

        return added;
    }

    private static int SampleSegment(XYZ start, XYZ end, Transform transform, ProjectedPointCollector collector, double samplingStepFeet)
    {
        var length = start.DistanceTo(end);
        var segmentCount = Math.Max(1, (int)Math.Ceiling(length / samplingStepFeet));
        segmentCount = Math.Min(segmentCount, 2048);

        var added = 0;
        for (var i = 0; i <= segmentCount; i++)
        {
            var t = (double)i / segmentCount;
            var point = start + ((end - start) * t);
            if (collector.Add(transform.OfPoint(point)))
            {
                added++;
            }
        }

        return added;
    }

    private static void AddBoundingBoxFallbackPoints(Element element, View view, ProjectedPointCollector collector)
    {
        var bbox = element.get_BoundingBox(view) ?? element.get_BoundingBox(null);
        if (bbox == null)
        {
            return;
        }

        collector.Add(bbox.Min);
        collector.Add(new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z));
        collector.Add(new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z));
        collector.Add(new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z));
        collector.Add(new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Max.Z));
        collector.Add(new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Max.Z));
        collector.Add(bbox.Max);
        collector.Add(new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Max.Z));
    }

    private static ProjectedPoint2dDto ProjectedPoint2dDtoFromKey(string key, double toleranceFeet)
    {
        var separator = key.IndexOf('|');
        if (separator <= 0 || separator >= key.Length - 1)
        {
            return new ProjectedPoint2dDto();
        }

        var uIndex = long.Parse(key.Substring(0, separator), CultureInfo.InvariantCulture);
        var vIndex = long.Parse(key.Substring(separator + 1), CultureInfo.InvariantCulture);

        return new ProjectedPoint2dDto
        {
            U = uIndex * toleranceFeet,
            V = vIndex * toleranceFeet
        };
    }

    private static string ResolveCadGenericCompareStatus(CadGenericModelOverlapResponse response)
    {
        if (response.ImportCadCount == 0 && response.GenericModelCount == 0)
        {
            return "empty_scope";
        }

        if (response.ImportCadCount == 0)
        {
            return "no_import_cad";
        }

        if (response.GenericModelCount == 0)
        {
            return "no_generic_model";
        }

        if (response.ImportCadScopeTruncated
            || response.GenericModelScopeTruncated
            || response.CadSampleLimitHit
            || response.GenericModelSampleLimitHit)
        {
            return "partial_compare";
        }

        return response.IsExactMatch ? "exact_match" : "different";
    }

    private static string BuildCadGenericCompareSummary(CadGenericModelOverlapResponse response)
    {
        if (string.Equals(response.Status, "empty_scope", StringComparison.Ordinal))
        {
            return "Không thấy CAD import hay Generic Model nào trong view để so sánh.";
        }

        if (string.Equals(response.Status, "no_import_cad", StringComparison.Ordinal))
        {
            return "Không thấy CAD import nào trong view hiện tại theo scope/filter đã chọn.";
        }

        if (string.Equals(response.Status, "no_generic_model", StringComparison.Ordinal))
        {
            return "Không thấy Generic Model nào trong view hiện tại theo scope/filter đã chọn.";
        }

        var summary = string.Format(
            CultureInfo.InvariantCulture,
            "Overlap={0:P2}; CAD coverage={1:P2}; Generic Model coverage={2:P2}; CAD-only={3}; Generic-only={4}.",
            response.OverlapRatio,
            response.CadCoverageRatio,
            response.GenericModelCoverageRatio,
            response.CadOnlySamplePointCount,
            response.GenericModelOnlySamplePointCount);

        if (string.Equals(response.Status, "exact_match", StringComparison.Ordinal))
        {
            return "CAD import và Generic Model khớp 100% theo projected point-cloud trong view hiện tại. " + summary;
        }

        if (string.Equals(response.Status, "partial_compare", StringComparison.Ordinal))
        {
            return "So sánh mới chạy trên scope/sample bị cắt bớt; chưa thể khẳng định 100%. " + summary;
        }

        return "CAD import và Generic Model đang khác nhau trong view hiện tại. " + summary;
    }

    private static ReviewReport BuildCadGenericCompareReview(CadGenericModelOverlapResponse response)
    {
        var review = new ReviewReport
        {
            Name = "cad_generic_model_overlap",
            DocumentKey = response.DocumentKey,
            ViewKey = response.ViewKey
        };

        if (response.ImportCadCount == 0)
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = "CAD_IMPORT_NOT_FOUND",
                Severity = DiagnosticSeverity.Warning,
                Message = "Không tìm thấy CAD import nào trong view theo scope/filter hiện tại."
            });
        }

        if (response.GenericModelCount == 0)
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = "GENERIC_MODEL_NOT_FOUND",
                Severity = DiagnosticSeverity.Warning,
                Message = "Không tìm thấy Generic Model nào trong view theo scope/filter hiện tại."
            });
        }

        if (response.ImportCadScopeTruncated)
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = "CAD_SCOPE_TRUNCATED",
                Severity = DiagnosticSeverity.Warning,
                Message = "Số lượng CAD import vượt MaxElementsPerSide; kết quả mới phản ánh phần đầu scope."
            });
        }

        if (response.GenericModelScopeTruncated)
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = "GENERIC_MODEL_SCOPE_TRUNCATED",
                Severity = DiagnosticSeverity.Warning,
                Message = "Số lượng Generic Model vượt MaxElementsPerSide; kết quả mới phản ánh phần đầu scope."
            });
        }

        if (response.CadSampleLimitHit)
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = "CAD_SAMPLE_LIMIT_HIT",
                Severity = DiagnosticSeverity.Warning,
                Message = "CAD import chạm MaxSamplePointsPerSide; cần tăng giới hạn nếu muốn kết luận chắc chắn."
            });
        }

        if (response.GenericModelSampleLimitHit)
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = "GENERIC_MODEL_SAMPLE_LIMIT_HIT",
                Severity = DiagnosticSeverity.Warning,
                Message = "Generic Model chạm MaxSamplePointsPerSide; cần tăng giới hạn nếu muốn kết luận chắc chắn."
            });
        }

        if (!response.IsExactMatch
            && response.ImportCadCount > 0
            && response.GenericModelCount > 0
            && !string.Equals(response.Status, "partial_compare", StringComparison.Ordinal))
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = "CAD_GENERIC_MISMATCH",
                Severity = DiagnosticSeverity.Warning,
                Message = response.Summary
            });
        }

        review.IssueCount = review.Issues.Count;
        return review;
    }

    private static bool ContainsIgnoreCase(string source, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(source)
            && source.IndexOf(expected.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private sealed class ViewProjection
    {
        private ViewProjection(XYZ right, XYZ up, XYZ viewDirection)
        {
            Right = right;
            Up = up;
            ViewDirection = viewDirection;
        }

        internal XYZ Right { get; }
        internal XYZ Up { get; }
        internal XYZ ViewDirection { get; }

        internal static ViewProjection Create(View view)
        {
            if (view is View3D view3D && view3D.IsPerspective)
            {
                throw new InvalidOperationException("Tool này chỉ hỗ trợ orthographic views; perspective 3D chưa được hỗ trợ.");
            }

            try
            {
                var right = view.RightDirection.Normalize();
                var up = view.UpDirection.Normalize();
                var direction = view.ViewDirection.Normalize();

                if (right.GetLength() < 1e-9 || up.GetLength() < 1e-9 || direction.GetLength() < 1e-9)
                {
                    throw new InvalidOperationException();
                }

                return new ViewProjection(right, up, direction);
            }
            catch
            {
                throw new InvalidOperationException("Tool này cần một graphical view hợp lệ để project geometry trong view hiện tại.");
            }
        }

        internal ProjectedPoint Project(XYZ point)
        {
            return new ProjectedPoint(
                point.DotProduct(Right),
                point.DotProduct(Up),
                point.DotProduct(ViewDirection));
        }
    }

    private sealed class ProjectedPointCollector
    {
        private readonly ViewProjection _projection;
        private readonly double _quantum;

        internal ProjectedPointCollector(ViewProjection projection, double quantum)
        {
            _projection = projection;
            _quantum = quantum;
        }

        internal HashSet<string> Keys { get; } = new HashSet<string>(StringComparer.Ordinal);
        internal ProjectedBoundsAccumulator Bounds { get; } = new ProjectedBoundsAccumulator();
        internal int Count => Keys.Count;

        internal bool Add(XYZ point)
        {
            var projected = _projection.Project(point);
            var uIndex = Quantize(projected.U, _quantum);
            var vIndex = Quantize(projected.V, _quantum);
            var key = uIndex.ToString(CultureInfo.InvariantCulture) + "|" + vIndex.ToString(CultureInfo.InvariantCulture);
            if (!Keys.Add(key))
            {
                return false;
            }

            Bounds.Include(projected.U, projected.V);
            return true;
        }

        private static long Quantize(double value, double quantum)
        {
            return checked((long)Math.Round(value / quantum, MidpointRounding.AwayFromZero));
        }
    }

    private sealed class CadGenericSideSnapshot
    {
        private readonly int _maxSamplePoints;

        internal CadGenericSideSnapshot(int maxSamplePoints)
        {
            _maxSamplePoints = maxSamplePoints;
        }

        internal HashSet<string> PointKeys { get; } = new HashSet<string>(StringComparer.Ordinal);
        internal ProjectedBoundsAccumulator Bounds { get; } = new ProjectedBoundsAccumulator();
        internal List<CadGenericElementDigestDto> Elements { get; } = new List<CadGenericElementDigestDto>();
        internal int ProcessedElementCount { get; private set; }
        internal bool SampleLimitHit { get; private set; }

        internal void AddElement(CadGenericElementDigestDto digest)
        {
            Elements.Add(digest);
            ProcessedElementCount++;
        }

        internal void Merge(ProjectedPointCollector collector)
        {
            Bounds.Merge(collector.Bounds);

            foreach (var key in collector.Keys)
            {
                if (PointKeys.Count >= _maxSamplePoints)
                {
                    SampleLimitHit = true;
                    break;
                }

                PointKeys.Add(key);
            }
        }
    }

    private sealed class ProjectedBoundsAccumulator
    {
        internal bool HasValue { get; private set; }
        internal double MinU { get; private set; }
        internal double MinV { get; private set; }
        internal double MaxU { get; private set; }
        internal double MaxV { get; private set; }

        internal void Include(double u, double v)
        {
            if (!HasValue)
            {
                MinU = MaxU = u;
                MinV = MaxV = v;
                HasValue = true;
                return;
            }

            if (u < MinU) MinU = u;
            if (u > MaxU) MaxU = u;
            if (v < MinV) MinV = v;
            if (v > MaxV) MaxV = v;
        }

        internal void Merge(ProjectedBoundsAccumulator other)
        {
            if (!other.HasValue)
            {
                return;
            }

            Include(other.MinU, other.MinV);
            Include(other.MaxU, other.MaxV);
        }

        internal ProjectedBoundsDto? ToDto()
        {
            if (!HasValue)
            {
                return null;
            }

            return new ProjectedBoundsDto
            {
                MinU = MinU,
                MinV = MinV,
                MaxU = MaxU,
                MaxV = MaxV,
                Width = MaxU - MinU,
                Height = MaxV - MinV
            };
        }
    }

    private readonly struct ProjectedPoint
    {
        internal ProjectedPoint(double u, double v, double depth)
        {
            U = u;
            V = v;
            Depth = depth;
        }

        internal double U { get; }
        internal double V { get; }
        internal double Depth { get; }
    }
}
