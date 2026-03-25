using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed partial class PenetrationShadowService
{
    internal ExecutionResult PreviewCreateRoundPenetrationReviewPacket(
        UIApplication uiapp,
        PlatformServices services,
        Document doc,
        RoundPenetrationReviewPacketRequest request,
        ToolRequestEnvelope envelope)
    {
        request ??= new RoundPenetrationReviewPacketRequest();
        var plan = ResolveRoundPenetrationReviewPacketPlan(doc, request);
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        var diagnostics = new List<DiagnosticRecord>
        {
            DiagnosticRecord.Create("ROUND_PEN_REVIEW_PACKET_COUNT", DiagnosticSeverity.Info, $"Review packet items = {plan.Items.Count}."),
            DiagnosticRecord.Create("ROUND_PEN_REVIEW_SHEET", DiagnosticSeverity.Info, $"Sheet = {request.SheetNumber} | {request.SheetName}."),
            DiagnosticRecord.Create("ROUND_PEN_REVIEW_EXPORT", DiagnosticSeverity.Info, "ExportSheetImage = " + request.ExportSheetImage.ToString(CultureInfo.InvariantCulture))
        };

        diagnostics.AddRange(plan.Items.Select(x => DiagnosticRecord.Create(
            "ROUND_PEN_REVIEW_ITEM",
            string.Equals(x.QcItem.Status, "CUT_OK", StringComparison.OrdinalIgnoreCase) ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
            $"Source {x.PlanItem.SourceInstance.Id.Value} | Host {x.PlanItem.HostElement.Id.Value} | Opening {x.PlanItem.ExistingInfo?.Instance.Id.Value ?? 0} | Status {x.QcItem.Status}.",
            checked((int)(x.PlanItem.ExistingInfo?.Instance.Id.Value ?? x.PlanItem.SourceInstance.Id.Value)))));

        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = plan.Items
                .Where(x => x.PlanItem.ExistingInfo != null)
                .Select(x => checked((int)x.PlanItem.ExistingInfo!.Instance.Id.Value))
                .Distinct()
                .ToList(),
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                "packetCount=" + plan.Items.Count.ToString(CultureInfo.InvariantCulture),
                "sheetNumber=" + request.SheetNumber,
                "sheetName=" + request.SheetName
            },
            ReviewSummary = new ReviewReport
            {
                Name = "round_penetration_review_packet_preview",
                DocumentKey = services.GetDocumentKey(doc),
                ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
                IssueCount = diagnostics.Count(x => x.Severity != DiagnosticSeverity.Info),
                Issues = diagnostics
                    .Where(x => x.Severity != DiagnosticSeverity.Info)
                    .Select(x => new ReviewIssue
                    {
                        Code = x.Code,
                        Severity = x.Severity,
                        Message = x.Message,
                        ElementId = x.SourceId
                    })
                    .ToList()
            }
        };
    }

    internal ExecutionResult ExecuteCreateRoundPenetrationReviewPacket(
        UIApplication uiapp,
        PlatformServices services,
        Document doc,
        RoundPenetrationReviewPacketRequest request)
    {
        request ??= new RoundPenetrationReviewPacketRequest();
        var diagnostics = new List<DiagnosticRecord>();
        var artifacts = new List<string>();
        var createdIds = new List<int>();
        var modifiedIds = new List<int>();
        var plan = ResolveRoundPenetrationReviewPacketPlan(doc, request);
        if (plan.Items.Count == 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_PACKET_EMPTY", DiagnosticSeverity.Warning, "Khong co item nao du dieu kien de tao review packet."));
            return new ExecutionResult
            {
                OperationName = ToolNames.ReviewRoundPenetrationPacketSafe,
                DryRun = false,
                Diagnostics = diagnostics,
                ReviewSummary = new ReviewReport
                {
                    Name = "round_penetration_review_packet_review",
                    DocumentKey = services.GetDocumentKey(doc),
                    ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
                    IssueCount = 1,
                    Issues = new List<ReviewIssue>
                    {
                        new ReviewIssue
                        {
                            Code = "ROUND_PEN_REVIEW_PACKET_EMPTY",
                            Severity = DiagnosticSeverity.Warning,
                            Message = "Khong co item nao du dieu kien de tao review packet."
                        }
                    }
                }
            };
        }

        ViewSheet? sheet = null;
        var beforeWarnings = doc.GetWarnings().Count;
        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::review.round_penetration_packet_safe");
        group.Start();
        using (var transaction = new Transaction(doc, "Create round penetration review packet"))
        {
            transaction.Start();

            sheet = EnsureRoundPenetrationReviewSheet(doc, request, diagnostics, createdIds, modifiedIds);
            var reviewViews = new List<View3D>();
            foreach (var item in plan.Items)
            {
                if (item.PlanItem.ExistingInfo?.Instance != null)
                {
                    TrySetRoundPenetrationYesNoParameter(item.PlanItem.ExistingInfo.Instance, "BIM765T_ShowReviewBody", true);
                    modifiedIds.Add(checked((int)item.PlanItem.ExistingInfo.Instance.Id.Value));
                }

                var view = EnsureRoundPenetrationReviewView(uiapp, doc, item, request, diagnostics, createdIds, modifiedIds);
                ConfigureRoundPenetrationReviewView(doc, view, item, request.SectionBoxPaddingFeet, diagnostics);
                reviewViews.Add(view);
                artifacts.Add($"reviewView={view.Name}|viewId={view.Id.Value}|openingId={item.PlanItem.ExistingInfo?.Instance?.Id.Value ?? 0}");
            }

            if (reviewViews.Count == 0)
            {
                transaction.RollBack();
                group.RollBack();
                diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_VIEW_EMPTY", DiagnosticSeverity.Warning, "Khong tao duoc review view nao."));
                return new ExecutionResult
                {
                    OperationName = ToolNames.ReviewRoundPenetrationPacketSafe,
                    DryRun = false,
                    Diagnostics = diagnostics,
                    ReviewSummary = new ReviewReport
                    {
                        Name = "round_penetration_review_packet_review",
                        DocumentKey = services.GetDocumentKey(doc),
                        ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
                        IssueCount = diagnostics.Count(x => x.Severity != DiagnosticSeverity.Info),
                        Issues = diagnostics.Where(x => x.Severity != DiagnosticSeverity.Info).Select(x => new ReviewIssue
                        {
                            Code = x.Code,
                            Severity = x.Severity,
                            Message = x.Message,
                            ElementId = x.SourceId
                        }).ToList()
                    }
                };
            }

            PlaceRoundPenetrationReviewViewsOnSheet(doc, sheet, reviewViews, diagnostics, createdIds, modifiedIds);
            transaction.Commit();
        }

        group.Assimilate();

        if (sheet != null && request.ExportSheetImage)
        {
            var snapshot = services.CaptureSnapshot(uiapp, doc, new CaptureSnapshotRequest
            {
                DocumentKey = request.DocumentKey,
                Scope = "sheet",
                SheetId = checked((int)sheet.Id.Value),
                ExportImage = true,
                ImageOutputPath = request.ImageOutputPath,
                ImagePixelSize = 2048,
                MaxElements = 4000
            });

            artifacts.AddRange(snapshot.ArtifactPaths);
            foreach (var issue in snapshot.Review.Issues)
            {
                diagnostics.Add(DiagnosticRecord.Create(issue.Code, issue.Severity, issue.Message, issue.ElementId));
            }
        }

        if (sheet != null && request.ActivateSheetAfterCreate && uiapp.ActiveUIDocument?.Document?.Equals(doc) == true)
        {
            try
            {
                uiapp.ActiveUIDocument.ActiveView = sheet;
            }
            catch (Exception ex)
            {
                diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_ACTIVATE_SHEET_FAILED", DiagnosticSeverity.Warning, ex.Message, checked((int)sheet.Id.Value)));
            }
        }

        var diff = new DiffSummary
        {
            CreatedIds = createdIds.Distinct().ToList(),
            ModifiedIds = modifiedIds.Distinct().ToList(),
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };
        var review = services.BuildExecutionReview("round_penetration_review_packet_review", diff);
        foreach (var record in diagnostics.Where(x => x.Severity != DiagnosticSeverity.Info))
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = record.Code,
                Severity = record.Severity,
                Message = record.Message,
                ElementId = record.SourceId
            });
        }
        review.IssueCount = review.Issues.Count;

        return new ExecutionResult
        {
            OperationName = ToolNames.ReviewRoundPenetrationPacketSafe,
            DryRun = false,
            ChangedIds = diff.CreatedIds.Concat(diff.ModifiedIds).Distinct().ToList(),
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = artifacts,
            ReviewSummary = review
        };
    }

    private static RoundPenetrationSettings BuildRoundPenetrationSettings(RoundPenetrationReviewPacketRequest request)
    {
        return BuildRoundPenetrationSettingsCore(
            request.TargetFamilyName,
            request.SourceElementClasses,
            request.HostElementClasses,
            request.SourceFamilyNameContains,
            request.SourceElementIds,
            request.GybClearancePerSideInches,
            request.WfrClearancePerSideInches,
            request.AxisToleranceDegrees,
            request.TraceCommentPrefix,
            request.MaxResults,
            true);
    }

    private static RoundPenetrationReviewPacketPlan ResolveRoundPenetrationReviewPacketPlan(Document doc, RoundPenetrationReviewPacketRequest request)
    {
        var plan = ResolveRoundPenetrationPlan(doc, BuildRoundPenetrationSettings(request));
        var requestedOpeningIds = request.PenetrationElementIds != null && request.PenetrationElementIds.Count > 0
            ? new HashSet<int>(request.PenetrationElementIds)
            : null;
        var requestedSourceIds = request.SourceElementIds != null && request.SourceElementIds.Count > 0
            ? new HashSet<int>(request.SourceElementIds)
            : null;

        var reviewItems = new List<RoundPenetrationReviewPacketItem>();
        foreach (var item in plan.Items)
        {
            var qcItem = BuildRoundPenetrationQcItem(item, request.AxisToleranceDegrees);
            var openingId = item.ExistingInfo?.Instance != null
                ? checked((int)item.ExistingInfo.Instance.Id.Value)
                : (int?)null;
            if (requestedOpeningIds != null && (!openingId.HasValue || !requestedOpeningIds.Contains(openingId.Value)))
            {
                continue;
            }

            if (requestedSourceIds != null && !requestedSourceIds.Contains(checked((int)item.SourceInstance.Id.Value)))
            {
                continue;
            }

            if (requestedOpeningIds == null && requestedSourceIds == null && request.IncludeOnlyNonOkQcItems && string.Equals(qcItem.Status, "CUT_OK", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            reviewItems.Add(new RoundPenetrationReviewPacketItem(item, qcItem));
        }

        if (requestedOpeningIds == null && requestedSourceIds == null && reviewItems.Count == 0)
        {
            foreach (var item in plan.Items)
            {
                reviewItems.Add(new RoundPenetrationReviewPacketItem(item, BuildRoundPenetrationQcItem(item, request.AxisToleranceDegrees)));
            }
        }

        return new RoundPenetrationReviewPacketPlan(reviewItems
            .OrderBy(x => string.Equals(x.QcItem.Status, "CUT_OK", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(x => x.PlanItem.SourceInstance.Id.Value)
            .Take(Math.Max(1, request.MaxItems))
            .ToList());
    }

    private static ViewSheet EnsureRoundPenetrationReviewSheet(Document doc, RoundPenetrationReviewPacketRequest request, ICollection<DiagnosticRecord> diagnostics, ICollection<int> createdIds, ICollection<int> modifiedIds)
    {
        var existingSheet = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSheet))
            .Cast<ViewSheet>()
            .FirstOrDefault(x => !x.IsTemplate && string.Equals(x.SheetNumber, request.SheetNumber, StringComparison.OrdinalIgnoreCase));
        if (existingSheet != null && request.ReuseExistingSheet)
        {
            if (!string.Equals(existingSheet.Name, request.SheetName, StringComparison.Ordinal))
            {
                existingSheet.Name = request.SheetName;
                modifiedIds.Add(checked((int)existingSheet.Id.Value));
            }

            return existingSheet;
        }

        var titleBlockTypeId = ResolveRoundPenetrationReviewTitleBlockTypeId(doc, request.TitleBlockTypeName);
        var sheet = ViewSheet.Create(doc, titleBlockTypeId);
        sheet.SheetNumber = request.SheetNumber;
        sheet.Name = request.SheetName;
        createdIds.Add(checked((int)sheet.Id.Value));
        diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_SHEET_CREATED", DiagnosticSeverity.Info, $"Created review sheet {sheet.SheetNumber} - {sheet.Name}.", checked((int)sheet.Id.Value)));
        return sheet;
    }

    private static ElementId ResolveRoundPenetrationReviewTitleBlockTypeId(Document doc, string titleBlockTypeName)
    {
        var titleBlocks = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsElementType()
            .Cast<ElementType>()
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (titleBlocks.Count == 0)
        {
            return ElementId.InvalidElementId;
        }

        if (!string.IsNullOrWhiteSpace(titleBlockTypeName))
        {
            var match = titleBlocks.FirstOrDefault(x => string.Equals(x.Name, titleBlockTypeName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                return match.Id;
            }
        }

        return titleBlocks[0].Id;
    }

    private static View3D EnsureRoundPenetrationReviewView(
        UIApplication uiapp,
        Document doc,
        RoundPenetrationReviewPacketItem item,
        RoundPenetrationReviewPacketRequest request,
        ICollection<DiagnosticRecord> diagnostics,
        ICollection<int> createdIds,
        ICollection<int> modifiedIds)
    {
        var viewName = BuildRoundPenetrationReviewViewName(item, request.ViewNamePrefix);
        var existingView = new FilteredElementCollector(doc)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(x => !x.IsTemplate && string.Equals(x.Name, viewName, StringComparison.OrdinalIgnoreCase));
        if (existingView != null && request.ReuseExistingViews)
        {
            modifiedIds.Add(checked((int)existingView.Id.Value));
            return existingView;
        }

        var viewFamilyType = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .First(x => x.ViewFamily == ViewFamily.ThreeDimensional);
        var view = View3D.CreateIsometric(doc, viewFamilyType.Id);
        view.Name = existingView == null ? viewName : BuildUniqueRoundPenetrationReviewViewName(doc, viewName);
        createdIds.Add(checked((int)view.Id.Value));
        diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_VIEW_CREATED", DiagnosticSeverity.Info, $"Created review view {view.Name}.", checked((int)view.Id.Value)));

        if (request.CopyActive3DOrientation && uiapp.ActiveUIDocument?.Document?.Equals(doc) == true && uiapp.ActiveUIDocument.ActiveView is View3D active3D && !active3D.IsTemplate)
        {
            try
            {
                view.SetOrientation(active3D.GetOrientation());
            }
            catch (Exception ex)
            {
                diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_COPY_ORIENTATION_FAILED", DiagnosticSeverity.Warning, ex.Message, checked((int)view.Id.Value)));
            }
        }

        return view;
    }

    private static string BuildRoundPenetrationReviewViewName(RoundPenetrationReviewPacketItem item, string prefix)
    {
        var safePrefix = string.IsNullOrWhiteSpace(prefix) ? "BIM765T_RoundPen_Review" : prefix.Trim();
        return item.PlanItem.ExistingInfo?.Instance != null
            ? $"{safePrefix}_S{item.PlanItem.SourceInstance.Id.Value}_H{item.PlanItem.HostElement.Id.Value}_P{item.PlanItem.ExistingInfo.Instance.Id.Value}"
            : $"{safePrefix}_S{item.PlanItem.SourceInstance.Id.Value}_H{item.PlanItem.HostElement.Id.Value}_MISSING";
    }

    private static string BuildUniqueRoundPenetrationReviewViewName(Document doc, string baseName)
    {
        var existingNames = new HashSet<string>(
            new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(x => !x.IsTemplate)
                .Select(x => x.Name),
            StringComparer.OrdinalIgnoreCase);

        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        for (var index = 2; index < 1000; index++)
        {
            var candidate = $"{baseName}_{index:000}";
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }

        return baseName + "_" + Guid.NewGuid().ToString("N");
    }

    private static void ConfigureRoundPenetrationReviewView(Document doc, View3D view, RoundPenetrationReviewPacketItem item, double paddingFeet, ICollection<DiagnosticRecord> diagnostics)
    {
        view.DetailLevel = ViewDetailLevel.Fine;
        try
        {
            view.DisplayStyle = DisplayStyle.ShadingWithEdges;
        }
        catch
        {
            // keep current style if project/template locks it
        }

        var box = BuildRoundPenetrationReviewSectionBox(new[]
        {
            item.PlanItem.SourceInstance as Element,
            item.PlanItem.HostElement,
            item.PlanItem.ExistingInfo?.Instance as Element
        }, paddingFeet);
        if (box != null)
        {
            view.IsSectionBoxActive = true;
            view.SetSectionBox(box);
        }

        var solidFillId = ResolveSolidFillPatternId(doc);
        view.SetElementOverrides(item.PlanItem.SourceInstance.Id, BuildRoundPenetrationOverride(new Color(220, 30, 30), 15, solidFillId));
        if (item.PlanItem.ExistingInfo?.Instance != null)
        {
            view.SetElementOverrides(item.PlanItem.ExistingInfo.Instance.Id, BuildRoundPenetrationOverride(new Color(255, 140, 0), 0, solidFillId));
        }

        var hostColor = string.Equals(item.PlanItem.HostClass, "GYB", StringComparison.OrdinalIgnoreCase)
            ? new Color(64, 128, 255)
            : new Color(255, 210, 0);
        view.SetElementOverrides(item.PlanItem.HostElement.Id, BuildRoundPenetrationOverride(hostColor, 75, solidFillId));

        diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_VIEW_CONFIGURED", DiagnosticSeverity.Info, $"Configured review graphics for {view.Name}.", checked((int)view.Id.Value)));
    }

    private static BoundingBoxXYZ? BuildRoundPenetrationReviewSectionBox(IEnumerable<Element?> elements, double paddingFeet)
    {
        var boxes = elements
            .Where(x => x != null)
            .Select(x => x!.get_BoundingBox(null))
            .Where(x => x != null)
            .ToList();
        if (boxes.Count == 0)
        {
            return null;
        }

        var minX = boxes.Min(x => x!.Min.X) - paddingFeet;
        var minY = boxes.Min(x => x!.Min.Y) - paddingFeet;
        var minZ = boxes.Min(x => x!.Min.Z) - paddingFeet;
        var maxX = boxes.Max(x => x!.Max.X) + paddingFeet;
        var maxY = boxes.Max(x => x!.Max.Y) + paddingFeet;
        var maxZ = boxes.Max(x => x!.Max.Z) + paddingFeet;
        return new BoundingBoxXYZ
        {
            Transform = Transform.Identity,
            Min = new XYZ(minX, minY, minZ),
            Max = new XYZ(maxX, maxY, maxZ)
        };
    }

    private static OverrideGraphicSettings BuildRoundPenetrationOverride(Color color, int transparency, ElementId solidFillId)
    {
        var overrides = new OverrideGraphicSettings();
        overrides.SetProjectionLineColor(color);
        overrides.SetCutLineColor(color);
        overrides.SetSurfaceTransparency(Math.Max(0, Math.Min(100, transparency)));
        if (solidFillId != ElementId.InvalidElementId)
        {
            overrides.SetSurfaceForegroundPatternId(solidFillId);
            overrides.SetSurfaceForegroundPatternColor(color);
            overrides.SetCutForegroundPatternId(solidFillId);
            overrides.SetCutForegroundPatternColor(color);
        }

        return overrides;
    }

    private static ElementId ResolveSolidFillPatternId(Document doc)
    {
        var solidFill = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(x => x.GetFillPattern().IsSolidFill);
        return solidFill?.Id ?? ElementId.InvalidElementId;
    }

    private static void PlaceRoundPenetrationReviewViewsOnSheet(Document doc, ViewSheet sheet, IReadOnlyList<View3D> views, ICollection<DiagnosticRecord> diagnostics, ICollection<int> createdIds, ICollection<int> modifiedIds)
    {
        var existingViewports = new FilteredElementCollector(doc)
            .OfClass(typeof(Viewport))
            .Cast<Viewport>()
            .Where(x => x.SheetId == sheet.Id)
            .ToDictionary(x => x.ViewId, x => x);
        var centers = BuildRoundPenetrationSheetCenters(views.Count);

        for (var index = 0; index < views.Count; index++)
        {
            var view = views[index];
            var center = centers[index];
            if (existingViewports.TryGetValue(view.Id, out var viewport))
            {
                viewport.SetBoxCenter(center);
                modifiedIds.Add(checked((int)viewport.Id.Value));
                continue;
            }

            if (!Viewport.CanAddViewToSheet(doc, sheet.Id, view.Id))
            {
                diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_REVIEW_VIEWPORT_SKIPPED", DiagnosticSeverity.Warning, $"Khong the dat view {view.Name} len sheet {sheet.SheetNumber}.", checked((int)view.Id.Value)));
                continue;
            }

            var created = Viewport.Create(doc, sheet.Id, view.Id, center);
            createdIds.Add(checked((int)created.Id.Value));
        }
    }

    private static IReadOnlyList<XYZ> BuildRoundPenetrationSheetCenters(int count)
    {
        var centers = new List<XYZ>(count);
        var columns = count <= 1 ? 1 : 2;
        const double xStart = 1.1;
        const double xSpacing = 1.6;
        const double yStart = 1.0;
        const double ySpacing = 1.15;

        for (var index = 0; index < count; index++)
        {
            var column = index % columns;
            var row = index / columns;
            centers.Add(new XYZ(xStart + (column * xSpacing), yStart - (row * ySpacing), 0.0));
        }

        return centers;
    }

    private sealed class RoundPenetrationReviewPacketPlan
    {
        internal RoundPenetrationReviewPacketPlan(IReadOnlyList<RoundPenetrationReviewPacketItem> items)
        {
            Items = items;
        }

        internal IReadOnlyList<RoundPenetrationReviewPacketItem> Items { get; }
    }

    private sealed class RoundPenetrationReviewPacketItem
    {
        internal RoundPenetrationReviewPacketItem(RoundPenetrationPlanItem planItem, RoundPenetrationCutQcItemDto qcItem)
        {
            PlanItem = planItem;
            QcItem = qcItem;
        }

        internal RoundPenetrationPlanItem PlanItem { get; }
        internal RoundPenetrationCutQcItemDto QcItem { get; }
    }
}
