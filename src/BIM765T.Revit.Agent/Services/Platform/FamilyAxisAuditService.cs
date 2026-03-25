using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class FamilyAxisAuditService
{
    internal FamilyAxisAlignmentResponse ReviewFamilyAxisAlignment(UIApplication uiapp, PlatformServices platform, Document doc, FamilyAxisAlignmentRequest request, string requestedView)
    {
        request ??= new FamilyAxisAlignmentRequest();

        var maxElements = Math.Max(1, request.MaxElements);
        var maxIssues = Math.Max(1, request.MaxIssues);
        var tolerance = request.AngleToleranceDegrees > 0 ? request.AngleToleranceDegrees : 5.0;
        var requestedViewName = string.IsNullOrWhiteSpace(request.ViewName) ? requestedView : request.ViewName;
        var documentWideScope = !request.UseActiveViewOnly &&
                                !request.ViewId.HasValue &&
                                string.IsNullOrWhiteSpace(requestedViewName);
        var view = documentWideScope
            ? null
            : platform.ResolveView(uiapp, doc, requestedViewName, request.ViewId);

        var response = new FamilyAxisAlignmentResponse
        {
            DocumentKey = platform.GetDocumentKey(doc),
            ViewKey = documentWideScope ? "document-wide" : platform.GetViewKey(view!),
            ViewId = documentWideScope ? 0 : checked((int)view!.Id.Value),
            ViewName = documentWideScope ? "<document-wide>" : view!.Name,
            AngleToleranceDegrees = tolerance,
            HighlightRequested = request.HighlightInUi
        };

        var review = new ReviewReport
        {
            Name = "family_axis_alignment",
            DocumentKey = response.DocumentKey,
            ViewKey = response.ViewKey
        };

        var collector = (documentWideScope ? new FilteredElementCollector(doc) : new FilteredElementCollector(doc, view!.Id))
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>();

        if (request.CategoryNames != null && request.CategoryNames.Count > 0)
        {
            collector = collector.Where(x => x.Category != null && request.CategoryNames.Any(c => string.Equals(c, x.Category.Name, StringComparison.OrdinalIgnoreCase)));
        }

        var instances = collector.ToList();
        response.TotalFamilyInstances = instances.Count;
        response.Truncated = instances.Count > maxElements;

        var candidateInstances = instances.Take(maxElements).ToList();
        var familyAuditByFamilyId = BuildFamilyAuditCache(doc, candidateInstances, tolerance, request, response, review, maxIssues);

        var mismatchedIds = new List<ElementId>();
        var issueCount = review.Issues.Count;

        foreach (var instance in candidateInstances)
        {
            response.CheckedCount++;
            var item = AnalyzeProjectAxes(instance, tolerance, request.TreatAntiParallelAsMismatch, request.TreatMirroredAsMismatch);
            item.ProjectAxisStatus = item.Status;
            item.ProjectAxisReason = item.Reason;

            var familyId = instance.Symbol?.Family?.Id;
            if (request.AnalyzeNestedFamilies && familyId != null && familyAuditByFamilyId.TryGetValue(checked((int)familyId.Value), out var familyAudit))
            {
                ApplyNestedAudit(item, familyAudit, request);
            }
            else
            {
                item.NeedsReview = !item.MatchesProjectAxes;
            }

            if (item.NeedsReview)
            {
                response.MismatchCount++;
                mismatchedIds.Add(instance.Id);
                if (item.HasNestedTransformRisk || item.HasNestedIfcRisk)
                {
                    response.NestedRiskHostCount++;
                }

                if (issueCount < maxIssues)
                {
                    review.Issues.Add(new ReviewIssue
                    {
                        Code = GetIssueCode(item),
                        Severity = GetIssueSeverity(item),
                        Message = $"{item.FamilyName} / {item.TypeName} (Id {item.ElementId}) - {item.Reason}",
                        ElementId = item.ElementId
                    });
                    issueCount++;
                }
            }
            else
            {
                response.AlignedCount++;
            }

            if (item.NeedsReview || request.IncludeAlignedItems)
            {
                response.Items.Add(item);
            }
        }

        if (request.HighlightInUi)
        {
            if (documentWideScope)
            {
                review.Issues.Add(new ReviewIssue
                {
                    Code = "FAMILY_AXIS_HIGHLIGHT_SKIPPED_DOCUMENT_WIDE",
                    Severity = DiagnosticSeverity.Info,
                    Message = "Highlight bo qua vi review dang chay theo document-wide scope."
                });
            }
            else if (uiapp.ActiveUIDocument?.Document?.Equals(doc) == true && doc.ActiveView?.Id == view!.Id)
            {
                if (mismatchedIds.Count > 0)
                {
                    uiapp.ActiveUIDocument.Selection.SetElementIds(mismatchedIds);
                    response.HighlightApplied = true;
                    response.HighlightedCount = mismatchedIds.Count;

                    if (request.ZoomToHighlighted)
                    {
                        try
                        {
                            uiapp.ActiveUIDocument.ShowElements(mismatchedIds);
                        }
                        catch (Exception ex)
                        {
                            review.Issues.Add(new ReviewIssue
                            {
                                Code = "FAMILY_AXIS_ZOOM_FAILED",
                                Severity = DiagnosticSeverity.Info,
                                Message = "Khong zoom duoc toi cac element dang highlight: " + ex.Message
                            });
                        }
                    }
                }
                else
                {
                    review.Issues.Add(new ReviewIssue
                    {
                        Code = "FAMILY_AXIS_NO_MISMATCH",
                        Severity = DiagnosticSeverity.Info,
                        Message = "Khong co family instance nao lech truc de highlight."
                    });
                }
            }
            else
            {
                review.Issues.Add(new ReviewIssue
                {
                    Code = "FAMILY_AXIS_HIGHLIGHT_SKIPPED",
                    Severity = DiagnosticSeverity.Info,
                    Message = "Highlight bo qua vi target view/document khong phai active UI context."
                });
            }
        }

        if (response.Truncated)
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = "FAMILY_AXIS_TRUNCATED",
                Severity = DiagnosticSeverity.Info,
                Message = $"So family instance trong view ({response.TotalFamilyInstances}) vuot qua MaxElements={maxElements}; ket qua bi cat bot."
            });
        }

        if (documentWideScope)
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = "FAMILY_AXIS_DOCUMENT_WIDE_SCOPE",
                Severity = DiagnosticSeverity.Info,
                Message = $"Dang audit theo document-wide scope. TotalFamilyInstances={response.TotalFamilyInstances}."
            });
        }

        if (response.NestedAnalysisTruncated)
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = "FAMILY_AXIS_NESTED_ANALYSIS_TRUNCATED",
                Severity = DiagnosticSeverity.Info,
                Message = $"Nested family analysis bi gioi han boi MaxFamilyDefinitionsToInspect={Math.Max(1, request.MaxFamilyDefinitionsToInspect)} hoac MaxNestedInstancesPerFamily={Math.Max(1, request.MaxNestedInstancesPerFamily)}."
            });
        }

        review.IssueCount = review.Issues.Count;
        response.Review = review;
        return response;
    }

    private static Dictionary<int, FamilyDefinitionAuditSummary> BuildFamilyAuditCache(
        Document doc,
        IReadOnlyCollection<FamilyInstance> candidateInstances,
        double toleranceDegrees,
        FamilyAxisAlignmentRequest request,
        FamilyAxisAlignmentResponse response,
        ReviewReport review,
        int maxIssues)
    {
        var cache = new Dictionary<int, FamilyDefinitionAuditSummary>();
        if (!request.AnalyzeNestedFamilies)
        {
            return cache;
        }

        var distinctFamilies = candidateInstances
            .Select(x => x.Symbol?.Family)
            .Where(x => x != null)
            .GroupBy(x => checked((int)x!.Id.Value))
            .Select(x => x.First()!)
            .ToList();

        response.DistinctFamilyDefinitions = distinctFamilies.Count;
        var maxFamilies = Math.Max(1, request.MaxFamilyDefinitionsToInspect);
        response.NestedAnalysisTruncated = distinctFamilies.Count > maxFamilies;

        foreach (var family in distinctFamilies.Take(maxFamilies))
        {
            var summary = AnalyzeFamilyDefinition(doc, family, toleranceDegrees, request);
            cache[checked((int)family.Id.Value)] = summary;
            response.AnalyzedFamilyDefinitions++;

            if ((summary.AnalysisFailed || summary.AnalysisSkipped) && review.Issues.Count < maxIssues)
            {
                review.Issues.Add(new ReviewIssue
                {
                    Code = summary.AnalysisFailed ? "FAMILY_AXIS_NESTED_ANALYSIS_FAILED" : "FAMILY_AXIS_NESTED_ANALYSIS_SKIPPED",
                    Severity = DiagnosticSeverity.Info,
                    Message = $"{family.Name}: {summary.SummaryMessage}"
                });
            }

            if (summary.Truncated)
            {
                response.NestedAnalysisTruncated = true;
            }
        }

        return cache;
    }

    private static FamilyDefinitionAuditSummary AnalyzeFamilyDefinition(
        Document projectDoc,
        Family family,
        double toleranceDegrees,
        FamilyAxisAlignmentRequest request)
    {
        var summary = new FamilyDefinitionAuditSummary
        {
            FamilyId = checked((int)family.Id.Value),
            FamilyName = family.Name ?? string.Empty
        };

        if (family.IsInPlace)
        {
            summary.AnalysisSkipped = true;
            summary.SummaryMessage = "Family la in-place; bo qua nested audit trong family editor.";
            return summary;
        }

        if (!family.IsEditable)
        {
            summary.AnalysisSkipped = true;
            summary.SummaryMessage = "Family khong editable; bo qua nested audit.";
            return summary;
        }

        Document? familyDoc = null;
        try
        {
            familyDoc = projectDoc.EditFamily(family);
            summary.VoidFormCount = CountVoidForms(familyDoc);

            var nestedInstances = new FilteredElementCollector(familyDoc)
                .OfClass(typeof(FamilyInstance))
                .WhereElementIsNotElementType()
                .Cast<FamilyInstance>()
                .ToList();

            summary.NestedInstanceCount = nestedInstances.Count;
            var maxNestedInstances = Math.Max(1, request.MaxNestedInstancesPerFamily);
            if (nestedInstances.Count > maxNestedInstances)
            {
                summary.Truncated = true;
            }

            foreach (var nested in nestedInstances.Take(maxNestedInstances))
            {
                var finding = AnalyzeNestedInstance(nested, toleranceDegrees, request, summary.VoidFormCount > 0);
                if (finding == null)
                {
                    continue;
                }

                if (finding.Shared)
                {
                    summary.SharedNestedCount++;
                }
                else
                {
                    summary.NonSharedNestedCount++;
                }

                if (!string.Equals(finding.Status, "ALIGNED", StringComparison.OrdinalIgnoreCase))
                {
                    summary.NestedTransformRiskCount++;
                }

                if (summary.Findings.Count < Math.Max(1, request.MaxNestedFindingsPerFamily))
                {
                    summary.Findings.Add(finding);
                }
            }

            if (nestedInstances.Count == 0)
            {
                summary.SummaryMessage = "Khong co nested family instance.";
            }
            else
            {
                summary.SummaryMessage = BuildNestedSummaryMessage(summary);
            }
        }
        catch (Exception ex)
        {
            summary.AnalysisFailed = true;
            summary.SummaryMessage = "Khong mo/phan tich duoc family definition: " + ex.Message;
        }
        finally
        {
            try
            {
                familyDoc?.Close(false);
            }
            catch
            {
                // ignore
            }
        }

        return summary;
    }

    private static FamilyNestedRiskDto? AnalyzeNestedInstance(
        FamilyInstance instance,
        double toleranceDegrees,
        FamilyAxisAlignmentRequest request,
        bool hostHasVoidForms)
    {
        var nestedFamily = instance.Symbol?.Family;
        var shared = IsFamilyShared(nestedFamily);
        var mirrored = instance.Mirrored;

        try
        {
            var transform = instance.GetTransform();
            if (transform == null)
            {
                return new FamilyNestedRiskDto
                {
                    NestedFamilyName = nestedFamily?.Name ?? string.Empty,
                    NestedTypeName = instance.Symbol?.Name ?? string.Empty,
                    Shared = shared,
                    Mirrored = mirrored,
                    Status = "NESTED_TRANSFORM_UNAVAILABLE",
                    Reason = "Nested family instance khong tra ve transform.",
                    HasIfcTransformRisk = request.TreatNonSharedNestedAsRisk && !shared
                };
            }

            var basisX = SafeNormalize(transform.BasisX);
            var basisY = SafeNormalize(transform.BasisY);
            var basisZ = SafeNormalize(transform.BasisZ);

            var angleX = ComputeAngleDegrees(basisX, XYZ.BasisX, request.TreatAntiParallelAsMismatch);
            var angleY = ComputeAngleDegrees(basisY, XYZ.BasisY, request.TreatAntiParallelAsMismatch);
            var angleZ = ComputeAngleDegrees(basisZ, XYZ.BasisZ, request.TreatAntiParallelAsMismatch);
            var rotZ = ComputeSignedPlanRotationDegrees(basisX);

            var tiltRisk = request.TreatNestedTiltedAsRisk && angleZ > toleranceDegrees;
            var rotationRisk = request.TreatNestedRotatedAsRisk && (angleX > toleranceDegrees || angleY > toleranceDegrees);
            var mirroredRisk = request.TreatNestedMirroredAsRisk && mirrored;
            var nonSharedRisk = request.TreatNonSharedNestedAsRisk && !shared;

            var hasIfcRisk = nonSharedRisk && (tiltRisk || rotationRisk || mirroredRisk || hostHasVoidForms);
            var hasAnyRisk = hasIfcRisk || tiltRisk || rotationRisk || mirroredRisk || nonSharedRisk;
            if (!hasAnyRisk)
            {
                return null;
            }

            var finding = new FamilyNestedRiskDto
            {
                NestedFamilyName = nestedFamily?.Name ?? string.Empty,
                NestedTypeName = instance.Symbol?.Name ?? string.Empty,
                Shared = shared,
                Mirrored = mirrored,
                AngleXDegrees = angleX,
                AngleYDegrees = angleY,
                AngleZDegrees = angleZ,
                RotationInHostDegrees = rotZ,
                HasIfcTransformRisk = hasIfcRisk
            };

            if (hasIfcRisk)
            {
                finding.Status = "NESTED_IFC_TRANSFORM_CHAIN_RISK";
                finding.Reason = $"Nested family {(shared ? "shared" : "non-shared")} co transform lech trong host/hoac host co void forms. AngleX={angleX:0.##}°, AngleY={angleY:0.##}°, AngleZ={angleZ:0.##}°, RotZ={rotZ:0.##}°, VoidForms={(hostHasVoidForms ? "Yes" : "No")}.";
            }
            else if (tiltRisk)
            {
                finding.Status = "NESTED_TILTED_IN_HOST";
                finding.Reason = $"Nested family lech truc Z cua host {angleZ:0.##}°.";
            }
            else if (rotationRisk)
            {
                finding.Status = "NESTED_ROTATED_IN_HOST";
                finding.Reason = $"Nested family xoay trong host. AngleX={angleX:0.##}°, AngleY={angleY:0.##}°, RotZ={rotZ:0.##}°.";
            }
            else if (mirroredRisk)
            {
                finding.Status = "NESTED_MIRRORED_IN_HOST";
                finding.Reason = "Nested family dang mirrored trong host family.";
            }
            else
            {
                finding.Status = "NESTED_NON_SHARED";
                finding.Reason = "Nested family khong Shared; IFC exporter co the gom geometry vao host family.";
            }

            return finding;
        }
        catch (Exception ex)
        {
            return new FamilyNestedRiskDto
            {
                NestedFamilyName = nestedFamily?.Name ?? string.Empty,
                NestedTypeName = instance.Symbol?.Name ?? string.Empty,
                Shared = shared,
                Mirrored = mirrored,
                Status = "NESTED_ANALYSIS_FAILED",
                Reason = "Khong phan tich duoc nested family transform: " + ex.Message,
                HasIfcTransformRisk = request.TreatNonSharedNestedAsRisk && !shared
            };
        }
    }

    private static void ApplyNestedAudit(FamilyAxisAlignmentItemDto item, FamilyDefinitionAuditSummary summary, FamilyAxisAlignmentRequest request)
    {
        item.HasNonSharedNestedFamilies = summary.NonSharedNestedCount > 0;
        item.HasSharedNestedFamilies = summary.SharedNestedCount > 0;
        item.HostFamilyContainsVoidForms = summary.VoidFormCount > 0;
        item.HasNestedTransformRisk = summary.NestedTransformRiskCount > 0;
        item.NestedRiskCount = summary.Findings.Count;
        item.NestedRiskSummary = summary.SummaryMessage;
        if (request.IncludeNestedFindings && summary.Findings.Count > 0)
        {
            item.NestedFindings = summary.Findings;
        }

        item.HasNestedIfcRisk = item.HasNonSharedNestedFamilies &&
                                (item.HasNestedTransformRisk || item.HostFamilyContainsVoidForms || !item.MatchesProjectAxes);

        item.NeedsReview = !item.MatchesProjectAxes || item.HasNestedTransformRisk || item.HasNestedIfcRisk;

        if (item.MatchesProjectAxes && item.HasNestedIfcRisk)
        {
            item.Status = "NESTED_IFC_RISK";
            item.Reason = summary.SummaryMessage;
        }
        else if (item.MatchesProjectAxes && item.HasNestedTransformRisk)
        {
            item.Status = "NESTED_FAMILY_TRANSFORM_RISK";
            item.Reason = summary.SummaryMessage;
        }
        else if (!item.MatchesProjectAxes && !string.IsNullOrWhiteSpace(summary.SummaryMessage))
        {
            item.Reason = item.Reason + " Nested: " + summary.SummaryMessage;
        }
    }

    private static FamilyAxisAlignmentItemDto AnalyzeProjectAxes(FamilyInstance instance, double toleranceDegrees, bool treatAntiParallelAsMismatch, bool treatMirroredAsMismatch)
    {
        var familyName = instance.Symbol?.FamilyName ?? string.Empty;
        var typeName = instance.Symbol?.Name ?? string.Empty;

        var item = new FamilyAxisAlignmentItemDto
        {
            ElementId = checked((int)instance.Id.Value),
            UniqueId = instance.UniqueId ?? string.Empty,
            CategoryName = instance.Category?.Name ?? string.Empty,
            FamilyName = familyName,
            TypeName = typeName,
            InstanceName = instance.Name ?? string.Empty,
            Mirrored = instance.Mirrored
        };

        try
        {
            var transform = instance.GetTransform();
            if (transform == null)
            {
                item.Status = "TRANSFORM_UNAVAILABLE";
                item.Reason = "Khong lay duoc transform cua family instance.";
                item.MatchesProjectAxes = false;
                return item;
            }

            var origin = transform.Origin;
            var basisX = SafeNormalize(transform.BasisX);
            var basisY = SafeNormalize(transform.BasisY);
            var basisZ = SafeNormalize(transform.BasisZ);

            item.Origin = ToVector(origin);
            item.BasisX = ToVector(basisX);
            item.BasisY = ToVector(basisY);
            item.BasisZ = ToVector(basisZ);

            item.AngleXDegrees = ComputeAngleDegrees(basisX, XYZ.BasisX, treatAntiParallelAsMismatch);
            item.AngleYDegrees = ComputeAngleDegrees(basisY, XYZ.BasisY, treatAntiParallelAsMismatch);
            item.AngleZDegrees = ComputeAngleDegrees(basisZ, XYZ.BasisZ, treatAntiParallelAsMismatch);
            item.RotationAroundProjectZDegrees = ComputeSignedPlanRotationDegrees(basisX);

            var zMismatch = item.AngleZDegrees > toleranceDegrees;
            var xMismatch = item.AngleXDegrees > toleranceDegrees;
            var yMismatch = item.AngleYDegrees > toleranceDegrees;
            var mirroredMismatch = treatMirroredAsMismatch && item.Mirrored;

            if (zMismatch)
            {
                item.Status = "TILTED_OUT_OF_PROJECT_Z";
                item.Reason = $"BasisZ lech {item.AngleZDegrees:0.##}° so voi truc Z cua project.";
                item.MatchesProjectAxes = false;
            }
            else if (mirroredMismatch)
            {
                item.Status = "MIRRORED";
                item.Reason = "Family instance dang mirrored; local axes co the dao chieu so voi project.";
                item.MatchesProjectAxes = false;
            }
            else if (xMismatch || yMismatch)
            {
                item.Status = "ROTATED_IN_VIEW";
                item.Reason = $"BasisX/BasisY khong song song voi truc project. AngleX={item.AngleXDegrees:0.##}°, AngleY={item.AngleYDegrees:0.##}°, RotZ={item.RotationAroundProjectZDegrees:0.##}°.";
                item.MatchesProjectAxes = false;
            }
            else
            {
                item.Status = "ALIGNED";
                item.Reason = "Family instance dang song song voi he truc project trong tolerance.";
                item.MatchesProjectAxes = true;
            }
        }
        catch (Exception ex)
        {
            item.Status = "TRANSFORM_FAILED";
            item.Reason = "Khong phan tich duoc transform: " + ex.Message;
            item.MatchesProjectAxes = false;
        }

        return item;
    }

    private static int CountVoidForms(Document familyDoc)
    {
        try
        {
            return new FilteredElementCollector(familyDoc)
                .OfClass(typeof(GenericForm))
                .Cast<GenericForm>()
                .Count(x => !x.IsSolid);
        }
        catch
        {
            return 0;
        }
    }

    private static bool IsFamilyShared(Family? family)
    {
        if (family == null)
        {
            return false;
        }

        try
        {
            return family.get_Parameter(BuiltInParameter.FAMILY_SHARED)?.AsInteger() == 1;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildNestedSummaryMessage(FamilyDefinitionAuditSummary summary)
    {
        var parts = new List<string>
        {
            $"NestedInstances={summary.NestedInstanceCount}",
            $"NonShared={summary.NonSharedNestedCount}",
            $"Shared={summary.SharedNestedCount}",
            $"NestedTransformRisks={summary.NestedTransformRiskCount}",
            $"VoidForms={summary.VoidFormCount}"
        };

        if (summary.Truncated)
        {
            parts.Add("Truncated=Yes");
        }

        return string.Join("; ", parts);
    }

    private static AxisVectorDto ToVector(XYZ? value)
    {
        if (value == null)
        {
            return new AxisVectorDto();
        }

        return new AxisVectorDto
        {
            X = value.X,
            Y = value.Y,
            Z = value.Z
        };
    }

    private static XYZ SafeNormalize(XYZ? value)
    {
        if (value == null)
        {
            return XYZ.Zero;
        }

        var length = value.GetLength();
        if (length <= 1e-9)
        {
            return XYZ.Zero;
        }

        return new XYZ(value.X / length, value.Y / length, value.Z / length);
    }

    private static double ComputeAngleDegrees(XYZ vector, XYZ target, bool treatAntiParallelAsMismatch)
    {
        if (vector.IsZeroLength() || target.IsZeroLength())
        {
            return 180.0;
        }

        var dot = vector.DotProduct(target);
        dot = Math.Max(-1.0, Math.Min(1.0, dot));
        if (!treatAntiParallelAsMismatch)
        {
            dot = Math.Abs(dot);
        }

        return Math.Acos(dot) * 180.0 / Math.PI;
    }

    private static double ComputeSignedPlanRotationDegrees(XYZ basisX)
    {
        var planar = new XYZ(basisX.X, basisX.Y, 0.0);
        if (planar.IsZeroLength())
        {
            return 0.0;
        }

        planar = SafeNormalize(planar);
        var angle = Math.Atan2(planar.Y, planar.X) * 180.0 / Math.PI;
        while (angle > 180.0)
        {
            angle -= 360.0;
        }
        while (angle <= -180.0)
        {
            angle += 360.0;
        }

        return angle;
    }

    private static string GetIssueCode(FamilyAxisAlignmentItemDto item)
    {
        if (item.HasNestedIfcRisk)
        {
            return "FAMILY_AXIS_NESTED_IFC_RISK";
        }

        if (item.HasNestedTransformRisk)
        {
            return "FAMILY_AXIS_NESTED_TRANSFORM_RISK";
        }

        return item.ProjectAxisStatus switch
        {
            "ROTATED_IN_VIEW" => "FAMILY_AXIS_ROTATED",
            "TILTED_OUT_OF_PROJECT_Z" => "FAMILY_AXIS_TILTED",
            "MIRRORED" => "FAMILY_AXIS_MIRRORED",
            "TRANSFORM_UNAVAILABLE" => "FAMILY_AXIS_NO_TRANSFORM",
            "TRANSFORM_FAILED" => "FAMILY_AXIS_ANALYSIS_FAILED",
            _ => "FAMILY_AXIS_MISMATCH"
        };
    }

    private static DiagnosticSeverity GetIssueSeverity(FamilyAxisAlignmentItemDto item)
    {
        if (item.HasNestedIfcRisk || string.Equals(item.ProjectAxisStatus, "TILTED_OUT_OF_PROJECT_Z", StringComparison.OrdinalIgnoreCase))
        {
            return DiagnosticSeverity.Warning;
        }

        return DiagnosticSeverity.Info;
    }

    private sealed class FamilyDefinitionAuditSummary
    {
        internal int FamilyId { get; set; }
        internal string FamilyName { get; set; } = string.Empty;
        internal int NestedInstanceCount { get; set; }
        internal int NestedTransformRiskCount { get; set; }
        internal int NonSharedNestedCount { get; set; }
        internal int SharedNestedCount { get; set; }
        internal int VoidFormCount { get; set; }
        internal bool Truncated { get; set; }
        internal bool AnalysisSkipped { get; set; }
        internal bool AnalysisFailed { get; set; }
        internal string SummaryMessage { get; set; } = string.Empty;
        internal List<FamilyNestedRiskDto> Findings { get; } = new List<FamilyNestedRiskDto>();
    }
}
