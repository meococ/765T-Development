using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using BIM765T.Revit.Agent.Infrastructure.Failures;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed partial class PenetrationShadowService
{
    private const int RoundPenetrationFamilySchemaVersion = 3;

    internal RoundPenetrationCutPlanResponse PlanRoundPenetrationCut(PlatformServices services, Document doc, RoundPenetrationCutPlanRequest? request)
    {
        request ??= new RoundPenetrationCutPlanRequest();
        var plan = ResolveRoundPenetrationPlan(doc, BuildRoundPenetrationSettings(request));
        return BuildRoundPenetrationPlanResponse(services, doc, plan, request.TargetFamilyName);
    }

    internal ExecutionResult PreviewCreateRoundPenetrationCutBatch(PlatformServices services, Document doc, CreateRoundPenetrationCutBatchRequest request, ToolRequestEnvelope envelope)
    {
        var settings = BuildRoundPenetrationSettings(request);
        var plan = ResolveRoundPenetrationPlan(doc, settings);
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        var diagnostics = new List<DiagnosticRecord>
        {
            DiagnosticRecord.Create("ROUND_PEN_PLAN_COUNT", DiagnosticSeverity.Info, $"Planned pairs = {plan.Items.Count}."),
            DiagnosticRecord.Create("ROUND_PEN_CREATABLE_COUNT", DiagnosticSeverity.Info, $"Creatable pairs = {plan.Items.Count(x => x.CanCreateNewInstance)}."),
            DiagnosticRecord.Create("ROUND_PEN_EXISTING_COUNT", DiagnosticSeverity.Info, $"Existing traced openings = {plan.Items.Count(x => x.ExistingInfo != null)}."),
            DiagnosticRecord.Create("ROUND_PEN_OUTPUT_DIRECTORY", DiagnosticSeverity.Info, "OutputDirectory = " + ResolveRoundPenetrationOutputDirectory(request))
        };

        diagnostics.AddRange(plan.Items
            .Where(x => !x.CanCreateNewInstance)
            .Take(80)
            .Select(x => DiagnosticRecord.Create("ROUND_PEN_PREVIEW_RESIDUAL", x.ExistingInfo != null ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning, x.ResidualNote, checked((int)x.SourceInstance.Id.Value))));

        var requiredSpecs = plan.Items
            .Where(x => x.CanCreateNewInstance)
            .Select(x => x.FamilySpec)
            .GroupBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = plan.Items
                .Where(x => x.ExistingInfo != null)
                .Select(x => checked((int)x.ExistingInfo!.Instance.Id.Value))
                .Distinct()
                .ToList(),
            Diagnostics = diagnostics,
            Artifacts = new List<string>(
                new[]
                {
                    "plannedCount=" + plan.Items.Count.ToString(CultureInfo.InvariantCulture),
                    "creatableCount=" + plan.Items.Count(x => x.CanCreateNewInstance).ToString(CultureInfo.InvariantCulture),
                    "existingCount=" + plan.Items.Count(x => x.ExistingInfo != null).ToString(CultureInfo.InvariantCulture),
                    "outputDirectory=" + ResolveRoundPenetrationOutputDirectory(request)
                }.Concat(requiredSpecs.Select(x =>
                    $"familySpec={x.FamilyName}|type={x.TypeName}|openingFt={x.OpeningDiameterFeet.ToString("0.######", CultureInfo.InvariantCulture)}|lengthFt={x.VoidLengthFeet.ToString("0.######", CultureInfo.InvariantCulture)}"))),
            ReviewSummary = new ReviewReport
            {
                Name = "round_penetration_cut_preview",
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

    internal ExecutionResult ExecuteCreateRoundPenetrationCutBatch(PlatformServices services, Document doc, CreateRoundPenetrationCutBatchRequest request)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var artifacts = new List<string>();
        var createdIds = new List<int>();
        var modifiedIds = new List<int>();
        var reviewIssues = new List<ReviewIssue>();
        var beforeWarnings = doc.GetWarnings().Count;
        var settings = BuildRoundPenetrationSettings(request);
        var plan = ResolveRoundPenetrationPlan(doc, settings);
        var creatable = plan.Items.Where(x => x.CanCreateNewInstance).ToList();

        foreach (var item in plan.Items.Where(x => !x.CanCreateNewInstance))
        {
            diagnostics.Add(DiagnosticRecord.Create(
                "ROUND_PEN_SKIP",
                item.ExistingInfo != null ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
                item.ResidualNote,
                checked((int)item.SourceInstance.Id.Value)));
        }

        if (creatable.Count == 0)
        {
            if (diagnostics.Count == 0)
            {
                diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_NO_WORK", DiagnosticSeverity.Warning, "No round penetration openings are eligible for creation."));
            }

            return new ExecutionResult
            {
                OperationName = ToolNames.BatchCreateRoundPenetrationCutSafe,
                DryRun = false,
                Diagnostics = diagnostics,
                ReviewSummary = new ReviewReport
                {
                    Name = "round_penetration_cut_execute_review",
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

        var familySymbolMap = EnsureRoundPenetrationFamilySymbols(doc, request, creatable.Select(x => x.FamilySpec), diagnostics, artifacts);

        var inactiveSymbols = familySymbolMap.Values.Where(x => !x.IsActive).ToList();
        if (inactiveSymbols.Count > 0)
        {
            using var activateTransaction = new Transaction(doc, "Activate round penetration symbols");
            activateTransaction.Start();
            AgentFailureHandling.Configure(activateTransaction, diagnostics);
            foreach (var symbol in inactiveSymbols)
            {
                symbol.Activate();
            }

            doc.Regenerate();
            var activationStatus = activateTransaction.Commit();
            if (activationStatus != TransactionStatus.Committed)
            {
                diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_SYMBOL_ACTIVATION_FAILED", DiagnosticSeverity.Error, $"Family symbol activation transaction finished with status {activationStatus}."));
                return new ExecutionResult
                {
                    OperationName = ToolNames.BatchCreateRoundPenetrationCutSafe,
                    DryRun = false,
                    Diagnostics = diagnostics,
                    Artifacts = artifacts,
                    ReviewSummary = new ReviewReport
                    {
                        Name = "round_penetration_cut_execute_review",
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
        }

        foreach (var item in creatable)
        {
            if (!familySymbolMap.TryGetValue(item.FamilySpec.TypeName, out var symbol))
            {
                diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_SYMBOL_MISSING", DiagnosticSeverity.Error, $"Cannot resolve loaded family symbol for {item.FamilySpec.FamilyName}/{item.FamilySpec.TypeName}.", checked((int)item.SourceInstance.Id.Value)));
                reviewIssues.Add(BuildRoundPenetrationIssue("ROUND_PEN_SYMBOL_MISSING", DiagnosticSeverity.Error, item.ResidualNote, item));
                continue;
            }

            using var itemTransaction = new Transaction(doc, $"Create round penetration {item.SourceInstance.Id.Value}->{item.HostElement.Id.Value}");
            itemTransaction.Start();
            AgentFailureHandling.Configure(itemTransaction, diagnostics);

            FamilyInstance? instance = null;
            try
            {
                instance = CreateRoundPenetrationInstance(doc, symbol, item, diagnostics);
                if (instance == null)
                {
                    itemTransaction.RollBack();
                    reviewIssues.Add(BuildRoundPenetrationIssue("ROUND_PEN_PLACE_FAILED", DiagnosticSeverity.Error, "Cannot place opening instance.", item));
                    continue;
                }

                SetRoundPenetrationMetadata(instance, item, request, diagnostics);
                doc.Regenerate();

                var alignment = AlignRoundPenetrationInstance(doc, instance, item, request.AxisToleranceDegrees, diagnostics);
                if (request.RequireAxisAlignedResult && !alignment.IsAligned)
                {
                    diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_AXIS_REJECTED", DiagnosticSeverity.Warning, alignment.Reason, checked((int)item.SourceInstance.Id.Value)));
                    reviewIssues.Add(BuildRoundPenetrationIssue("ROUND_PEN_AXIS_REJECTED", DiagnosticSeverity.Warning, alignment.Reason, item));
                    itemTransaction.RollBack();
                    continue;
                }

                if (!TryApplyRoundPenetrationCutWithRetry(doc, item.HostElement, instance, request.MaxCutRetries, request.RetryBackoffMs, diagnostics, out var cutNote))
                {
                    diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_CUT_FAILED", DiagnosticSeverity.Warning, cutNote, checked((int)item.SourceInstance.Id.Value)));
                    reviewIssues.Add(BuildRoundPenetrationIssue("ROUND_PEN_CUT_FAILED", DiagnosticSeverity.Warning, cutNote, item));
                    itemTransaction.RollBack();
                    continue;
                }

                var itemStatus = itemTransaction.Commit();
                if (itemStatus != TransactionStatus.Committed)
                {
                    diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_ITEM_NOT_COMMITTED", DiagnosticSeverity.Error, $"Item transaction finished with status {itemStatus}.", checked((int)item.SourceInstance.Id.Value)));
                    reviewIssues.Add(BuildRoundPenetrationIssue("ROUND_PEN_ITEM_NOT_COMMITTED", DiagnosticSeverity.Error, $"Item transaction finished with status {itemStatus}.", item));
                    continue;
                }

                createdIds.Add(checked((int)instance.Id.Value));
                artifacts.Add($"opening:source={item.SourceInstance.Id.Value};host={item.HostElement.Id.Value};opening={instance.Id.Value};family={item.FamilySpec.FamilyName};type={item.FamilySpec.TypeName}");
                diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_CREATED", DiagnosticSeverity.Info, $"Created opening {instance.Id.Value} for source {item.SourceInstance.Id.Value} and host {item.HostElement.Id.Value}.", checked((int)instance.Id.Value)));
            }
            catch (Exception ex)
            {
                try
                {
                    itemTransaction.RollBack();
                }
                catch
                {
                    // ignore rollback failure
                }

                diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_CREATE_FAILED", DiagnosticSeverity.Error, ex.Message, checked((int)item.SourceInstance.Id.Value)));
                reviewIssues.Add(BuildRoundPenetrationIssue("ROUND_PEN_CREATE_FAILED", DiagnosticSeverity.Error, ex.Message, item));
            }
        }

        var diff = new DiffSummary
        {
            CreatedIds = createdIds.Distinct().ToList(),
            ModifiedIds = modifiedIds.Distinct().ToList(),
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };

        return new ExecutionResult
        {
            OperationName = ToolNames.BatchCreateRoundPenetrationCutSafe,
            DryRun = false,
            ChangedIds = diff.CreatedIds.Concat(diff.ModifiedIds).Distinct().ToList(),
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = artifacts,
            ReviewSummary = new ReviewReport
            {
                Name = "round_penetration_cut_execute_review",
                DocumentKey = services.GetDocumentKey(doc),
                ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
                IssueCount = reviewIssues.Count,
                Issues = reviewIssues
            }
        };
    }

    internal RoundPenetrationCutQcResponse ReportRoundPenetrationCutQc(PlatformServices services, Document doc, RoundPenetrationCutQcRequest? request)
    {
        request ??= new RoundPenetrationCutQcRequest();
        var plan = ResolveRoundPenetrationPlan(doc, BuildRoundPenetrationSettings(request));
        var response = new RoundPenetrationCutQcResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            TargetFamilyName = request.TargetFamilyName
        };

        var consumedExistingIds = new HashSet<int>();
        foreach (var item in plan.Items)
        {
            var qc = BuildRoundPenetrationQcItem(item, request.AxisToleranceDegrees);
            if (item.ExistingInfo != null)
            {
                consumedExistingIds.Add(checked((int)item.ExistingInfo.Instance.Id.Value));
            }

            response.Items.Add(qc);
        }

        foreach (var existing in plan.ExistingInstances.Where(x => !consumedExistingIds.Contains(checked((int)x.Instance.Id.Value))))
        {
            response.Items.Add(new RoundPenetrationCutQcItemDto
            {
                SourceElementId = existing.SourceElementId ?? 0,
                HostElementId = existing.HostElementId ?? 0,
                PenetrationElementId = checked((int)existing.Instance.Id.Value),
                PenetrationFamilyName = existing.Instance.Symbol?.Family?.Name ?? string.Empty,
                PenetrationTypeName = existing.Instance.Symbol?.Name ?? string.Empty,
                HostClass = ReadParameterValue(existing.Instance, "BIM765T_HostClass"),
                CassetteId = ReadParameterValue(existing.Instance, "BIM765T_CassetteId"),
                Status = "ORPHAN_INSTANCE",
                AxisStatus = "NOT_EVALUATED",
                CutStatus = "NOT_EVALUATED",
                PlacementStatus = "NOT_EVALUATED",
                PlacementDriftFeet = 0.0,
                TraceComment = existing.TraceComment,
                ResidualNote = "Traced opening exists but no current source/host plan pair resolved for it."
            });
        }

        response.Count = response.Items.Count;
        response.PlacedCount = response.Items.Count(x => x.PenetrationElementId.HasValue);
        response.CutSuccessCount = response.Items.Count(x => string.Equals(x.Status, "CUT_OK", StringComparison.OrdinalIgnoreCase));
        response.ResidualCount = response.Items.Count(x => x.PenetrationElementId.HasValue && !string.Equals(x.Status, "CUT_OK", StringComparison.OrdinalIgnoreCase));
        response.OrphanCount = response.Items.Count(x => string.Equals(x.Status, "ORPHAN_INSTANCE", StringComparison.OrdinalIgnoreCase));
        response.Review = new ReviewReport
        {
            Name = "round_penetration_cut_qc",
            DocumentKey = services.GetDocumentKey(doc),
            ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
            IssueCount = response.Items.Count(x => !string.Equals(x.Status, "CUT_OK", StringComparison.OrdinalIgnoreCase)),
            Issues = response.Items
                .Where(x => !string.Equals(x.Status, "CUT_OK", StringComparison.OrdinalIgnoreCase))
                .Select(x => new ReviewIssue
                {
                    Code = x.Status,
                    Severity = string.Equals(x.Status, "ORPHAN_INSTANCE", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.Status, "CUT_MISSING", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.Status, "PLACEMENT_REVIEW", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(x.Status, "AXIS_REVIEW", StringComparison.OrdinalIgnoreCase)
                        ? DiagnosticSeverity.Warning
                        : DiagnosticSeverity.Info,
                    Message = x.ResidualNote,
                    ElementId = x.PenetrationElementId ?? x.SourceElementId
                })
                .ToList()
        };

        return response;
    }

    private static RoundPenetrationCutQcItemDto BuildRoundPenetrationQcItem(RoundPenetrationPlanItem item, double toleranceDegrees)
    {
        if (item.ExistingInfo == null)
        {
            return new RoundPenetrationCutQcItemDto
            {
                SourceElementId = checked((int)item.SourceInstance.Id.Value),
                HostElementId = checked((int)item.HostElement.Id.Value),
                PenetrationElementId = null,
                PenetrationFamilyName = item.FamilySpec.FamilyName,
                PenetrationTypeName = item.FamilySpec.TypeName,
                HostClass = item.HostClass,
                CassetteId = item.CassetteId,
                Status = item.CanCreateNewInstance ? "MISSING_INSTANCE" : "RESIDUAL_PLAN",
                AxisStatus = "NOT_EVALUATED",
                CutStatus = "NOT_EVALUATED",
                PlacementStatus = "NOT_EVALUATED",
                PlacementDriftFeet = 0.0,
                TraceComment = item.TraceComment,
                ResidualNote = item.ResidualNote
            };
        }

        var axis = EvaluateRoundPenetrationAxis(item.ExistingInfo.Instance, item.SourceBasisX, toleranceDegrees);
        var placement = EvaluateRoundPenetrationPlacement(item.ExistingInfo.Instance, item.PlacementPoint);
        var cutExists = InstanceVoidCutUtils.InstanceVoidCutExists(item.HostElement, item.ExistingInfo.Instance);
        var status = !cutExists
            ? "CUT_MISSING"
            : !placement.IsAligned
                ? "PLACEMENT_REVIEW"
                : axis.IsAligned
                    ? "CUT_OK"
                    : "AXIS_REVIEW";

        return new RoundPenetrationCutQcItemDto
        {
            SourceElementId = checked((int)item.SourceInstance.Id.Value),
            HostElementId = checked((int)item.HostElement.Id.Value),
            PenetrationElementId = checked((int)item.ExistingInfo.Instance.Id.Value),
            PenetrationFamilyName = item.ExistingInfo.Instance.Symbol?.Family?.Name ?? string.Empty,
            PenetrationTypeName = item.ExistingInfo.Instance.Symbol?.Name ?? string.Empty,
            HostClass = item.HostClass,
            CassetteId = item.CassetteId,
            Status = status,
            AxisStatus = axis.Status,
            CutStatus = cutExists ? "CUT_OK" : "CUT_MISSING",
            PlacementStatus = placement.Status,
            PlacementDriftFeet = placement.DriftFeet,
            TraceComment = item.ExistingInfo.TraceComment,
            ResidualNote = status == "CUT_OK"
                ? "Opening cut and local X aligns to source BasisX."
                : status == "PLACEMENT_REVIEW"
                    ? placement.Reason
                : cutExists
                    ? axis.Reason
                    : item.ResidualNote
        };
    }

    private static RoundPenetrationCutPlanResponse BuildRoundPenetrationPlanResponse(PlatformServices services, Document doc, RoundPenetrationPlan plan, string targetFamilyName)
    {
        var response = new RoundPenetrationCutPlanResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            TargetFamilyName = targetFamilyName
        };

        foreach (var item in plan.Items)
        {
            response.Items.Add(new RoundPenetrationCutPlanItemDto
            {
                SourceElementId = checked((int)item.SourceInstance.Id.Value),
                HostElementId = checked((int)item.HostElement.Id.Value),
                SourceFamilyName = item.SourceInstance.Symbol?.Family?.Name ?? string.Empty,
                SourceTypeName = item.SourceInstance.Symbol?.Name ?? string.Empty,
                HostFamilyName = (item.HostElement as FamilyInstance)?.Symbol?.Family?.Name ?? string.Empty,
                HostTypeName = (item.HostElement as FamilyInstance)?.Symbol?.Name ?? string.Empty,
                HostClass = item.HostClass,
                CassetteId = item.CassetteId,
                Origin = ToVector(item.SourceOrigin),
                BasisX = ToVector(item.SourceBasisX),
                NominalOD = item.NominalOdDisplay,
                NominalODFeet = item.NominalOdFeet,
                OpeningDiameterFeet = item.OpeningDiameterFeet,
                CutLengthFeet = item.CutLengthFeet,
                ClearancePerSideFeet = item.ClearancePerSideFeet,
                PlacementPoint = ToVector(item.PlacementPoint),
                TargetFamilyName = item.FamilySpec.FamilyName,
                TypeName = item.FamilySpec.TypeName,
                ExistingPenetrationElementId = item.ExistingInfo != null ? checked((int)item.ExistingInfo.Instance.Id.Value) : (int?)null,
                CanPlace = item.CanPlace,
                CanCut = item.CanCut,
                TraceComment = item.TraceComment,
                ResidualNote = item.ResidualNote
            });
        }

        response.Count = response.Items.Count;
        response.CreatableCount = response.Items.Count(x => x.CanPlace && x.CanCut && !x.ExistingPenetrationElementId.HasValue);
        response.ExistingCount = response.Items.Count(x => x.ExistingPenetrationElementId.HasValue);
        response.ResidualCount = response.Items.Count(x =>
            (x.ExistingPenetrationElementId.HasValue && !x.CanCut) ||
            (!x.ExistingPenetrationElementId.HasValue && (!x.CanPlace || !x.CanCut)));
        response.Review = new ReviewReport
        {
            Name = "round_penetration_cut_plan",
            DocumentKey = services.GetDocumentKey(doc),
            ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
            IssueCount = response.Items.Count(x =>
                (x.ExistingPenetrationElementId.HasValue && !x.CanCut) ||
                (!x.ExistingPenetrationElementId.HasValue && (!x.CanPlace || !x.CanCut))),
            Issues = response.Items
                .Where(x =>
                    (x.ExistingPenetrationElementId.HasValue && !x.CanCut) ||
                    (!x.ExistingPenetrationElementId.HasValue && (!x.CanPlace || !x.CanCut)))
                .Select(x => new ReviewIssue
                {
                    Code = x.ExistingPenetrationElementId.HasValue ? "ROUND_PEN_EXISTING" : "ROUND_PEN_RESIDUAL",
                    Severity = x.ExistingPenetrationElementId.HasValue ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
                    Message = x.ResidualNote,
                    ElementId = x.SourceElementId
                })
                .ToList()
        };
        return response;
    }

    private static RoundPenetrationSettings BuildRoundPenetrationSettings(RoundPenetrationCutPlanRequest request)
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
            request.IncludeExisting);
    }

    private static RoundPenetrationSettings BuildRoundPenetrationSettings(CreateRoundPenetrationCutBatchRequest request)
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

    private static RoundPenetrationSettings BuildRoundPenetrationSettings(RoundPenetrationCutQcRequest request)
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

    private static RoundPenetrationSettings BuildRoundPenetrationSettingsCore(
        string? targetFamilyName,
        IEnumerable<string>? sourceElementClasses,
        IEnumerable<string>? hostElementClasses,
        IEnumerable<string>? sourceFamilyNameContains,
        IEnumerable<int>? sourceElementIds,
        double gybClearancePerSideInches,
        double wfrClearancePerSideInches,
        double axisToleranceDegrees,
        string? traceCommentPrefix,
        int maxResults,
        bool includeExisting)
    {
        return new RoundPenetrationSettings
        {
            TargetFamilyName = NormalizeRoundPenetrationFamilyName(targetFamilyName),
            SourceElementClasses = new HashSet<string>((sourceElementClasses ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(NormalizeToken), StringComparer.OrdinalIgnoreCase),
            HostElementClasses = new HashSet<string>((hostElementClasses ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(NormalizeToken), StringComparer.OrdinalIgnoreCase),
            SourceFamilyNameContains = (sourceFamilyNameContains ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SourceElementIds = sourceElementIds != null && sourceElementIds.Any() ? new HashSet<int>(sourceElementIds) : null,
            GybClearanceFeet = gybClearancePerSideInches / 12.0,
            WfrClearanceFeet = wfrClearancePerSideInches / 12.0,
            AxisToleranceDegrees = axisToleranceDegrees,
            TraceCommentPrefix = traceCommentPrefix?.Trim() ?? string.Empty,
            MaxResults = Math.Max(1, maxResults),
            IncludeExisting = includeExisting
        };
    }

    private static RoundPenetrationPlan ResolveRoundPenetrationPlan(Document doc, RoundPenetrationSettings settings)
    {
        var plan = new RoundPenetrationPlan();
        var allFamilyInstances = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .OrderBy(x => x.Id.Value)
            .ToList();

        plan.ExistingInstances.AddRange(CollectExistingRoundPenetrationInstances(allFamilyInstances, settings.TargetFamilyName, settings.TraceCommentPrefix));
        var existingMap = plan.ExistingInstances
            .Where(x => x.SourceElementId.HasValue && x.HostElementId.HasValue)
            .GroupBy(x => new RoundPenetrationPairKey(x.SourceElementId!.Value, x.HostElementId!.Value))
            .ToDictionary(x => x.Key, x => x.OrderBy(info => info.Instance.Id.Value).ToList());

        var hosts = allFamilyInstances
            .Where(x => MatchesHostCandidate(x, settings))
            .OrderBy(x => x.Id.Value)
            .Cast<Element>()
            .ToList();

        var hostByCassette = hosts
            .GroupBy(x => ReadEffectiveParameterValue(x, "Mii_CassetteID"), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key ?? string.Empty, x => x.OrderBy(h => h.Id.Value).ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var source in allFamilyInstances.Where(x => MatchesSourceCandidate(x, settings)))
        {
            if (settings.SourceElementIds != null && !settings.SourceElementIds.Contains(checked((int)source.Id.Value)))
            {
                continue;
            }

            if (!TryResolveSourceTransform(source, out var sourceOrigin, out var sourceBasisX))
            {
                continue;
            }

            var sourceCassetteId = ReadEffectiveParameterValue(source, "Mii_CassetteID");
            var nominalOdDisplay = ReadFirstEffectiveNonEmptyParameter(source, "Mii_Diameter", "Mii_DimDiameter", "Diameter");
            if (!TryResolveEffectiveLengthFeet(source, out var nominalOdFeet, "Mii_Diameter", "Mii_DimDiameter", "Diameter"))
            {
                continue;
            }

            var sourceAxisSearchHalfLength = ResolveSourceAxisSearchHalfLength(source, sourceBasisX);
            var sourceBox = source.get_BoundingBox(null);
            var candidateHosts = !string.IsNullOrWhiteSpace(sourceCassetteId) && hostByCassette.TryGetValue(sourceCassetteId, out var cassetteHosts)
                ? cassetteHosts
                : hosts;

            foreach (var host in candidateHosts.OrderBy(x => x.Id.Value))
            {
                if (plan.Items.Count >= settings.MaxResults)
                {
                    return plan;
                }

                if (host.Id == source.Id)
                {
                    continue;
                }

                if (!BoundingBoxesLikelyOverlap(sourceBox, host.get_BoundingBox(null), 0.25))
                {
                    continue;
                }

                var hostClass = NormalizeToken(ReadEffectiveParameterValue(host, "Mii_ElementClass"));
                if (!settings.HostElementClasses.Contains(hostClass))
                {
                    continue;
                }

                var pairKey = new RoundPenetrationPairKey(checked((int)source.Id.Value), checked((int)host.Id.Value));
                existingMap.TryGetValue(pairKey, out var existingCandidates);

                var clearancePerSideFeet = ResolveRoundPenetrationClearanceFeet(hostClass, settings);
                var hasHostIntersection = TryResolveHostIntersection(sourceOrigin, sourceBasisX, sourceAxisSearchHalfLength, host, out var computedPlacementPoint, out var hostIntersectionLengthFeet);
                if (!hasHostIntersection && (existingCandidates == null || existingCandidates.Count == 0))
                {
                    continue;
                }

                var openingDiameterFeet = nominalOdFeet + (clearancePerSideFeet * 2.0);
                var cutLengthFeet = Math.Max(hostIntersectionLengthFeet + (clearancePerSideFeet * 2.0), 1.0 / 12.0);
                var existing = settings.IncludeExisting
                    ? SelectBestExistingRoundPenetration(existingCandidates, host, hasHostIntersection ? computedPlacementPoint : (XYZ?)null)
                    : null;

                if (existing != null)
                {
                    if (TryResolveExistingRoundPenetrationDouble(existing.Instance, "BIM765T_OpeningDiameter", out var existingOpeningDiameterFeet))
                    {
                        openingDiameterFeet = existingOpeningDiameterFeet;
                    }

                    if (TryResolveExistingRoundPenetrationDouble(existing.Instance, "BIM765T_CutLength", out var existingCutLengthFeet))
                    {
                        cutLengthFeet = Math.Max(existingCutLengthFeet, 1.0 / 12.0);
                    }
                }

                var placementPoint = hasHostIntersection
                    ? computedPlacementPoint
                    : XYZ.Zero;

                if (existing != null)
                {
                    if (existing.StoredPlacementPoint is XYZ storedPlacementPoint)
                    {
                        placementPoint = storedPlacementPoint;
                    }
                    else if (TryGetRoundPenetrationInsertionPoint(existing.Instance, out var existingInsertionPoint))
                    {
                        placementPoint = existingInsertionPoint;
                    }
                }

                var logicalTypeName = BuildRoundPenetrationTypeName(hostClass, nominalOdFeet, openingDiameterFeet, cutLengthFeet);
                var familyName = settings.TargetFamilyName;
                var residualNote = string.Empty;
                var canPlace = openingDiameterFeet > 1e-6 && (hasHostIntersection || existing != null);
                var canCut = InstanceVoidCutUtils.CanBeCutWithVoid(host);

                if (!canCut)
                {
                    residualNote = $"Host {host.Id.Value} ({hostClass}) cannot be cut with instance voids.";
                }

                if (!hasHostIntersection && existing != null)
                {
                    residualNote = "Host intersection is already modified by an existing traced opening; reuse stored opening placement for QC.";
                }

                if (existing != null)
                {
                    existing.CutExists = InstanceVoidCutUtils.InstanceVoidCutExists(host, existing.Instance);
                    residualNote = existing.CutExists
                        ? "Existing traced opening already cuts the host."
                        : "Existing traced opening found but cut relation is missing.";
                    canPlace = false;
                    canCut = existing.CutExists;
                }

                if (!canPlace && string.IsNullOrWhiteSpace(residualNote))
                {
                    residualNote = "Opening diameter or placement point could not be resolved.";
                }

                plan.Items.Add(new RoundPenetrationPlanItem
                {
                    SourceInstance = source,
                    HostElement = host,
                    HostClass = hostClass,
                    CassetteId = sourceCassetteId,
                    SourceOrigin = sourceOrigin,
                    SourceBasisX = sourceBasisX,
                    NominalOdDisplay = nominalOdDisplay,
                    NominalOdFeet = nominalOdFeet,
                    OpeningDiameterFeet = openingDiameterFeet,
                    CutLengthFeet = cutLengthFeet,
                    ClearancePerSideFeet = clearancePerSideFeet,
                    PlacementPoint = placementPoint,
                    FamilySpec = new RoundPenetrationFamilySpec
                    {
                        FamilyName = familyName,
                        TypeName = logicalTypeName,
                        NominalOdFeet = nominalOdFeet,
                        OpeningDiameterFeet = openingDiameterFeet,
                        VoidLengthFeet = cutLengthFeet,
                        HostClass = hostClass,
                        ClearancePerSideFeet = clearancePerSideFeet
                    },
                    TraceComment = BuildRoundPenetrationTraceComment(settings.TraceCommentPrefix, source, host, hostClass, sourceCassetteId),
                    CanPlace = canPlace,
                    CanCut = canCut,
                    ResidualNote = residualNote,
                    ExistingInfo = existing
                });
            }
        }

        return plan;
    }

    private static IEnumerable<ExistingRoundPenetrationInstanceInfo> CollectExistingRoundPenetrationInstances(IEnumerable<FamilyInstance> instances, string targetFamilyName, string tracePrefix)
    {
        foreach (var instance in instances)
        {
            var familyName = instance.Symbol?.Family?.Name ?? string.Empty;
            if (!IsRoundPenetrationFamilyMatch(familyName, targetFamilyName))
            {
                continue;
            }

            var traceComment = ReadFirstNonEmptyParameter(instance, "BIM765T_TraceComment", "Comments");
            if (!string.IsNullOrWhiteSpace(tracePrefix) && !traceComment.StartsWith(tracePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var traceParsed = TryParseRoundPenetrationTrace(traceComment, out var sourceId, out var hostId);
            if (!traceParsed)
            {
                sourceId = 0;
                hostId = 0;
            }
            XYZ? storedPlacementPoint = null;
            if (TryParseRoundPenetrationPoint(ReadFirstNonEmptyParameter(instance, "BIM765T_PlannedPoint"), out var parsedPlacementPoint))
            {
                storedPlacementPoint = parsedPlacementPoint;
            }

            yield return new ExistingRoundPenetrationInstanceInfo
            {
                Instance = instance,
                TraceComment = traceComment,
                SourceElementId = sourceId,
                HostElementId = hostId,
                StoredPlacementPoint = storedPlacementPoint
            };
        }
    }

    private static ExistingRoundPenetrationInstanceInfo? SelectBestExistingRoundPenetration(
        IReadOnlyList<ExistingRoundPenetrationInstanceInfo>? candidates,
        Element host,
        XYZ? expectedPlacementPoint)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .Select(candidate =>
            {
                var hasCut = InstanceVoidCutUtils.InstanceVoidCutExists(host, candidate.Instance);
                var anchor = candidate.StoredPlacementPoint;
                if (anchor == null && TryGetRoundPenetrationInsertionPoint(candidate.Instance, out var insertionPoint))
                {
                    anchor = insertionPoint;
                }

                var distance = expectedPlacementPoint != null && anchor != null
                    ? anchor.DistanceTo(expectedPlacementPoint)
                    : double.MaxValue;

                return new
                {
                    Candidate = candidate,
                    HasCut = hasCut,
                    HasStoredPoint = candidate.StoredPlacementPoint != null,
                    Distance = distance
                };
            })
            .OrderByDescending(x => x.HasCut)
            .ThenByDescending(x => x.HasStoredPoint)
            .ThenBy(x => x.Distance)
            .ThenByDescending(x => x.Candidate.Instance.Id.Value)
            .Select(x => x.Candidate)
            .FirstOrDefault();
    }

    private static bool TryResolveExistingRoundPenetrationDouble(FamilyInstance instance, string parameterName, out double value)
    {
        value = 0.0;
        var parameter = instance.LookupParameter(parameterName)
                        ?? instance.Symbol?.LookupParameter(parameterName)
                        ?? instance.Symbol?.Family?.LookupParameter(parameterName);
        if (parameter == null)
        {
            return false;
        }

        if (parameter.StorageType == StorageType.Double)
        {
            value = parameter.AsDouble();
            return value > 1e-9;
        }

        if (parameter.StorageType == StorageType.Integer)
        {
            value = parameter.AsInteger();
            return true;
        }

        return false;
    }

    private static bool MatchesSourceCandidate(FamilyInstance instance, RoundPenetrationSettings settings)
    {
        var effectiveClass = NormalizeToken(ReadEffectiveParameterValue(instance, "Mii_ElementClass"));
        if (settings.SourceElementClasses.Contains(effectiveClass))
        {
            return true;
        }

        var familyName = instance.Symbol?.Family?.Name ?? string.Empty;
        return settings.SourceFamilyNameContains.Any(token => familyName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool MatchesHostCandidate(FamilyInstance instance, RoundPenetrationSettings settings)
    {
        var effectiveClass = NormalizeToken(ReadEffectiveParameterValue(instance, "Mii_ElementClass"));
        return settings.HostElementClasses.Contains(effectiveClass);
    }

    private static string ReadEffectiveParameterValue(Element element, params string[] parameterNames)
    {
        foreach (var parameterName in parameterNames)
        {
            var value = ReadParameterValue(element, parameterName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        var typeId = element.GetTypeId();
        if (typeId == null || typeId == ElementId.InvalidElementId)
        {
            return string.Empty;
        }

        var typeElement = element.Document.GetElement(typeId);
        if (typeElement == null)
        {
            return string.Empty;
        }

        foreach (var parameterName in parameterNames)
        {
            var value = ReadParameterValue(typeElement, parameterName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string ReadFirstEffectiveNonEmptyParameter(Element element, params string[] parameterNames)
    {
        return ReadEffectiveParameterValue(element, parameterNames);
    }

    private static bool TryResolveEffectiveLengthFeet(Element element, out double feet, params string[] parameterNames)
    {
        feet = 0.0;
        foreach (var parameterName in parameterNames)
        {
            if (TryReadLengthFeet(element, parameterName, out feet))
            {
                return true;
            }
        }

        var typeId = element.GetTypeId();
        if (typeId == null || typeId == ElementId.InvalidElementId)
        {
            return false;
        }

        var typeElement = element.Document.GetElement(typeId);
        if (typeElement == null)
        {
            return false;
        }

        foreach (var parameterName in parameterNames)
        {
            if (TryReadLengthFeet(typeElement, parameterName, out feet))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadLengthFeet(Element element, string parameterName, out double feet)
    {
        feet = 0.0;
        var parameter = element.LookupParameter(parameterName);
        if (parameter == null)
        {
            return false;
        }

        if (parameter.StorageType == StorageType.Double)
        {
            feet = parameter.AsDouble();
            return feet > 1e-9;
        }

        var value = parameter.AsValueString();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = PlatformServices.ParameterValue(parameter);
        }

        return TryParseImperialLengthStringToFeet(value, out feet) && feet > 1e-9;
    }

    private static bool TryResolveSourceTransform(FamilyInstance source, out XYZ origin, out XYZ basisX)
    {
        origin = XYZ.Zero;
        basisX = XYZ.Zero;

        if (source.Location is LocationCurve locationCurve && locationCurve.Curve != null)
        {
            try
            {
                var curve = locationCurve.Curve;
                origin = curve.Evaluate(0.5, true);
                var tangent = curve.ComputeDerivatives(0.5, true).BasisX;
                basisX = SafeNormalize(tangent);
                if (!basisX.IsZeroLength())
                {
                    return true;
                }
            }
            catch
            {
                // fallback below
            }
        }

        try
        {
            var transform = source.GetTransform();
            if (transform == null)
            {
                return false;
            }

            origin = transform.Origin;
            basisX = SafeNormalize(transform.BasisX);
            return !basisX.IsZeroLength();
        }
        catch
        {
            return false;
        }
    }

    private static double ResolveSourceAxisSearchHalfLength(FamilyInstance source, XYZ basisX)
    {
        var halfLength = 1.0;
        if (source.Location is LocationCurve locationCurve && locationCurve.Curve != null)
        {
            try
            {
                halfLength = Math.Max(halfLength, locationCurve.Curve.Length * 0.5);
            }
            catch
            {
                // fallback below
            }
        }

        if (TryResolveEffectiveLengthFeet(source, out var parameterLengthFeet, "Mii_DimLength", "Length"))
        {
            halfLength = Math.Max(halfLength, parameterLengthFeet * 0.5);
        }

        var bbox = source.get_BoundingBox(null);
        if (bbox != null)
        {
            halfLength = Math.Max(halfLength, EstimateProjectedBoundingBoxLength(bbox, basisX) * 0.5);
        }

        return halfLength + 0.5;
    }

    private static double EstimateProjectedBoundingBoxLength(BoundingBoxXYZ bbox, XYZ axis)
    {
        var points = new[]
        {
            new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Min.Z),
            new XYZ(bbox.Min.X, bbox.Min.Y, bbox.Max.Z),
            new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Min.Z),
            new XYZ(bbox.Min.X, bbox.Max.Y, bbox.Max.Z),
            new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Min.Z),
            new XYZ(bbox.Max.X, bbox.Min.Y, bbox.Max.Z),
            new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Min.Z),
            new XYZ(bbox.Max.X, bbox.Max.Y, bbox.Max.Z)
        };

        var projections = points.Select(x => x.DotProduct(axis)).ToList();
        return projections.Max() - projections.Min();
    }

    private static bool BoundingBoxesLikelyOverlap(BoundingBoxXYZ? left, BoundingBoxXYZ? right, double paddingFeet)
    {
        if (left == null || right == null)
        {
            return false;
        }

        return !(left.Max.X + paddingFeet < right.Min.X - paddingFeet ||
                 left.Min.X - paddingFeet > right.Max.X + paddingFeet ||
                 left.Max.Y + paddingFeet < right.Min.Y - paddingFeet ||
                 left.Min.Y - paddingFeet > right.Max.Y + paddingFeet ||
                 left.Max.Z + paddingFeet < right.Min.Z - paddingFeet ||
                 left.Min.Z - paddingFeet > right.Max.Z + paddingFeet);
    }

    private static double ResolveRoundPenetrationClearanceFeet(string hostClass, RoundPenetrationSettings settings)
    {
        return string.Equals(hostClass, "GYB", StringComparison.OrdinalIgnoreCase)
            ? settings.GybClearanceFeet
            : settings.WfrClearanceFeet;
    }

    private static bool TryResolveHostIntersection(XYZ origin, XYZ axis, double halfLength, Element host, out XYZ placementPoint, out double hostIntersectionLengthFeet)
    {
        placementPoint = XYZ.Zero;
        hostIntersectionLengthFeet = 0.0;

        var line = Line.CreateBound(origin - axis.Multiply(halfLength), origin + axis.Multiply(halfLength));
        var solids = CollectSolids(host);
        if (solids.Count == 0)
        {
            return false;
        }

        var intervals = new List<RoundPenetrationAxisInterval>();
        foreach (var solid in solids)
        {
            if (solid == null || solid.Volume <= 1e-9)
            {
                continue;
            }

            using var intersection = solid.IntersectWithCurve(line, new SolidCurveIntersectionOptions());
            for (var index = 0; index < intersection.SegmentCount; index++)
            {
                var segment = intersection.GetCurveSegment(index);
                if (segment == null)
                {
                    continue;
                }

                var start = (segment.GetEndPoint(0) - origin).DotProduct(axis);
                var end = (segment.GetEndPoint(1) - origin).DotProduct(axis);
                intervals.Add(new RoundPenetrationAxisInterval(Math.Min(start, end), Math.Max(start, end)));
            }
        }

        if (intervals.Count == 0)
        {
            return false;
        }

        var mergedIntervals = MergeRoundPenetrationAxisIntervals(intervals);
        var chosen = mergedIntervals
            .OrderBy(x => ComputeRoundPenetrationIntervalDistanceToZero(x))
            .ThenBy(x => Math.Abs((x.Start + x.End) * 0.5))
            .First();

        var midpointParameter = (chosen.Start + chosen.End) * 0.5;
        placementPoint = origin + axis.Multiply(midpointParameter);
        hostIntersectionLengthFeet = chosen.Length;
        return hostIntersectionLengthFeet > 1e-9;
    }

    private static IReadOnlyList<RoundPenetrationAxisInterval> MergeRoundPenetrationAxisIntervals(IEnumerable<RoundPenetrationAxisInterval> intervals)
    {
        const double toleranceFeet = 1.0 / 1024.0;
        var ordered = intervals
            .OrderBy(x => x.Start)
            .ThenBy(x => x.End)
            .ToList();
        if (ordered.Count == 0)
        {
            return Array.Empty<RoundPenetrationAxisInterval>();
        }

        var merged = new List<RoundPenetrationAxisInterval>();
        var current = ordered[0];
        for (var index = 1; index < ordered.Count; index++)
        {
            var next = ordered[index];
            if (next.Start <= current.End + toleranceFeet)
            {
                current = new RoundPenetrationAxisInterval(current.Start, Math.Max(current.End, next.End));
                continue;
            }

            merged.Add(current);
            current = next;
        }

        merged.Add(current);
        return merged;
    }

    private static double ComputeRoundPenetrationIntervalDistanceToZero(RoundPenetrationAxisInterval interval)
    {
        if (interval.Start <= 0.0 && interval.End >= 0.0)
        {
            return 0.0;
        }

        return Math.Min(Math.Abs(interval.Start), Math.Abs(interval.End));
    }

    private static List<Solid> CollectSolids(Element element)
    {
        var solids = new List<Solid>();
        var options = new Options
        {
            ComputeReferences = false,
            IncludeNonVisibleObjects = true,
            DetailLevel = ViewDetailLevel.Fine
        };

        CollectSolidsRecursive(element.get_Geometry(options), solids);
        return solids;
    }

    private static void CollectSolidsRecursive(GeometryElement? geometryElement, ICollection<Solid> solids)
    {
        if (geometryElement == null)
        {
            return;
        }

        foreach (var geometryObject in geometryElement)
        {
            if (geometryObject is Solid solid && solid.Volume > 1e-9)
            {
                solids.Add(solid);
                continue;
            }

            if (geometryObject is GeometryInstance geometryInstance)
            {
                CollectSolidsRecursive(geometryInstance.GetInstanceGeometry(), solids);
                continue;
            }

            if (geometryObject is GeometryElement nestedGeometry)
            {
                CollectSolidsRecursive(nestedGeometry, solids);
            }
        }
    }

    private static string NormalizeToken(string? value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static string BuildRoundPenetrationTypeName(string hostClass, double nominalOdFeet, double openingDiameterFeet, double cutLengthFeet)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}__OD{1}__OPEN{2}__LEN{3}",
            NormalizeToken(hostClass),
            BuildRoundPenetrationSizeToken(nominalOdFeet),
            BuildRoundPenetrationSizeToken(openingDiameterFeet),
            BuildRoundPenetrationSizeToken(cutLengthFeet));
    }

    private static string BuildRoundPenetrationSizeToken(double feet)
    {
        var value = (int)Math.Round(feet * 12.0 * 256.0, MidpointRounding.AwayFromZero);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string NormalizeRoundPenetrationFamilyName(string? targetFamilyName)
    {
        return string.IsNullOrWhiteSpace(targetFamilyName) ? "Mii_Pen-Round_Project" : targetFamilyName!.Trim();
    }

    private static bool IsRoundPenetrationFamilyMatch(string familyName, string targetFamilyName)
    {
        if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(targetFamilyName))
        {
            return false;
        }

        return string.Equals(familyName, targetFamilyName, StringComparison.OrdinalIgnoreCase)
            || familyName.StartsWith(targetFamilyName + "__", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRoundPenetrationTraceComment(string prefix, Element source, Element host, string hostClass, string cassetteId)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}|source={1}|host={2}|hostClass={3}|cassette={4}",
            prefix,
            source.Id.Value,
            host.Id.Value,
            hostClass,
            cassetteId ?? string.Empty);
    }

    private static string SerializeRoundPenetrationPoint(XYZ point)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}|{1}|{2}",
            point.X.ToString("0.########", CultureInfo.InvariantCulture),
            point.Y.ToString("0.########", CultureInfo.InvariantCulture),
            point.Z.ToString("0.########", CultureInfo.InvariantCulture));
    }

    private static bool TryParseRoundPenetrationPoint(string? raw, out XYZ point)
    {
        point = XYZ.Zero;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var tokens = raw!.Split('|');
        if (tokens.Length != 3)
        {
            return false;
        }

        if (!double.TryParse(tokens[0], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(tokens[1], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var y)
            || !double.TryParse(tokens[2], NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var z))
        {
            return false;
        }

        point = new XYZ(x, y, z);
        return true;
    }

    private static bool TryParseRoundPenetrationTrace(string traceComment, out int? sourceElementId, out int? hostElementId)
    {
        sourceElementId = TryParseTraceInteger(traceComment, "source=");
        hostElementId = TryParseTraceInteger(traceComment, "host=");
        return sourceElementId.HasValue || hostElementId.HasValue;
    }

    private static int? TryParseTraceInteger(string traceComment, string token)
    {
        if (string.IsNullOrWhiteSpace(traceComment))
        {
            return null;
        }

        var index = traceComment.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        index += token.Length;
        var end = traceComment.IndexOf('|', index);
        var raw = end >= 0 ? traceComment.Substring(index, end - index) : traceComment.Substring(index);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null;
    }

    private static Dictionary<string, FamilySymbol> EnsureRoundPenetrationFamilySymbols(Document doc, CreateRoundPenetrationCutBatchRequest request, IEnumerable<RoundPenetrationFamilySpec> specs, ICollection<DiagnosticRecord> diagnostics, ICollection<string> artifacts)
    {
        var requiredSpecs = specs
            .GroupBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new Dictionary<string, FamilySymbol>(StringComparer.OrdinalIgnoreCase);
        if (requiredSpecs.Count == 0)
        {
            return result;
        }

        var outputDirectory = ResolveRoundPenetrationOutputDirectory(request);
        Directory.CreateDirectory(outputDirectory);
        var canonicalFamilyName = NormalizeRoundPenetrationFamilyName(requiredSpecs[0].FamilyName);
        var existingFamily = FindFamilyByName(doc, canonicalFamilyName);
        var requiresCleanRebuild = request.ForceRebuildFamilies
                                   || existingFamily == null
                                   || IsRoundPenetrationFamilyLegacyOrDirty(doc, existingFamily);
        var existingSpecs = requiresCleanRebuild
            ? Array.Empty<RoundPenetrationFamilySpec>()
            : CollectCurrentRoundPenetrationSpecsFromFamily(doc, existingFamily);
        var effectiveSpecs = MergeRoundPenetrationFamilySpecs(requiredSpecs, existingSpecs);

        if (existingFamily != null && !requiresCleanRebuild)
        {
            var reusableSymbols = ResolveRoundPenetrationSymbolsByType(doc, existingFamily, requiredSpecs.Select(x => x.TypeName));
            if (reusableSymbols.Count == requiredSpecs.Count)
            {
                foreach (var spec in requiredSpecs)
                {
                    var reusable = reusableSymbols[spec.TypeName];
                    result[spec.TypeName] = reusable;
                    diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_FAMILY_REUSED", DiagnosticSeverity.Info, $"Reuse loaded family {canonicalFamilyName}/{spec.TypeName}.", checked((int)reusable.Id.Value)));
                }

                return result;
            }

            if (TryEnsureRoundPenetrationProjectSymbolTypes(doc, existingFamily, requiredSpecs, diagnostics))
            {
                reusableSymbols = ResolveRoundPenetrationSymbolsByType(doc, existingFamily, requiredSpecs.Select(x => x.TypeName));
                if (reusableSymbols.Count == requiredSpecs.Count)
                {
                    foreach (var spec in requiredSpecs)
                    {
                        var reusable = reusableSymbols[spec.TypeName];
                        result[spec.TypeName] = reusable;
                        diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_FAMILY_REUSED_AFTER_TYPE_SYNC", DiagnosticSeverity.Info, $"Reuse loaded family {canonicalFamilyName}/{spec.TypeName} after project type sync.", checked((int)reusable.Id.Value)));
                    }

                    return result;
                }
            }
        }

        var family = BuildRoundPenetrationFamilyFile(
            doc,
            canonicalFamilyName,
            effectiveSpecs,
            outputDirectory,
            request.OverwriteFamilyFiles,
            request.OverwriteExistingProjectFamilies,
            diagnostics,
            out var familyFilePath);

        var loadedSymbols = ResolveRoundPenetrationSymbolsByType(doc, family, requiredSpecs.Select(x => x.TypeName));
        foreach (var spec in requiredSpecs)
        {
            if (!loadedSymbols.TryGetValue(spec.TypeName, out var symbol))
            {
                throw new InvalidOperationException($"Cannot resolve symbol {spec.TypeName} in family {canonicalFamilyName} after load.");
            }

            result[spec.TypeName] = symbol;
        }

        artifacts.Add("familyFile=" + familyFilePath);
        return result;
    }

    private static bool TryEnsureRoundPenetrationProjectSymbolTypes(Document doc, Family existingFamily, IReadOnlyList<RoundPenetrationFamilySpec> requiredSpecs, ICollection<DiagnosticRecord> diagnostics)
    {
        var existingSymbols = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Where(x => x.Family != null && x.Family.Id == existingFamily.Id)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (existingSymbols.Count == 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_TYPE_SYNC_NO_SEED", DiagnosticSeverity.Warning, $"Loaded family {existingFamily.Name} has no symbols to duplicate.", checked((int)existingFamily.Id.Value)));
            return false;
        }

        var missingSpecs = requiredSpecs
            .Where(spec => existingSymbols.All(symbol => !string.Equals(symbol.Name ?? string.Empty, spec.TypeName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (missingSpecs.Count == 0)
        {
            return true;
        }

        using var transaction = new Transaction(doc, "Ensure round penetration project types");
        transaction.Start();
        foreach (var spec in missingSpecs)
        {
            var seedSymbol = ResolveRoundPenetrationSeedSymbol(existingSymbols, spec) ?? existingSymbols[0];
            if (seedSymbol.Duplicate(spec.TypeName) is not FamilySymbol duplicated)
            {
                diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_TYPE_SYNC_DUPLICATE_FAILED", DiagnosticSeverity.Warning, $"Failed to duplicate a seed type for {spec.TypeName}.", checked((int)seedSymbol.Id.Value)));
                continue;
            }

            SetRoundPenetrationSymbolTypeParameters(duplicated, spec, diagnostics);
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_TYPE_SYNC_CREATED", DiagnosticSeverity.Info, $"Created project type {existingFamily.Name}/{duplicated.Name}.", checked((int)duplicated.Id.Value)));
            existingSymbols.Add(duplicated);
        }

        doc.Regenerate();
        transaction.Commit();
        return true;
    }

    private static FamilySymbol? ResolveRoundPenetrationSeedSymbol(IEnumerable<FamilySymbol> candidates, RoundPenetrationFamilySpec targetSpec)
    {
        return candidates
            .Select(symbol =>
            {
                var score = 1000.0;
                if (TryParseRoundPenetrationFamilySpec(symbol.Family?.Name ?? string.Empty, symbol.Name ?? string.Empty, out var existingSpec))
                {
                    score = Math.Abs(existingSpec.NominalOdFeet - targetSpec.NominalOdFeet)
                        + Math.Abs(existingSpec.OpeningDiameterFeet - targetSpec.OpeningDiameterFeet)
                        + Math.Abs(existingSpec.VoidLengthFeet - targetSpec.VoidLengthFeet);
                    if (!string.Equals(existingSpec.HostClass, targetSpec.HostClass, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 100.0;
                    }
                }

                return new { Symbol = symbol, Score = score };
            })
            .OrderBy(x => x.Score)
            .ThenBy(x => x.Symbol.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Symbol)
            .FirstOrDefault();
    }

    private static void SetRoundPenetrationSymbolTypeParameters(FamilySymbol symbol, RoundPenetrationFamilySpec spec, ICollection<DiagnosticRecord> diagnostics)
    {
        TrySetRoundPenetrationTypeParameter(symbol, "BIM765T_OpeningDiameter", spec.OpeningDiameterFeet, diagnostics);
        TrySetRoundPenetrationTypeParameter(symbol, "BIM765T_CutLength", spec.VoidLengthFeet, diagnostics);
        TrySetRoundPenetrationTypeParameter(symbol, "BIM765T_NominalOD", spec.NominalOdFeet, diagnostics);
        TrySetRoundPenetrationTypeParameter(symbol, "BIM765T_SchemaVersion", RoundPenetrationFamilySchemaVersion, diagnostics);
        TrySetRoundPenetrationTypeParameter(symbol, "BIM765T_ClearancePerSide", spec.ClearancePerSideFeet, diagnostics);
    }

    private static void TrySetRoundPenetrationTypeParameter(ElementType type, string parameterName, double value, ICollection<DiagnosticRecord> diagnostics)
    {
        var parameter = type.LookupParameter(parameterName);
        if (parameter == null)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_TYPE_SYNC_PARAMETER_MISSING", DiagnosticSeverity.Warning, $"Type {type.Name} is missing parameter {parameterName}.", checked((int)type.Id.Value)));
            return;
        }

        if (parameter.IsReadOnly)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_TYPE_SYNC_PARAMETER_READONLY", DiagnosticSeverity.Warning, $"Type {type.Name} parameter {parameterName} is read-only.", checked((int)type.Id.Value)));
            return;
        }

        try
        {
            if (parameter.StorageType == StorageType.Double)
            {
                parameter.Set(value);
            }
            else if (parameter.StorageType == StorageType.Integer)
            {
                parameter.Set((int)Math.Round(value, MidpointRounding.AwayFromZero));
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_TYPE_SYNC_PARAMETER_SET_FAILED", DiagnosticSeverity.Warning, $"{parameterName}: {ex.Message}", checked((int)type.Id.Value)));
        }
    }

    private static void TrySetRoundPenetrationTypeParameter(ElementType type, string parameterName, int value, ICollection<DiagnosticRecord> diagnostics)
    {
        var parameter = type.LookupParameter(parameterName);
        if (parameter == null)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_TYPE_SYNC_PARAMETER_MISSING", DiagnosticSeverity.Warning, $"Type {type.Name} is missing parameter {parameterName}.", checked((int)type.Id.Value)));
            return;
        }

        if (parameter.IsReadOnly)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_TYPE_SYNC_PARAMETER_READONLY", DiagnosticSeverity.Warning, $"Type {type.Name} parameter {parameterName} is read-only.", checked((int)type.Id.Value)));
            return;
        }

        try
        {
            if (parameter.StorageType == StorageType.Integer)
            {
                parameter.Set(value);
            }
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_TYPE_SYNC_PARAMETER_SET_FAILED", DiagnosticSeverity.Warning, $"{parameterName}: {ex.Message}", checked((int)type.Id.Value)));
        }
    }

    private static string ResolveRoundPenetrationOutputDirectory(CreateRoundPenetrationCutBatchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.OutputDirectory))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.OutputDirectory.Trim()));
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), BridgeConstants.AppDataFolderName, "generated", "round_penetration_project");
    }

    private static IReadOnlyList<RoundPenetrationFamilySpec> MergeRoundPenetrationFamilySpecs(IEnumerable<RoundPenetrationFamilySpec> requiredSpecs, IEnumerable<RoundPenetrationFamilySpec> existingSpecs)
    {
        var map = new Dictionary<string, RoundPenetrationFamilySpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in existingSpecs.Concat(requiredSpecs))
        {
            map[spec.TypeName] = spec;
        }

        return map.Values
            .OrderBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<RoundPenetrationFamilySpec> CollectCurrentRoundPenetrationSpecsFromFamily(Document doc, Family? family)
    {
        if (family == null)
        {
            return Array.Empty<RoundPenetrationFamilySpec>();
        }

        var result = new List<RoundPenetrationFamilySpec>();
        foreach (var symbol in new FilteredElementCollector(doc)
                     .OfClass(typeof(FamilySymbol))
                     .Cast<FamilySymbol>()
                     .Where(x => x.Family != null && x.Family.Id == family.Id)
                     .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryParseRoundPenetrationFamilySpec(family.Name, symbol.Name ?? string.Empty, out var spec))
            {
                throw new InvalidOperationException($"Cannot rebuild family '{family.Name}' because existing type '{symbol.Name}' is not parseable.");
            }

            result.Add(spec);
        }

        return result;
    }

    private static bool IsRoundPenetrationFamilyLegacyOrDirty(Document doc, Family family)
    {
        var symbols = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Where(x => x.Family != null && x.Family.Id == family.Id)
            .ToList();
        if (symbols.Count == 0)
        {
            return true;
        }

        foreach (var symbol in symbols)
        {
            var parameters = symbol.Parameters.Cast<Parameter>().ToList();
            if (parameters.Any(x => x.Definition?.Name?.StartsWith("VIS_ROUND_PEN_", StringComparison.OrdinalIgnoreCase) == true))
            {
                return true;
            }

            if (symbol.LookupParameter("BIM765T_OpeningDiameter") == null
                || symbol.LookupParameter("BIM765T_CutLength") == null
                || symbol.LookupParameter("BIM765T_SchemaVersion") == null
                || symbol.LookupParameter("BIM765T_NominalOD") == null
                || symbol.LookupParameter("BIM765T_ClearancePerSide") == null)
            {
                return true;
            }

            var schemaVersion = symbol.LookupParameter("BIM765T_SchemaVersion");
            if (schemaVersion == null
                || schemaVersion.StorageType != StorageType.Integer
                || schemaVersion.AsInteger() != RoundPenetrationFamilySchemaVersion)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseRoundPenetrationFamilySpec(string familyName, string typeName, out RoundPenetrationFamilySpec spec)
    {
        spec = new RoundPenetrationFamilySpec();
        var tokens = (typeName ?? string.Empty).Split(new[] { "__" }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 4)
        {
            return false;
        }

        if (!TryParseRoundPenetrationSizeToken(tokens[1], "OD", out var nominalOdFeet)
            || !TryParseRoundPenetrationSizeToken(tokens[2], "OPEN", out var openingDiameterFeet)
            || !TryParseRoundPenetrationSizeToken(tokens[3], "LEN", out var voidLengthFeet))
        {
            return false;
        }

        spec = new RoundPenetrationFamilySpec
        {
            FamilyName = NormalizeRoundPenetrationFamilyName(familyName),
            TypeName = typeName ?? string.Empty,
            HostClass = NormalizeToken(tokens[0]),
            NominalOdFeet = nominalOdFeet,
            OpeningDiameterFeet = openingDiameterFeet,
            VoidLengthFeet = voidLengthFeet,
            ClearancePerSideFeet = Math.Max((openingDiameterFeet - nominalOdFeet) * 0.5, 0.0)
        };
        return true;
    }

    private static bool TryParseRoundPenetrationSizeToken(string token, string expectedPrefix, out double feet)
    {
        feet = 0.0;
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var raw = token.Substring(expectedPrefix.Length);
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var encoded))
        {
            return false;
        }

        feet = encoded / (12.0 * 256.0);
        return feet > 1e-9;
    }

    private static Family BuildRoundPenetrationFamilyFile(Document projectDoc, string familyName, IReadOnlyList<RoundPenetrationFamilySpec> requiredSpecs, string outputDirectory, bool overwriteFamilyFiles, bool overwriteExistingProjectFamilies, ICollection<DiagnosticRecord> diagnostics, out string filePath)
    {
        Directory.CreateDirectory(outputDirectory);
        filePath = Path.Combine(outputDirectory, SanitizeFileName(familyName) + ".rfa");
        var familyDoc = OpenRoundPenetrationFamilySeedDocument(projectDoc, familyName, filePath, diagnostics);

        try
        {
            using var transaction = new Transaction(familyDoc, "Build round penetration project family");
            transaction.Start();
            AgentFailureHandling.Configure(transaction, diagnostics as List<DiagnosticRecord> ?? new List<DiagnosticRecord>());

            var ownerFamily = familyDoc.OwnerFamily ?? throw new InvalidOperationException("Family document has no OwnerFamily.");
            ownerFamily.Name = familyName;
            SetFamilyToggle(ownerFamily.get_Parameter(BuiltInParameter.FAMILY_SHARED), 0);
            SetFamilyToggle(ownerFamily.get_Parameter(BuiltInParameter.FAMILY_ALWAYS_VERTICAL), 0);
            SetFamilyToggle(ownerFamily.get_Parameter(BuiltInParameter.FAMILY_WORK_PLANE_BASED), 1);
            SetFamilyToggle(ownerFamily.get_Parameter(BuiltInParameter.FAMILY_ALLOW_CUT_WITH_VOIDS), 1);

            var parameterMap = EnsureRoundPenetrationFamilyParameters(familyDoc);
            EnsureRoundPenetrationGeometry(familyDoc, parameterMap, diagnostics);
            EnsureRoundPenetrationFamilyTypes(familyDoc, requiredSpecs, parameterMap);
            transaction.Commit();

            SaveRoundPenetrationFamilyDocument(familyDoc, filePath, overwriteFamilyFiles);
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_FAMILY_FILE_BUILT", DiagnosticSeverity.Info, $"Built opening family {familyName} -> {filePath}."));

            if (projectDoc.IsModifiable)
            {
                throw new InvalidOperationException("Project document is modifiable; cannot load family while transaction is open.");
            }

            var loadedFamily = familyDoc.LoadFamily(projectDoc, new AlwaysOverwriteFamilyLoadOptions(overwriteExistingProjectFamilies));
            if (loadedFamily == null)
            {
                throw new InvalidOperationException($"LoadFamily returned null for {familyName}.");
            }

            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_FAMILY_LOADED", DiagnosticSeverity.Info, $"Loaded family {loadedFamily.Name}.", checked((int)loadedFamily.Id.Value)));
            return loadedFamily;
        }
        finally
        {
            familyDoc.Close(false);
        }
    }

    private static Document OpenRoundPenetrationFamilySeedDocument(Document projectDoc, string familyName, string filePath, ICollection<DiagnosticRecord> diagnostics)
    {
        if (projectDoc.IsModifiable)
        {
            throw new InvalidOperationException("Project document is modifiable; cannot open/edit family seed document.");
        }

        if (File.Exists(filePath))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_FAMILY_FILE_REPLACED", DiagnosticSeverity.Info, $"Rebuilding canonical family file {filePath} from a fresh template seed."));
        }

        var templatePath = ResolveRoundWrapperTemplatePath(projectDoc.Application);
        diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_FAMILY_TEMPLATE_SEED", DiagnosticSeverity.Info, $"Creating new family seed from template {templatePath}."));
        return projectDoc.Application.NewFamilyDocument(templatePath);
    }

    private static IReadOnlyList<RoundPenetrationFamilySpec> CollectCurrentRoundPenetrationSpecsFromFamilyDocument(Document familyDoc, string familyName)
    {
        var familyManager = familyDoc.FamilyManager;
        if (familyManager == null)
        {
            return Array.Empty<RoundPenetrationFamilySpec>();
        }

        var resolvedFamilyName = familyDoc.OwnerFamily?.Name;
        if (string.IsNullOrWhiteSpace(resolvedFamilyName))
        {
            resolvedFamilyName = familyName;
        }

        var result = new List<RoundPenetrationFamilySpec>();
        foreach (var type in familyManager.Types.Cast<FamilyType>().Where(x => x != null).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!TryParseRoundPenetrationFamilySpec(resolvedFamilyName!, type.Name ?? string.Empty, out var spec))
            {
                continue;
            }

            result.Add(spec);
        }

        return result;
    }

    private static void SaveRoundPenetrationFamilyDocument(Document familyDoc, string filePath, bool overwriteFamilyFiles)
    {
        var normalizedTargetPath = Path.GetFullPath(filePath);
        var normalizedCurrentPath = string.IsNullOrWhiteSpace(familyDoc.PathName)
            ? string.Empty
            : Path.GetFullPath(familyDoc.PathName);

        if (string.Equals(normalizedCurrentPath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            familyDoc.Save();
            return;
        }

        if (File.Exists(filePath) && !overwriteFamilyFiles)
        {
            throw new InvalidOperationException($"Family file already exists and OverwriteFamilyFiles=false: {filePath}");
        }

        familyDoc.SaveAs(filePath, new SaveAsOptions { OverwriteExistingFile = overwriteFamilyFiles });
    }

    private static RoundPenetrationFamilyParameterMap EnsureRoundPenetrationFamilyParameters(Document familyDoc)
    {
        var familyManager = familyDoc.FamilyManager ?? throw new InvalidOperationException("Family document khong co FamilyManager.");
        EnsureRoundPenetrationFamilySeedType(familyManager);
        var parameterMap = familyManager.Parameters.Cast<FamilyParameter>().ToDictionary(x => x.Definition.Name, x => x, StringComparer.OrdinalIgnoreCase);
        var typeOpeningDiameter = EnsureFamilyLengthParameter(familyManager, parameterMap, "BIM765T_OpeningDiameter", false);
        var typeCutLength = EnsureFamilyLengthParameter(familyManager, parameterMap, "BIM765T_CutLength", false);
        var typeExtrusionStart = EnsureFamilyLengthParameter(familyManager, parameterMap, "BIM765T_ExtrusionStart", false);
        var typeExtrusionEnd = EnsureFamilyLengthParameter(familyManager, parameterMap, "BIM765T_ExtrusionEnd", false);
        var typeNominalOd = EnsureFamilyLengthParameter(familyManager, parameterMap, "BIM765T_NominalOD", false);
        var typeClearance = EnsureFamilyLengthParameter(familyManager, parameterMap, "BIM765T_ClearancePerSide", false);
        var typeSchemaVersion = EnsureFamilyIntegerParameter(familyManager, parameterMap, "BIM765T_SchemaVersion", false);
        var showReviewBody = EnsureFamilyYesNoParameter(familyManager, parameterMap, "BIM765T_ShowReviewBody", true);
        var sourceElementId = EnsureFamilyTextParameter(familyManager, parameterMap, "BIM765T_SourceElementId");
        var hostElementId = EnsureFamilyTextParameter(familyManager, parameterMap, "BIM765T_HostElementId");
        var hostClass = EnsureFamilyTextParameter(familyManager, parameterMap, "BIM765T_HostClass");
        var cassetteId = EnsureFamilyTextParameter(familyManager, parameterMap, "BIM765T_CassetteId");
        var plannedPoint = EnsureFamilyTextParameter(familyManager, parameterMap, "BIM765T_PlannedPoint");
        var traceComment = EnsureFamilyTextParameter(familyManager, parameterMap, "BIM765T_TraceComment");

        SetFamilyFormula(familyManager, typeExtrusionStart, "- BIM765T_CutLength / 2");
        SetFamilyFormula(familyManager, typeExtrusionEnd, "BIM765T_CutLength / 2");

        return new RoundPenetrationFamilyParameterMap(
            typeOpeningDiameter,
            typeCutLength,
            typeExtrusionStart,
            typeExtrusionEnd,
            typeNominalOd,
            typeSchemaVersion,
            typeClearance,
            showReviewBody,
            sourceElementId,
            hostElementId,
            hostClass,
            cassetteId,
            plannedPoint,
            traceComment);
    }

    private static FamilyType EnsureRoundPenetrationFamilySeedType(FamilyManager familyManager)
    {
        var currentType = familyManager.CurrentType;
        if (currentType != null)
        {
            return currentType;
        }

        currentType = familyManager.Types
            .Cast<FamilyType>()
            .FirstOrDefault(x => x != null);
        if (currentType != null)
        {
            familyManager.CurrentType = currentType;
            return currentType;
        }

        currentType = familyManager.NewType("BIM765T_Seed");
        familyManager.CurrentType = currentType;
        return currentType;
    }

    private static FamilyParameter EnsureFamilyTextParameter(FamilyManager familyManager, IDictionary<string, FamilyParameter> parameterMap, string parameterName)
    {
        if (!parameterMap.TryGetValue(parameterName, out var parameter))
        {
            parameter = familyManager.AddParameter(parameterName, GroupTypeId.General, SpecTypeId.String.Text, true);
            parameterMap[parameterName] = parameter;
        }

        return parameter;
    }

    private static FamilyParameter EnsureFamilyLengthParameter(FamilyManager familyManager, IDictionary<string, FamilyParameter> parameterMap, string parameterName, bool instance)
    {
        if (!parameterMap.TryGetValue(parameterName, out var parameter))
        {
            parameter = familyManager.AddParameter(parameterName, GroupTypeId.Geometry, SpecTypeId.Length, instance);
            parameterMap[parameterName] = parameter;
        }

        return parameter;
    }

    private static FamilyParameter EnsureFamilyIntegerParameter(FamilyManager familyManager, IDictionary<string, FamilyParameter> parameterMap, string parameterName, bool instance)
    {
        if (!parameterMap.TryGetValue(parameterName, out var parameter))
        {
            parameter = familyManager.AddParameter(parameterName, GroupTypeId.General, SpecTypeId.Int.Integer, instance);
            parameterMap[parameterName] = parameter;
        }

        return parameter;
    }

    private static FamilyParameter EnsureFamilyYesNoParameter(FamilyManager familyManager, IDictionary<string, FamilyParameter> parameterMap, string parameterName, bool instance)
    {
        if (!parameterMap.TryGetValue(parameterName, out var parameter))
        {
            parameter = familyManager.AddParameter(parameterName, GroupTypeId.Visibility, SpecTypeId.Boolean.YesNo, instance);
            parameterMap[parameterName] = parameter;
        }

        return parameter;
    }

    private static void SetFamilyFormula(FamilyManager familyManager, FamilyParameter parameter, string formula)
    {
        var currentFormula = parameter.Formula;
        if (parameter.CanAssignFormula && !string.Equals(currentFormula ?? string.Empty, formula ?? string.Empty, StringComparison.Ordinal))
        {
            familyManager.SetFormula(parameter, formula);
        }
    }

    private static Dictionary<string, FamilyType> EnsureRoundPenetrationFamilyTypes(Document familyDoc, IReadOnlyList<RoundPenetrationFamilySpec> specs, RoundPenetrationFamilyParameterMap parameterMap)
    {
        var familyManager = familyDoc.FamilyManager ?? throw new InvalidOperationException("Family document khong co FamilyManager.");
        var desiredSpecs = specs
            .GroupBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var desiredTypeNames = desiredSpecs.Select(x => x.TypeName).ToList();
        var existingTypes = familyManager.Types
            .Cast<FamilyType>()
            .Where(x => x != null)
            .ToList();
        var seedType = familyManager.CurrentType ?? existingTypes.FirstOrDefault();
        if (seedType == null)
        {
            throw new InvalidOperationException("Round penetration family template has no valid seed family type.");
        }

        if (familyManager.CurrentType == null || !string.Equals(familyManager.CurrentType.Name, seedType.Name, StringComparison.OrdinalIgnoreCase))
        {
            familyManager.CurrentType = seedType;
        }

        var typeMap = existingTypes
            .ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var desiredTypeName in desiredTypeNames)
        {
            if (!typeMap.ContainsKey(desiredTypeName))
            {
                if (familyManager.CurrentType == null)
                {
                    familyManager.CurrentType = seedType;
                }

                typeMap[desiredTypeName] = familyManager.NewType(desiredTypeName);
            }
        }

        foreach (var spec in desiredSpecs)
        {
            familyManager.CurrentType = typeMap[spec.TypeName];
            familyManager.Set(parameterMap.OpeningDiameter, spec.OpeningDiameterFeet);
            familyManager.Set(parameterMap.CutLength, spec.VoidLengthFeet);
            familyManager.Set(parameterMap.NominalOd, spec.NominalOdFeet);
            familyManager.Set(parameterMap.SchemaVersion, RoundPenetrationFamilySchemaVersion);
            familyManager.Set(parameterMap.ClearancePerSide, spec.ClearancePerSideFeet);
        }

        familyManager.CurrentType = typeMap[desiredSpecs[0].TypeName];
        return desiredTypeNames.ToDictionary(x => x, x => typeMap[x], StringComparer.OrdinalIgnoreCase);
    }

    private static void EnsureRoundPenetrationGeometry(Document familyDoc, RoundPenetrationFamilyParameterMap parameterMap, ICollection<DiagnosticRecord> diagnostics)
    {
        var existingExtrusions = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(Extrusion))
            .Cast<Extrusion>()
            .ToList();
        if (existingExtrusions.Count > 0)
        {
            return;
        }

        var sketchPlane = SketchPlane.Create(familyDoc, Plane.CreateByNormalAndOrigin(XYZ.BasisX, XYZ.Zero));
        var seedRadius = 1.0 / 16.0;
        var seedLength = 1.0 / 8.0;
        PrimeRoundPenetrationSeedTypeValues(familyDoc, parameterMap, seedRadius, seedLength);

        var voidExtrusion = CreateRoundPenetrationExtrusion(
            familyDoc,
            sketchPlane,
            seedRadius,
            seedLength,
            false,
            parameterMap,
            diagnostics,
            "ROUND_PEN_VOID_CREATED");
        if (voidExtrusion == null)
        {
            throw new InvalidOperationException("Khong tao duoc void extrusion cho opening family.");
        }

        SetFamilyToggle(voidExtrusion.get_Parameter(BuiltInParameter.ELEMENT_IS_CUTTING), 1);
        SetFamilyToggle(voidExtrusion.get_Parameter(BuiltInParameter.VOID_CUTS_GEOMETRY), 1);

        var solidExtrusion = CreateRoundPenetrationExtrusion(
            familyDoc,
            sketchPlane,
            seedRadius,
            seedLength,
            true,
            parameterMap,
            diagnostics,
            "ROUND_PEN_REVIEW_BODY_CREATED");
        if (solidExtrusion != null)
        {
            AssociateVisibilityParameter(familyDoc, solidExtrusion, parameterMap.ShowReviewBody, diagnostics, "ROUND_PEN_REVIEW_VISIBILITY");
        }
    }

    private static void PrimeRoundPenetrationSeedTypeValues(Document familyDoc, RoundPenetrationFamilyParameterMap parameterMap, double seedRadius, double seedLength)
    {
        var familyManager = familyDoc.FamilyManager ?? throw new InvalidOperationException("Family document khong co FamilyManager.");
        var seedDiameter = Math.Max(seedRadius * 2.0, 1.0 / 64.0);
        var safeSeedLength = Math.Max(seedLength, 1.0 / 64.0);

        familyManager.Set(parameterMap.OpeningDiameter, seedDiameter);
        familyManager.Set(parameterMap.CutLength, safeSeedLength);
        familyManager.Set(parameterMap.NominalOd, seedDiameter);
        familyManager.Set(parameterMap.ClearancePerSide, 0.0);
    }

    private static Extrusion? CreateRoundPenetrationExtrusion(
        Document familyDoc,
        SketchPlane sketchPlane,
        double seedRadius,
        double seedLength,
        bool isSolid,
        RoundPenetrationFamilyParameterMap parameterMap,
        ICollection<DiagnosticRecord> diagnostics,
        string diagnosticCode)
    {
        var profile = BuildCircularProfile(XYZ.Zero, seedRadius, XYZ.BasisY, XYZ.BasisZ);
        var extrusion = familyDoc.FamilyCreate.NewExtrusion(isSolid, profile, sketchPlane, seedLength);
        if (extrusion == null)
        {
            return null;
        }

        CenterRoundPenetrationExtrusionSeed(extrusion, seedLength);
        familyDoc.Regenerate();
        AssociateRoundPenetrationExtrusionParameters(familyDoc, extrusion, parameterMap);
        diagnostics.Add(DiagnosticRecord.Create(diagnosticCode, DiagnosticSeverity.Info, $"Created {(isSolid ? "review" : "void")} extrusion for round penetration family.", checked((int)extrusion.Id.Value)));
        return extrusion;
    }

    private static void CenterRoundPenetrationExtrusionSeed(Extrusion extrusion, double seedLength)
    {
        var start = extrusion.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM);
        if (start != null && !start.IsReadOnly && start.StorageType == StorageType.Double)
        {
            start.Set(-(seedLength * 0.5));
        }

        var end = extrusion.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM);
        if (end != null && !end.IsReadOnly && end.StorageType == StorageType.Double)
        {
            end.Set(seedLength * 0.5);
        }
    }

    private static void AssociateRoundPenetrationExtrusionParameters(Document familyDoc, Extrusion extrusion, RoundPenetrationFamilyParameterMap parameterMap)
    {
        LabelRoundPenetrationDiameterDimension(familyDoc, extrusion, parameterMap.OpeningDiameter);
        AssociateRoundPenetrationElementParameter(
            familyDoc,
            extrusion.get_Parameter(BuiltInParameter.EXTRUSION_START_PARAM),
            parameterMap.ExtrusionStart,
            "round penetration extrusion start");
        AssociateRoundPenetrationElementParameter(
            familyDoc,
            extrusion.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM),
            parameterMap.ExtrusionEnd,
            "round penetration extrusion end");
    }

    private static void AssociateRoundPenetrationElementParameter(Document familyDoc, Parameter? elementParameter, FamilyParameter familyParameter, string parameterLabel)
    {
        if (elementParameter == null)
        {
            throw new InvalidOperationException($"Khong resolve duoc {parameterLabel} parameter de associate vao family parameter.");
        }

        var familyManager = familyDoc.FamilyManager ?? throw new InvalidOperationException("Family document khong co FamilyManager.");
        if (!familyManager.CanElementParameterBeAssociated(elementParameter))
        {
            throw new InvalidOperationException($"{parameterLabel} khong the associate voi family parameter.");
        }

        var associated = familyManager.GetAssociatedFamilyParameter(elementParameter);
        if (associated != null && associated.Id == familyParameter.Id)
        {
            return;
        }

        familyManager.AssociateElementParameterToFamilyParameter(elementParameter, familyParameter);
    }

    private static void LabelRoundPenetrationDiameterDimension(Document familyDoc, Extrusion extrusion, FamilyParameter openingDiameter)
    {
        var profileView = ResolveRoundPenetrationProfileView(familyDoc)
            ?? throw new InvalidOperationException("Khong tim thay family view phu hop de label diameter cho round penetration.");

        var arc = extrusion.Sketch.GetAllElements()
            .Select(familyDoc.GetElement)
            .OfType<CurveElement>()
            .Select(x => x.GeometryCurve as Arc)
            .FirstOrDefault(x => x?.Reference != null);
        if (arc?.Reference == null)
        {
            throw new InvalidOperationException("Khong resolve duoc arc reference cho diameter dimension cua round penetration.");
        }

        var dimension = familyDoc.FamilyCreate.NewDiameterDimension(profileView, arc.Reference, arc.Center);
        if (dimension == null)
        {
            throw new InvalidOperationException("Khong tao duoc diameter dimension cho round penetration.");
        }

        dimension.FamilyLabel = openingDiameter;
    }

    private static void LabelRoundPenetrationLengthDimension(Document familyDoc, Extrusion extrusion, FamilyParameter cutLength)
    {
        var lengthView = ResolveRoundPenetrationLengthView(familyDoc)
            ?? throw new InvalidOperationException("Khong tim thay family view phu hop de label length cho round penetration.");

        if (!TryResolveRoundPenetrationLengthFaces(extrusion, out var startFace, out var endFace))
        {
            throw new InvalidOperationException("Khong resolve duoc extrusion end faces cho round penetration length dimension.");
        }

        var startPoint = EvaluatePlanarFaceMidpoint(startFace);
        var endPoint = EvaluatePlanarFaceMidpoint(endFace);
        var dimensionAxis = SafeNormalize(endPoint - startPoint);
        if (dimensionAxis.IsZeroLength())
        {
            dimensionAxis = XYZ.BasisX;
        }

        var offsetDirection = SafeNormalize(GetViewDirectionOrDefault(lengthView).CrossProduct(dimensionAxis));
        if (offsetDirection.IsZeroLength())
        {
            offsetDirection = SafeNormalize(XYZ.BasisZ.CrossProduct(dimensionAxis));
        }

        if (offsetDirection.IsZeroLength())
        {
            offsetDirection = SafeNormalize(XYZ.BasisY.CrossProduct(dimensionAxis));
        }

        if (offsetDirection.IsZeroLength())
        {
            throw new InvalidOperationException("Khong resolve duoc offset direction cho round penetration length dimension.");
        }

        var offsetDistance = Math.Max(startPoint.DistanceTo(endPoint) * 0.25, 1.0 / 16.0);
        var dimensionLine = Line.CreateBound(
            startPoint + offsetDirection * offsetDistance,
            endPoint + offsetDirection * offsetDistance);

        var references = new ReferenceArray();
        references.Append(startFace.Reference);
        references.Append(endFace.Reference);

        var dimension = familyDoc.FamilyCreate.NewLinearDimension(lengthView, dimensionLine, references);
        if (dimension == null)
        {
            throw new InvalidOperationException("Khong tao duoc length dimension cho round penetration.");
        }

        dimension.FamilyLabel = cutLength;
    }

    private static View? ResolveRoundPenetrationProfileView(Document familyDoc)
    {
        return new FilteredElementCollector(familyDoc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(x => x != null && !x.IsTemplate && x.ViewType != ViewType.ThreeD && x.ViewType != ViewType.Schedule && x.ViewType != ViewType.DrawingSheet)
            .Select(x => new
            {
                View = x,
                Score = Math.Abs(SafeNormalize(GetViewDirectionOrDefault(x)).DotProduct(XYZ.BasisX))
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.View.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.View)
            .FirstOrDefault();
    }

    private static View? ResolveRoundPenetrationLengthView(Document familyDoc)
    {
        return new FilteredElementCollector(familyDoc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(x => x != null && !x.IsTemplate && x.ViewType != ViewType.ThreeD && x.ViewType != ViewType.Schedule && x.ViewType != ViewType.DrawingSheet)
            .Select(x => new
            {
                View = x,
                Score = Math.Abs(SafeNormalize(GetViewDirectionOrDefault(x)).DotProduct(XYZ.BasisX))
            })
            .OrderBy(x => x.Score)
            .ThenBy(x => x.View.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.View)
            .FirstOrDefault();
    }

    private static bool TryResolveRoundPenetrationLengthFaces(Extrusion extrusion, out PlanarFace startFace, out PlanarFace endFace)
    {
        startFace = null!;
        endFace = null!;

        var options = new Options
        {
            ComputeReferences = true,
            DetailLevel = ViewDetailLevel.Fine,
            IncludeNonVisibleObjects = true
        };

        var candidates = extrusion.get_Geometry(options)
            .OfType<Solid>()
            .Where(x => x != null && x.Faces.Size > 0)
            .SelectMany(x => x.Faces.Cast<Face>())
            .OfType<PlanarFace>()
            .Where(x => x.Reference != null)
            .Select(x => new
            {
                Face = x,
                Normal = SafeNormalize(x.FaceNormal),
                Point = EvaluatePlanarFaceMidpoint(x)
            })
            .Where(x => !x.Normal.IsZeroLength() && Math.Abs(Math.Abs(x.Normal.DotProduct(XYZ.BasisX)) - 1.0) <= 1e-6)
            .OrderBy(x => x.Point.X)
            .ToList();

        if (candidates.Count < 2)
        {
            return false;
        }

        startFace = candidates.First().Face;
        endFace = candidates.Last().Face;
        return true;
    }

    private static XYZ EvaluatePlanarFaceMidpoint(PlanarFace face)
    {
        var bounds = face.GetBoundingBox();
        var mid = new UV((bounds.Min.U + bounds.Max.U) * 0.5, (bounds.Min.V + bounds.Max.V) * 0.5);
        return face.Evaluate(mid);
    }

    private static XYZ GetViewDirectionOrDefault(View view)
    {
        try
        {
            return view.ViewDirection;
        }
        catch
        {
            return XYZ.Zero;
        }
    }

    private static void SetFamilyToggle(Parameter? parameter, int expectedValue)
    {
        if (parameter != null && !parameter.IsReadOnly && parameter.StorageType == StorageType.Integer && parameter.AsInteger() != expectedValue)
        {
            parameter.Set(expectedValue);
        }
    }

    private static FamilySymbol? ResolveRoundPenetrationSymbol(Document doc, Family? family, string typeName)
    {
        if (family == null)
        {
            return null;
        }

        return new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(x => x.Family != null && x.Family.Id == family.Id && string.Equals(x.Name ?? string.Empty, typeName, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, FamilySymbol> ResolveRoundPenetrationSymbolsByType(Document doc, Family family, IEnumerable<string> typeNames)
    {
        var requestedTypes = new HashSet<string>(typeNames.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        return new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Where(x => x.Family != null && x.Family.Id == family.Id && requestedTypes.Contains(x.Name ?? string.Empty))
            .ToDictionary(x => x.Name ?? string.Empty, x => x, StringComparer.OrdinalIgnoreCase);
    }

    private static FamilyInstance? CreateRoundPenetrationInstance(Document doc, FamilySymbol symbol, RoundPenetrationPlanItem item, ICollection<DiagnosticRecord> diagnostics)
    {
        FamilyInstance instance;
        var placementType = symbol.Family?.FamilyPlacementType ?? FamilyPlacementType.Invalid;
        var usedHostFacePlacement = false;
        if (placementType == FamilyPlacementType.WorkPlaneBased)
        {
            if (!TryCreateRoundPenetrationWorkPlaneInstance(doc, symbol, item, out instance, out usedHostFacePlacement))
            {
                diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_CREATE_FAILED", DiagnosticSeverity.Error, "Failed to create round penetration family instance for WorkPlaneBased family.", checked((int)item.SourceInstance.Id.Value)));
                return null;
            }
        }
        else
        {
            if (!TryCreateRoundPenetrationPointInstance(doc, symbol, item, out instance))
            {
                diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_CREATE_FAILED", DiagnosticSeverity.Error, $"Failed to create round penetration family instance for placement type {placementType}.", checked((int)item.SourceInstance.Id.Value)));
                return null;
            }
        }

        doc.Regenerate();
        var preCorrectionPlacementState = DescribeRoundPenetrationPlacementState(instance, item.PlacementPoint, usedHostFacePlacement);
        if (ShouldApplyRoundPenetrationPlacementCorrection(placementType, usedHostFacePlacement) && TryGetRoundPenetrationInsertionPoint(instance, out var currentAnchor))
        {
            var correction = item.PlacementPoint - currentAnchor;
            if (correction.GetLength() > 1e-6)
            {
                ElementTransformUtils.MoveElement(doc, instance.Id, correction);
                doc.Regenerate();
            }
        }

        if (!ValidateRoundPenetrationPlacement(instance, item.PlacementPoint, placementType, out var placementNote))
        {
            var postCorrectionPlacementState = DescribeRoundPenetrationPlacementState(instance, item.PlacementPoint, usedHostFacePlacement);
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_PLACEMENT_DRIFT", DiagnosticSeverity.Error, placementNote + " | pre=" + preCorrectionPlacementState + " | post=" + postCorrectionPlacementState, checked((int)item.SourceInstance.Id.Value)));
            return null;
        }

        return instance;
    }

    private static bool TryCreateRoundPenetrationPointInstance(Document doc, FamilySymbol symbol, RoundPenetrationPlanItem item, out FamilyInstance instance)
    {
        try
        {
            instance = doc.Create.NewFamilyInstance(item.PlacementPoint, symbol, StructuralType.NonStructural);
            return instance != null;
        }
        catch
        {
            // fall through
        }

        var level = ResolveAnyLevel(doc);
        if (level != null)
        {
            try
            {
                instance = doc.Create.NewFamilyInstance(item.PlacementPoint, symbol, level, StructuralType.NonStructural);
                return instance != null;
            }
            catch
            {
                // fall through
            }
        }

        instance = null!;
        return false;
    }

    private static bool TryCreateRoundPenetrationWorkPlaneInstance(Document doc, FamilySymbol symbol, RoundPenetrationPlanItem item, out FamilyInstance instance, out bool usedHostFacePlacement)
    {
        instance = null!;
        usedHostFacePlacement = false;
        try
        {
            var placementFrame = BuildRoundPenetrationPlacementFrame(item.SourceBasisX);
            var sketchPlane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(placementFrame.PlaneNormal, item.PlacementPoint));
            instance = doc.Create.NewFamilyInstance(sketchPlane.GetPlaneReference(), item.PlacementPoint, placementFrame.ReferenceDirection, symbol);
            return instance != null;
        }
        catch
        {
            // fallback below
        }

        if (TryResolveRoundPenetrationHostFace(item.HostElement, item.PlacementPoint, item.SourceBasisX, out var face, out var projectedPoint, out var faceReferenceDirection))
        {
            try
            {
                instance = doc.Create.NewFamilyInstance(face, projectedPoint, faceReferenceDirection, symbol);
                usedHostFacePlacement = instance != null;
                return instance != null;
            }
            catch
            {
                // fall through
            }
        }

        instance = null!;
        usedHostFacePlacement = false;
        return false;
    }

    private static bool ShouldApplyRoundPenetrationPlacementCorrection(FamilyPlacementType placementType, bool usedHostFacePlacement)
    {
        return usedHostFacePlacement;
    }

    private static bool ValidateRoundPenetrationPlacement(FamilyInstance instance, XYZ expectedPlacementPoint, FamilyPlacementType placementType, out string note)
    {
        note = string.Empty;
        var placement = EvaluateRoundPenetrationPlacement(instance, expectedPlacementPoint);
        if (placement.IsAligned)
        {
            return true;
        }

        var driftFeet = placement.DriftFeet;
        const double toleranceFeet = 1.0 / 768.0; // ~1/64"
        note = driftFeet <= toleranceFeet
            ? string.Empty
            : $"Round penetration anchor drifted by {driftFeet.ToString("0.######", CultureInfo.InvariantCulture)} ft from the planned cut point.";
        return driftFeet <= toleranceFeet;
    }

    private static bool TryGetRoundPenetrationInsertionPoint(FamilyInstance instance, out XYZ point)
    {
        try
        {
            var transform = instance.GetTransform();
            if (transform != null)
            {
                point = transform.Origin;
                return true;
            }
        }
        catch
        {
            // fall through
        }

        if (instance.Location is LocationPoint locationPoint)
        {
            point = locationPoint.Point;
            return true;
        }

        point = XYZ.Zero;
        return false;
    }

    private static bool TryGetRoundPenetrationGeometryCenter(FamilyInstance instance, out XYZ point)
    {
        var box = instance.get_BoundingBox(null);
        if (box != null)
        {
            point = (box.Min + box.Max) * 0.5;
            return true;
        }

        if (TryGetRoundPenetrationInsertionPoint(instance, out point))
        {
            return true;
        }

        point = XYZ.Zero;
        return false;
    }

    private static string DescribeRoundPenetrationPlacementState(FamilyInstance instance, XYZ plannedPoint, bool usedHostFacePlacement)
    {
        static string FormatPoint(XYZ point)
        {
            return "("
                   + point.X.ToString("0.######", CultureInfo.InvariantCulture) + ","
                   + point.Y.ToString("0.######", CultureInfo.InvariantCulture) + ","
                   + point.Z.ToString("0.######", CultureInfo.InvariantCulture) + ")";
        }

        var parts = new List<string>
        {
            "hostFacePlacement=" + usedHostFacePlacement.ToString(CultureInfo.InvariantCulture),
            "planned=" + FormatPoint(plannedPoint)
        };

        var box = instance.get_BoundingBox(null);
        if (box != null)
        {
            parts.Add("bboxCenter=" + FormatPoint((box.Min + box.Max) * 0.5));
        }

        if (TryGetRoundPenetrationInsertionPoint(instance, out var insertionPoint))
        {
            parts.Add("insertionPoint=" + FormatPoint(insertionPoint));
        }

        if (instance.Location is LocationPoint locationPoint)
        {
            parts.Add("locationPoint=" + FormatPoint(locationPoint.Point));
        }

        try
        {
            var transform = instance.GetTransform();
            if (transform != null)
            {
                parts.Add("transformOrigin=" + FormatPoint(transform.Origin));
            }
        }
        catch
        {
            // ignore transform diagnostics failure
        }

        return string.Join("; ", parts);
    }

    private static Level? ResolveAnyLevel(Document doc)
    {
        return new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(x => x.Elevation).FirstOrDefault();
    }

    private static void SetRoundPenetrationMetadata(FamilyInstance instance, RoundPenetrationPlanItem item, CreateRoundPenetrationCutBatchRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.SetCommentsTrace)
        {
            TrySetParameterValue(instance, new[] { "Comments" }, item.TraceComment, "ROUND_PEN_COMMENTS", diagnostics);
        }

        TrySetParameterValue(instance, new[] { "BIM765T_SourceElementId" }, item.SourceInstance.Id.Value.ToString(CultureInfo.InvariantCulture), "ROUND_PEN_SOURCE_META", diagnostics);
        TrySetParameterValue(instance, new[] { "BIM765T_HostElementId" }, item.HostElement.Id.Value.ToString(CultureInfo.InvariantCulture), "ROUND_PEN_HOST_META", diagnostics);
        TrySetParameterValue(instance, new[] { "BIM765T_HostClass" }, item.HostClass, "ROUND_PEN_HOSTCLASS_META", diagnostics);
        TrySetParameterValue(instance, new[] { "BIM765T_CassetteId" }, item.CassetteId, "ROUND_PEN_CASSETTE_META", diagnostics);
        TrySetParameterValue(instance, new[] { "BIM765T_PlannedPoint" }, SerializeRoundPenetrationPoint(item.PlacementPoint), "ROUND_PEN_PLAN_POINT_META", diagnostics);
        TrySetParameterValue(instance, new[] { "BIM765T_TraceComment" }, item.TraceComment, "ROUND_PEN_TRACE_META", diagnostics);
        TrySetRoundPenetrationYesNoParameter(instance, "BIM765T_ShowReviewBody", request.ShowReviewBodyByDefault);
    }

    private static void TrySetRoundPenetrationYesNoParameter(Element element, string parameterName, bool value)
    {
        var parameter = element.LookupParameter(parameterName);
        if (parameter == null || parameter.IsReadOnly || parameter.StorageType != StorageType.Integer)
        {
            return;
        }

        parameter.Set(value ? 1 : 0);
    }

    private static AxisEvaluationResult AlignRoundPenetrationInstance(Document doc, FamilyInstance instance, RoundPenetrationPlanItem item, double toleranceDegrees, ICollection<DiagnosticRecord> diagnostics)
    {
        doc.Regenerate();
        var alignment = EvaluateRoundPenetrationAxis(instance, item.SourceBasisX, toleranceDegrees);
        if (alignment.IsAligned)
        {
            return alignment;
        }

        var rotationOrigin = ResolveRoundPenetrationRotationOrigin(instance, item.PlacementPoint);
        var placementFrame = BuildRoundPenetrationPlacementFrame(item.SourceBasisX);
        if (TryRotateRoundPenetrationInPlane(doc, instance, rotationOrigin, placementFrame.PlaneNormal, item.SourceBasisX))
        {
            doc.Regenerate();
            alignment = EvaluateRoundPenetrationAxis(instance, item.SourceBasisX, toleranceDegrees);
            if (alignment.IsAligned)
            {
                return alignment;
            }
        }

        var transform = instance.GetTransform();
        var currentBasisX = SafeNormalize(transform.BasisX);
        var targetBasisX = SafeNormalize(item.SourceBasisX);
        var rotationAxis = currentBasisX.CrossProduct(targetBasisX);
        if (rotationAxis.IsZeroLength())
        {
            rotationAxis = BuildPerpendicularAxis(currentBasisX);
        }

        var rotationLine = Line.CreateBound(rotationOrigin, rotationOrigin + rotationAxis.Normalize());
        ElementTransformUtils.RotateElement(doc, instance.Id, rotationLine, currentBasisX.AngleTo(targetBasisX));
        doc.Regenerate();

        var finalAlignment = EvaluateRoundPenetrationAxis(instance, item.SourceBasisX, toleranceDegrees);
        if (!finalAlignment.IsAligned)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_AXIS_REVIEW", DiagnosticSeverity.Warning, finalAlignment.Reason, checked((int)instance.Id.Value)));
        }

        return finalAlignment;
    }

    private static XYZ ResolveRoundPenetrationRotationOrigin(FamilyInstance instance, XYZ fallback)
    {
        if (TryGetRoundPenetrationInsertionPoint(instance, out var insertionPoint))
        {
            return insertionPoint;
        }

        if (TryGetRoundPenetrationGeometryCenter(instance, out var center))
        {
            return center;
        }

        return fallback;
    }

    private static RoundPenetrationPlacementFrame BuildRoundPenetrationPlacementFrame(XYZ targetBasisX)
    {
        var axis = SafeNormalize(targetBasisX);
        var planeNormal = BuildPerpendicularAxis(axis).Normalize();
        var referenceDirection = ProjectVectorOntoPlane(axis, planeNormal);
        if (referenceDirection.IsZeroLength())
        {
            referenceDirection = BuildPerpendicularAxis(planeNormal);
        }

        return new RoundPenetrationPlacementFrame(planeNormal, referenceDirection.Normalize());
    }

    private static bool TryResolveRoundPenetrationHostFace(Element host, XYZ point, XYZ sourceBasisX, out PlanarFace? face, out XYZ projectedPoint, out XYZ referenceDirection)
    {
        face = ResolveRoundPenetrationPlanarFace(host, point, sourceBasisX);
        if (face == null)
        {
            projectedPoint = XYZ.Zero;
            referenceDirection = XYZ.Zero;
            return false;
        }

        projectedPoint = face.Project(point)?.XYZPoint ?? point;
        referenceDirection = ProjectVectorOntoPlane(SafeNormalize(sourceBasisX), face.FaceNormal);
        if (referenceDirection.IsZeroLength())
        {
            referenceDirection = ProjectVectorOntoPlane(XYZ.BasisZ, face.FaceNormal);
        }

        if (referenceDirection.IsZeroLength())
        {
            referenceDirection = ProjectVectorOntoPlane(XYZ.BasisX, face.FaceNormal);
        }

        if (referenceDirection.IsZeroLength())
        {
            referenceDirection = BuildPerpendicularAxis(face.FaceNormal);
        }

        referenceDirection = referenceDirection.Normalize();
        return true;
    }

    private static PlanarFace? ResolveRoundPenetrationPlanarFace(Element host, XYZ point, XYZ sourceBasisX)
    {
        var options = new Options
        {
            ComputeReferences = true,
            DetailLevel = ViewDetailLevel.Fine,
            IncludeNonVisibleObjects = true
        };

        var faces = new List<PlanarFace>();
        CollectRoundPenetrationPlanarFaces(host.get_Geometry(options), faces);
        if (faces.Count == 0)
        {
            return null;
        }

        var targetTangent = SafeNormalize(sourceBasisX);
        return faces
            .Select(candidate =>
            {
                var projected = candidate.Project(point);
                var distance = projected != null ? projected.XYZPoint.DistanceTo(point) : double.MaxValue;
                var tangentPenalty = Math.Abs(candidate.FaceNormal.Normalize().DotProduct(targetTangent));
                return new { Face = candidate, Score = tangentPenalty * 100.0 + distance };
            })
            .OrderBy(x => x.Score)
            .Select(x => x.Face)
            .FirstOrDefault();
    }

    private static void CollectRoundPenetrationPlanarFaces(GeometryElement? geometryElement, IList<PlanarFace> faces)
    {
        if (geometryElement == null)
        {
            return;
        }

        foreach (var item in geometryElement)
        {
            if (item is Solid solid && solid.Faces.Size > 0)
            {
                foreach (Face face in solid.Faces)
                {
                    if (face is PlanarFace planar)
                    {
                        faces.Add(planar);
                    }
                }
            }
            else if (item is GeometryInstance instance)
            {
                CollectRoundPenetrationPlanarFaces(instance.GetInstanceGeometry(), faces);
            }
            else if (item is GeometryElement nested)
            {
                CollectRoundPenetrationPlanarFaces(nested, faces);
            }
        }
    }

    private static bool TryRotateRoundPenetrationInPlane(Document doc, FamilyInstance instance, XYZ origin, XYZ planeNormal, XYZ targetBasisX)
    {
        var transform = instance.GetTransform();
        var currentBasisX = SafeNormalize(transform.BasisX);
        var currentProjected = ProjectVectorOntoPlane(currentBasisX, planeNormal);
        var targetProjected = ProjectVectorOntoPlane(SafeNormalize(targetBasisX), planeNormal);
        if (currentProjected.IsZeroLength() || targetProjected.IsZeroLength())
        {
            return false;
        }

        currentProjected = currentProjected.Normalize();
        targetProjected = targetProjected.Normalize();
        var angle = Math.Atan2(
            planeNormal.Normalize().DotProduct(currentProjected.CrossProduct(targetProjected)),
            Math.Max(-1.0, Math.Min(1.0, currentProjected.DotProduct(targetProjected))));

        if (Math.Abs(angle) <= 1e-9)
        {
            return false;
        }

        var rotationLine = Line.CreateBound(origin, origin + planeNormal.Normalize());
        ElementTransformUtils.RotateElement(doc, instance.Id, rotationLine, angle);
        return true;
    }

    private static XYZ BuildPerpendicularAxis(XYZ axis)
    {
        var candidate = axis.CrossProduct(XYZ.BasisZ);
        if (!candidate.IsZeroLength())
        {
            return candidate;
        }

        candidate = axis.CrossProduct(XYZ.BasisY);
        return !candidate.IsZeroLength() ? candidate : XYZ.BasisX;
    }

    private static AxisEvaluationResult EvaluateRoundPenetrationAxis(FamilyInstance instance, XYZ targetBasisX, double toleranceDegrees)
    {
        try
        {
            var transform = instance.GetTransform();
            var currentBasisX = SafeNormalize(transform.BasisX);
            var target = SafeNormalize(targetBasisX);
            var exactAngle = ComputeAngleDegrees(currentBasisX, target, true);
            if (exactAngle <= toleranceDegrees)
            {
                return new AxisEvaluationResult("ALIGNED_TO_SOURCE_X", $"Local X aligns to source BasisX within {exactAngle.ToString("0.###", CultureInfo.InvariantCulture)} degrees.", true);
            }

            var absoluteAngle = ComputeAngleDegrees(currentBasisX, target, false);
            if (absoluteAngle <= toleranceDegrees)
            {
                return new AxisEvaluationResult("ANTIPARALLEL_TO_SOURCE_X", $"Local X is anti-parallel to source BasisX by {exactAngle.ToString("0.###", CultureInfo.InvariantCulture)} degrees.", false);
            }

            return new AxisEvaluationResult("MISALIGNED_TO_SOURCE_X", $"Local X deviates from source BasisX by {exactAngle.ToString("0.###", CultureInfo.InvariantCulture)} degrees.", false);
        }
        catch (Exception ex)
        {
            return new AxisEvaluationResult("TRANSFORM_UNAVAILABLE", ex.Message, false);
        }
    }

    private static PlacementEvaluationResult EvaluateRoundPenetrationPlacement(FamilyInstance instance, XYZ expectedPlacementPoint)
    {
        if (!TryGetRoundPenetrationInsertionPoint(instance, out var actualPlacementPoint))
        {
            return new PlacementEvaluationResult("ANCHOR_UNAVAILABLE", "Cannot resolve round penetration placement anchor from the family transform.", double.MaxValue, false);
        }

        var driftFeet = actualPlacementPoint.DistanceTo(expectedPlacementPoint);
        const double toleranceFeet = 1.0 / 768.0; // ~1/64"
        if (driftFeet <= toleranceFeet)
        {
            return new PlacementEvaluationResult("PLACED_AT_PLAN_POINT", $"Placement anchor matches planned cut point within {driftFeet.ToString("0.######", CultureInfo.InvariantCulture)} ft.", driftFeet, true);
        }

        return new PlacementEvaluationResult(
            "PLACEMENT_DRIFT",
            $"Placement anchor drifted by {driftFeet.ToString("0.######", CultureInfo.InvariantCulture)} ft from the planned cut point.",
            driftFeet,
            false);
    }

    private static bool TryApplyRoundPenetrationCutWithRetry(Document doc, Element host, FamilyInstance cuttingInstance, int maxRetries, int retryBackoffMs, ICollection<DiagnosticRecord> diagnostics, out string note)
    {
        note = string.Empty;
        if (!InstanceVoidCutUtils.CanBeCutWithVoid(host))
        {
            note = $"Host {host.Id.Value} does not support InstanceVoidCutUtils.";
            return false;
        }

        if (!InstanceVoidCutUtils.IsVoidInstanceCuttingElement(cuttingInstance))
        {
            note = $"Opening family {cuttingInstance.Symbol?.Family?.Name ?? string.Empty} does not expose a cutting void.";
            return false;
        }

        if (InstanceVoidCutUtils.InstanceVoidCutExists(host, cuttingInstance))
        {
            note = "Cut already exists.";
            return true;
        }

        var attempts = Math.Max(1, maxRetries + 1);
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                InstanceVoidCutUtils.AddInstanceVoidCut(doc, host, cuttingInstance);
                doc.Regenerate();
                if (InstanceVoidCutUtils.InstanceVoidCutExists(host, cuttingInstance))
                {
                    note = "Cut applied.";
                    return true;
                }

                note = $"Attempt {attempt}/{attempts}: AddInstanceVoidCut returned but cut relation was not found.";
            }
            catch (Exception ex)
            {
                note = $"Attempt {attempt}/{attempts}: {ex.Message}";
                diagnostics.Add(DiagnosticRecord.Create("ROUND_PEN_CUT_RETRY", attempt < attempts ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error, note, checked((int)cuttingInstance.Id.Value)));
            }

            if (attempt < attempts && retryBackoffMs > 0)
            {
                // Cap backoff to prevent excessive UI freeze during Revit geometry retry.
                Thread.Sleep(Math.Min(retryBackoffMs, 250));
            }
        }

        return false;
    }

    private static ReviewIssue BuildRoundPenetrationIssue(string code, DiagnosticSeverity severity, string message, RoundPenetrationPlanItem item)
    {
        return new ReviewIssue
        {
            Code = code,
            Severity = severity,
            Message = string.IsNullOrWhiteSpace(message) ? item.ResidualNote : message,
            ElementId = checked((int)item.SourceInstance.Id.Value)
        };
    }

    private sealed class RoundPenetrationSettings
    {
        internal string TargetFamilyName { get; set; } = string.Empty;
        internal HashSet<string> SourceElementClasses { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> HostElementClasses { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal List<string> SourceFamilyNameContains { get; set; } = new List<string>();
        internal HashSet<int>? SourceElementIds { get; set; }
        internal double GybClearanceFeet { get; set; }
        internal double WfrClearanceFeet { get; set; }
        internal double AxisToleranceDegrees { get; set; }
        internal string TraceCommentPrefix { get; set; } = string.Empty;
        internal int MaxResults { get; set; }
        internal bool IncludeExisting { get; set; }
    }

    private sealed class RoundPenetrationPlan
    {
        internal List<RoundPenetrationPlanItem> Items { get; } = new List<RoundPenetrationPlanItem>();
        internal List<ExistingRoundPenetrationInstanceInfo> ExistingInstances { get; } = new List<ExistingRoundPenetrationInstanceInfo>();
    }

    private sealed class RoundPenetrationPlanItem
    {
        internal FamilyInstance SourceInstance { get; set; } = null!;
        internal Element HostElement { get; set; } = null!;
        internal string HostClass { get; set; } = string.Empty;
        internal string CassetteId { get; set; } = string.Empty;
        internal XYZ SourceOrigin { get; set; } = XYZ.Zero;
        internal XYZ SourceBasisX { get; set; } = XYZ.Zero;
        internal string NominalOdDisplay { get; set; } = string.Empty;
        internal double NominalOdFeet { get; set; }
        internal double OpeningDiameterFeet { get; set; }
        internal double CutLengthFeet { get; set; }
        internal double ClearancePerSideFeet { get; set; }
        internal XYZ PlacementPoint { get; set; } = XYZ.Zero;
        internal RoundPenetrationFamilySpec FamilySpec { get; set; } = null!;
        internal string TraceComment { get; set; } = string.Empty;
        internal bool CanPlace { get; set; }
        internal bool CanCut { get; set; }
        internal string ResidualNote { get; set; } = string.Empty;
        internal ExistingRoundPenetrationInstanceInfo? ExistingInfo { get; set; }
        internal bool CanCreateNewInstance => ExistingInfo == null && CanPlace && CanCut;
    }

    private sealed class RoundPenetrationPlacementFrame
    {
        internal RoundPenetrationPlacementFrame(XYZ planeNormal, XYZ referenceDirection)
        {
            PlaneNormal = planeNormal;
            ReferenceDirection = referenceDirection;
        }

        internal XYZ PlaneNormal { get; }
        internal XYZ ReferenceDirection { get; }
    }

    private sealed class RoundPenetrationAxisInterval
    {
        internal RoundPenetrationAxisInterval(double start, double end)
        {
            Start = start;
            End = end;
        }

        internal double Start { get; }
        internal double End { get; }
        internal double Length => End - Start;
    }

    private sealed class RoundPenetrationFamilySpec
    {
        internal string FamilyName { get; set; } = string.Empty;
        internal string TypeName { get; set; } = string.Empty;
        internal double NominalOdFeet { get; set; }
        internal double OpeningDiameterFeet { get; set; }
        internal double VoidLengthFeet { get; set; }
        internal string HostClass { get; set; } = string.Empty;
        internal double ClearancePerSideFeet { get; set; }
    }

    private sealed class RoundPenetrationFamilyParameterMap
    {
    internal RoundPenetrationFamilyParameterMap(
        FamilyParameter openingDiameter,
        FamilyParameter cutLength,
        FamilyParameter extrusionStart,
        FamilyParameter extrusionEnd,
        FamilyParameter nominalOd,
        FamilyParameter schemaVersion,
            FamilyParameter clearancePerSide,
            FamilyParameter showReviewBody,
        FamilyParameter sourceElementId,
        FamilyParameter hostElementId,
        FamilyParameter hostClass,
        FamilyParameter cassetteId,
        FamilyParameter plannedPoint,
        FamilyParameter traceComment)
    {
        OpeningDiameter = openingDiameter;
        CutLength = cutLength;
        ExtrusionStart = extrusionStart;
        ExtrusionEnd = extrusionEnd;
        NominalOd = nominalOd;
        SchemaVersion = schemaVersion;
            ClearancePerSide = clearancePerSide;
            ShowReviewBody = showReviewBody;
            SourceElementId = sourceElementId;
            HostElementId = hostElementId;
            HostClass = hostClass;
            CassetteId = cassetteId;
            PlannedPoint = plannedPoint;
            TraceComment = traceComment;
        }

    internal FamilyParameter OpeningDiameter { get; }
    internal FamilyParameter CutLength { get; }
    internal FamilyParameter ExtrusionStart { get; }
    internal FamilyParameter ExtrusionEnd { get; }
        internal FamilyParameter NominalOd { get; }
        internal FamilyParameter SchemaVersion { get; }
        internal FamilyParameter ClearancePerSide { get; }
        internal FamilyParameter ShowReviewBody { get; }
        internal FamilyParameter SourceElementId { get; }
        internal FamilyParameter HostElementId { get; }
        internal FamilyParameter HostClass { get; }
        internal FamilyParameter CassetteId { get; }
        internal FamilyParameter PlannedPoint { get; }
        internal FamilyParameter TraceComment { get; }
    }

    private sealed class ExistingRoundPenetrationInstanceInfo
    {
        internal FamilyInstance Instance { get; set; } = null!;
        internal string TraceComment { get; set; } = string.Empty;
        internal int? SourceElementId { get; set; }
        internal int? HostElementId { get; set; }
        internal XYZ? StoredPlacementPoint { get; set; }
        internal bool CutExists { get; set; }
    }

    private sealed class AxisEvaluationResult
    {
        internal AxisEvaluationResult(string status, string reason, bool isAligned)
        {
            Status = status;
            Reason = reason;
            IsAligned = isAligned;
        }

        internal string Status { get; }
        internal string Reason { get; }
        internal bool IsAligned { get; }
    }

    private sealed class PlacementEvaluationResult
    {
        internal PlacementEvaluationResult(string status, string reason, double driftFeet, bool isAligned)
        {
            Status = status;
            Reason = reason;
            DriftFeet = driftFeet;
            IsAligned = isAligned;
        }

        internal string Status { get; }
        internal string Reason { get; }
        internal double DriftFeet { get; }
        internal bool IsAligned { get; }
    }

    private sealed class RoundPenetrationPairKey : IEquatable<RoundPenetrationPairKey>
    {
        internal RoundPenetrationPairKey(int sourceElementId, int hostElementId)
        {
            SourceElementId = sourceElementId;
            HostElementId = hostElementId;
        }

        internal int SourceElementId { get; }
        internal int HostElementId { get; }

        public bool Equals(RoundPenetrationPairKey? other)
        {
            return other != null && other.SourceElementId == SourceElementId && other.HostElementId == HostElementId;
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as RoundPenetrationPairKey);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (SourceElementId * 397) ^ HostElementId;
            }
        }
    }
}
