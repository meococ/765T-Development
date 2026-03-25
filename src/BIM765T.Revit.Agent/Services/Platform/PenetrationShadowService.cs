using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure.Failures;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed partial class PenetrationShadowService
{
    internal PenetrationInventoryResponse ReportInventory(UIApplication uiapp, PlatformServices services, Document doc, PenetrationInventoryRequest? request)
    {
        request ??= new PenetrationInventoryRequest();
        var instances = CollectPenetrationInstances(uiapp, doc, request).Take(Math.Max(1, request.MaxResults)).ToList();

        var response = new PenetrationInventoryResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            FamilyName = request.FamilyName
        };

        foreach (var instance in instances)
        {
            var item = BuildInventoryItem(instance, request.IncludeAxisStatus);
            response.Items.Add(item);
        }

        response.Count = response.Items.Count;
        response.Groups = response.Items
            .GroupBy(x => new { x.TypeName, x.MiiDiameter, x.MiiDimLength, x.MiiElementClass, x.MiiElementTier })
            .OrderBy(x => x.Key.TypeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Key.MiiDiameter, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Key.MiiDimLength, StringComparer.OrdinalIgnoreCase)
            .Select(x => new PenetrationInventoryGroupDto
            {
                TypeName = x.Key.TypeName,
                MiiDiameter = x.Key.MiiDiameter,
                MiiDimLength = x.Key.MiiDimLength,
                MiiElementClass = x.Key.MiiElementClass,
                MiiElementTier = x.Key.MiiElementTier,
                Count = x.Count()
            })
            .ToList();

        response.Review = new ReviewReport
        {
            Name = "penetration_alpha_inventory",
            DocumentKey = services.GetDocumentKey(doc),
            ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
            IssueCount = 0
        };
        return response;
    }

    internal PenetrationRoundShadowPlanResponse PlanRoundShadow(UIApplication uiapp, PlatformServices services, Document doc, PenetrationRoundShadowPlanRequest? request)
    {
        request ??= new PenetrationRoundShadowPlanRequest();
        var plan = ResolveRoundShadowPlan(doc, new CreateRoundShadowBatchRequest
        {
            DocumentKey = request.DocumentKey,
            SourceFamilyName = request.SourceFamilyName,
            RoundFamilyName = request.RoundFamilyName,
            PreferredReferenceMark = request.PreferredReferenceMark,
            MaxResults = request.MaxResults,
            SkipIfTraceExists = false,
            SetCommentsTrace = false
        });

        var response = new PenetrationRoundShadowPlanResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            SourceFamilyName = request.SourceFamilyName,
            RoundFamilyName = request.RoundFamilyName
        };

        foreach (var item in plan.Items)
        {
            response.Items.Add(new PenetrationRoundShadowPlanItemDto
            {
                SourceElementId = checked((int)item.SourceInstance.Id.Value),
                SourceTypeName = item.SourceInstance.Symbol?.Name ?? string.Empty,
                MiiDiameter = item.MiiDiameter,
                MiiDimLength = item.MiiDimLength,
                MiiElementClass = item.MiiElementClass,
                MiiElementTier = item.MiiElementTier,
                RoundSymbolId = plan.PreferredSymbol != null ? checked((int)plan.PreferredSymbol.Id.Value) : 0,
                RoundTypeName = plan.PreferredSymbol?.Name ?? string.Empty,
                ReferenceRoundElementId = plan.ReferenceInstance != null ? checked((int)plan.ReferenceInstance.Id.Value) : (int?)null,
                ReferenceAxisStatus = plan.ReferenceInstance != null ? "ALIGNED" : string.Empty,
                CanCreateShadow = item.CanCreate,
                Notes = item.Notes
            });
        }

        response.Count = response.Items.Count;
        response.CreatableCount = response.Items.Count(x => x.CanCreateShadow);
        response.Review = new ReviewReport
        {
            Name = "penetration_round_shadow_plan",
            DocumentKey = services.GetDocumentKey(doc),
            ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
            IssueCount = response.Items.Count(x => !x.CanCreateShadow),
            Issues = response.Items
                .Where(x => !x.CanCreateShadow)
                .Select(x => new ReviewIssue
                {
                    Code = "ROUND_SHADOW_NOT_CREATABLE",
                    Severity = DiagnosticSeverity.Warning,
                    Message = x.Notes,
                    ElementId = x.SourceElementId
                })
                .ToList()
        };
        return response;
    }

    internal RoundExternalizationPlanResponse PlanRoundExternalization(PlatformServices services, Document doc, RoundExternalizationPlanRequest? request)
    {
        request ??= new RoundExternalizationPlanRequest();
        var maxResults = Math.Max(1, request.MaxResults);
        var allRounds = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(x => string.Equals(x.Symbol?.Family?.Name ?? string.Empty, request.RoundFamilyName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Id.Value)
            .ToList();

        var rounds = allRounds.Take(maxResults).ToList();
        var response = new RoundExternalizationPlanResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            DocumentTitle = doc.Title ?? string.Empty,
            ParentFamilyName = request.ParentFamilyName,
            RoundFamilyName = request.RoundFamilyName,
            TotalRoundInstances = allRounds.Count,
            Truncated = allRounds.Count > maxResults
        };

        foreach (var round in rounds)
        {
            response.Items.Add(BuildRoundExternalizationPlanItem(round, request));
        }

        response.Count = response.Items.Count;
        response.EligibleCount = response.Items.Count(x => x.CanExternalize);
        response.MissingParentCount = response.Items.Count(x => !x.ParentElementId.HasValue);
        response.UnexpectedParentCount = response.Items.Count(x =>
            x.ParentElementId.HasValue &&
            !string.IsNullOrWhiteSpace(x.ParentFamilyName) &&
            !string.Equals(x.ParentFamilyName, request.ParentFamilyName, StringComparison.OrdinalIgnoreCase));
        response.MissingTransformCount = response.Items.Count(x => string.Equals(x.RoundStatus, "TRANSFORM_UNAVAILABLE", StringComparison.OrdinalIgnoreCase));
        response.UniqueParentInstanceCount = response.Items
            .Where(x => x.ParentElementId.HasValue)
            .Select(x => x.ParentElementId!.Value)
            .Distinct()
            .Count();

        response.ModeSummary = response.Items
            .Where(x => !string.IsNullOrWhiteSpace(x.ProposedPlacementMode))
            .GroupBy(x => new { x.ProposedPlacementMode, x.ProposedTargetFamilyName, x.ProposedTargetTypeName })
            .OrderBy(x => x.Key.ProposedPlacementMode, StringComparer.OrdinalIgnoreCase)
            .Select(x => new RoundExternalizationModeSummaryDto
            {
                ProposedPlacementMode = x.Key.ProposedPlacementMode,
                ProposedTargetFamilyName = x.Key.ProposedTargetFamilyName,
                ProposedTargetTypeName = x.Key.ProposedTargetTypeName,
                Count = x.Count()
            })
            .ToList();

        response.TypeSummary = response.Items
            .Where(x => !string.IsNullOrWhiteSpace(x.ParentTypeName) && !string.IsNullOrWhiteSpace(x.ProposedPlacementMode))
            .GroupBy(x => new
            {
                x.ParentFamilyName,
                x.ParentTypeName,
                x.ProposedPlacementMode,
                x.ProposedTargetFamilyName,
                x.ProposedTargetTypeName
            })
            .OrderBy(x => x.Key.ParentFamilyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Key.ParentTypeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Key.ProposedPlacementMode, StringComparer.OrdinalIgnoreCase)
            .Select(x => new RoundExternalizationTypeSummaryDto
            {
                ParentFamilyName = x.Key.ParentFamilyName,
                ParentTypeName = x.Key.ParentTypeName,
                ProposedPlacementMode = x.Key.ProposedPlacementMode,
                ProposedTargetFamilyName = x.Key.ProposedTargetFamilyName,
                ProposedTargetTypeName = x.Key.ProposedTargetTypeName,
                Count = x.Count()
            })
            .ToList();

        var reviewIssues = new List<ReviewIssue>();
        if (response.Truncated)
        {
            reviewIssues.Add(new ReviewIssue
            {
                Code = "ROUND_EXTERNALIZATION_TRUNCATED",
                Severity = DiagnosticSeverity.Info,
                Message = $"Round externalization plan bi cat bot o MaxResults={maxResults}. TotalRoundInstances={response.TotalRoundInstances}."
            });
        }

        reviewIssues.AddRange(response.Items
            .Where(x => !x.CanExternalize)
            .Take(200)
            .Select(x => new ReviewIssue
            {
                Code = "ROUND_EXTERNALIZATION_NOT_ELIGIBLE",
                Severity = DiagnosticSeverity.Warning,
                Message = x.Notes,
                ElementId = x.RoundElementId
            }));

        response.Review = new ReviewReport
        {
            Name = "round_externalization_plan",
            DocumentKey = services.GetDocumentKey(doc),
            ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
            IssueCount = reviewIssues.Count,
            Issues = reviewIssues
        };

        return response;
    }

    internal ExecutionResult PreviewBuildRoundProjectWrappers(PlatformServices services, Document doc, BuildRoundProjectWrappersRequest request, ToolRequestEnvelope envelope)
    {
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        var plan = ResolveRoundWrapperBuildPlan(services, doc, request);

        var diagnostics = new List<DiagnosticRecord>
        {
            DiagnosticRecord.Create("ROUND_WRAPPER_SOURCE", DiagnosticSeverity.Info, $"Source family = {plan.SourceFamily.Name} ({plan.SourceFamily.Id.Value}), placementType={plan.SourceFamily.FamilyPlacementType}.", checked((int)plan.SourceFamily.Id.Value)),
            DiagnosticRecord.Create("ROUND_WRAPPER_OUTPUT", DiagnosticSeverity.Info, $"OutputDirectory = {plan.OutputDirectory}.")
        };

        diagnostics.AddRange(plan.Specs
            .GroupBy(spec => spec.FamilyName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(spec =>
            DiagnosticRecord.Create(
                spec.ExistingProjectFamily != null ? "ROUND_WRAPPER_WILL_RELOAD" : "ROUND_WRAPPER_WILL_LOAD",
                spec.ExistingProjectFamily != null ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
                spec.ExistingProjectFamily != null
                    ? $"Family {spec.FamilyName} da ton tai trong project va se duoc reload/overwrite neu execute."
                    : $"Family {spec.FamilyName} se duoc build tai {spec.FilePath} va load vao project.",
                spec.ExistingProjectFamily != null ? checked((int)spec.ExistingProjectFamily.Id.Value) : 0)));

        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = plan.Specs
                .GroupBy(x => x.FamilyName, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .Where(x => x.ExistingProjectFamily != null)
                .Select(x => checked((int)x.ExistingProjectFamily!.Id.Value))
                .Distinct()
                .ToList(),
            Diagnostics = diagnostics,
            Artifacts = new List<string>(
                new[]
                {
                    "outputDirectory=" + plan.OutputDirectory,
                    "sourceFamilyName=" + plan.SourceFamily.Name,
                    "sourcePlacementType=" + plan.SourceFamily.FamilyPlacementType
                }.Concat(plan.Specs.Select(x => $"wrapper={x.FamilyName}/{x.TypeName}|file={x.FilePath}|mode={x.PlacementMode}|length={x.SizeLengthValueString}|diameter={x.SizeDiameterValueString}")))
        };
    }

    internal ExecutionResult ExecuteBuildRoundProjectWrappers(PlatformServices services, Document doc, BuildRoundProjectWrappersRequest request)
    {
        var plan = ResolveRoundWrapperBuildPlan(services, doc, request);
        var diagnostics = new List<DiagnosticRecord>();
        var beforeWarnings = doc.GetWarnings().Count;
        var changedIds = new List<int>();
        var artifacts = new List<string>
        {
            "outputDirectory=" + plan.OutputDirectory
        };

        Directory.CreateDirectory(plan.OutputDirectory);
        artifacts.Add("sourceFamilyName=" + plan.SourceFamily.Name);

        foreach (var specGroup in plan.Specs
                     .GroupBy(x => x.FamilyName, StringComparer.OrdinalIgnoreCase)
                     .Select(x => x.OrderBy(spec => spec.TypeName, StringComparer.OrdinalIgnoreCase).ToList()))
        {
            var loadedFamily = BuildWrapperFamilyFile(
                doc,
                plan.SourceFamily,
                specGroup,
                request.OverwriteFamilyFiles,
                request.LoadIntoProject,
                request.OverwriteExistingProjectFamilies,
                diagnostics);
            artifacts.Add("wrapperFile=" + specGroup[0].FilePath);

            if (!request.LoadIntoProject)
            {
                continue;
            }

            if (loadedFamily == null)
            {
                throw new InvalidOperationException($"Khong load duoc family wrapper {specGroup[0].FamilyName} tu file {specGroup[0].FilePath}.");
            }

            changedIds.Add(checked((int)loadedFamily.Id.Value));
            foreach (var spec in specGroup)
            {
                artifacts.Add($"loadedFamily={loadedFamily.Name}|id={loadedFamily.Id.Value}|type={spec.TypeName}|mode={spec.PlacementMode}|length={spec.SizeLengthValueString}|diameter={spec.SizeDiameterValueString}");
            }
        }

        var diff = new DiffSummary
        {
            CreatedIds = changedIds.Distinct().ToList(),
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };

        var review = services.BuildExecutionReview("round_wrapper_build_review", diff);
        review.Issues.AddRange(diagnostics
            .Where(x => x.Severity != DiagnosticSeverity.Info)
            .Select(x => new ReviewIssue
            {
                Code = x.Code,
                Severity = x.Severity,
                Message = x.Message,
                ElementId = x.SourceId
            }));
        review.IssueCount = review.Issues.Count;

        return new ExecutionResult
        {
            OperationName = ToolNames.FamilyBuildRoundProjectWrappersSafe,
            DryRun = false,
            ChangedIds = diff.CreatedIds,
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = artifacts,
            ReviewSummary = review
        };
    }

    internal ExecutionResult PreviewSyncPenetrationAlphaNestedTypes(UIApplication uiapp, PlatformServices services, Document doc, SyncPenetrationAlphaNestedTypesRequest request, ToolRequestEnvelope envelope)
    {
        EnsurePenetrationParentFamilyDocument(doc, request);

        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        var diagnostics = new List<DiagnosticRecord>();
        NestedPenetrationTypeSyncPlan plan;

        using (var transaction = new Transaction(doc, "Preview sync nested Penetration Alpha types"))
        {
            transaction.Start();
            AgentFailureHandling.Configure(transaction, diagnostics);
            plan = AnalyzeNestedPenetrationTypeSync(uiapp, services, doc, request, diagnostics);
            transaction.RollBack();
        }

        diagnostics.Insert(0, DiagnosticRecord.Create("PEN_SYNC_PREVIEW_SCOPE", DiagnosticSeverity.Info, $"Parent family doc = {doc.Title}, nested family = {request.NestedFamilyName}."));
        diagnostics.Add(!string.IsNullOrWhiteSpace(plan.TypeControlParameterName)
            ? DiagnosticRecord.Create(
                plan.TypeControlParameterExists ? "PEN_SYNC_TYPE_CONTROL_PARAMETER_FOUND" : "PEN_SYNC_TYPE_CONTROL_PARAMETER_WILL_CREATE",
                plan.TypeControlParameterExists ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
                $"Type control parameter = {plan.TypeControlParameterName}.")
            : DiagnosticRecord.Create("PEN_SYNC_TYPE_CONTROL_PARAMETER_UNKNOWN", DiagnosticSeverity.Warning, "Chua resolve duoc type control parameter cho nested family."));

        if (request.ReloadIntoProject)
        {
            diagnostics.Add(plan.ProjectDocument != null
                ? DiagnosticRecord.Create("PEN_SYNC_PROJECT_TARGET", DiagnosticSeverity.Info, $"Reload target project = {plan.ProjectDocument.Title}.")
                : DiagnosticRecord.Create("PEN_SYNC_PROJECT_TARGET_MISSING", DiagnosticSeverity.Warning, "Khong resolve duoc project document dang mo de reload family sau execute."));
        }

        diagnostics.AddRange(plan.Items
            .Where(x => x.RequiresCreate)
            .Take(40)
            .Select(x => DiagnosticRecord.Create(
                "PEN_SYNC_TYPE_WILL_CREATE",
                DiagnosticSeverity.Warning,
                $"Parent type '{x.ParentType.Name}' chua co nested type '{x.TargetTypeName}'. Se duplicate tu '{x.SeedSymbol?.Name ?? "<unknown>"}'.",
                x.ReferenceNestedInstanceId)));

        diagnostics.AddRange(plan.Items
            .Where(x => !x.RequiresCreate && x.RequiresAssign)
            .Take(40)
            .Select(x => DiagnosticRecord.Create(
                "PEN_SYNC_TYPE_WILL_ASSIGN",
                DiagnosticSeverity.Info,
                $"Parent type '{x.ParentType.Name}' se doi nested type tu '{x.CurrentChildSymbol?.Name ?? "<none>"}' sang '{x.TargetTypeName}'.",
                x.ReferenceNestedInstanceId)));

        var changedIds = plan.NestedInstanceIds
            .Concat(plan.Items.Where(x => x.ExistingTargetSymbol != null).Select(x => checked((int)x.ExistingTargetSymbol!.Id.Value)))
            .Distinct()
            .ToList();

        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = changedIds,
            DiffSummary = new DiffSummary
            {
                CreatedIds = new List<int>(),
                ModifiedIds = plan.NestedInstanceIds.Distinct().ToList()
            },
            Diagnostics = diagnostics,
            Artifacts = BuildNestedTypeSyncArtifacts(services, doc, plan, request)
        };
    }

    internal ExecutionResult ExecuteSyncPenetrationAlphaNestedTypes(UIApplication uiapp, PlatformServices services, Document doc, SyncPenetrationAlphaNestedTypesRequest request)
    {
        EnsurePenetrationParentFamilyDocument(doc, request);

        var diagnostics = new List<DiagnosticRecord>();
        var createdIds = new List<int>();
        var modifiedIds = new List<int>();
        var changedIds = new List<int>();
        var beforeWarnings = doc.GetWarnings().Count;
        NestedPenetrationTypeSyncPlan plan = null!;

        using (var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::family.sync_penetration_alpha_nested_types_safe"))
        {
            group.Start();
            using (var transaction = new Transaction(doc, "Sync nested Penetration Alpha types"))
            {
                transaction.Start();
                AgentFailureHandling.Configure(transaction, diagnostics);

                plan = AnalyzeNestedPenetrationTypeSync(uiapp, services, doc, request, diagnostics);
                var childSymbolMap = CollectNestedFamilySymbols(doc, request.NestedFamilyName)
                    .GroupBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .ToDictionary(x => x.Name ?? string.Empty, x => x, StringComparer.OrdinalIgnoreCase);

                foreach (var item in plan.Items.Where(x => x.RequiresCreate))
                {
                    if (childSymbolMap.ContainsKey(item.TargetTypeName))
                    {
                        continue;
                    }

                    var seedSymbol = item.SeedSymbol ?? plan.PreferredSeedSymbol;
                    if (seedSymbol == null)
                    {
                        throw new InvalidOperationException($"Khong co seed type de tao nested type '{item.TargetTypeName}'.");
                    }

                    var duplicated = seedSymbol.Duplicate(item.TargetTypeName) as FamilySymbol;
                    if (duplicated == null)
                    {
                        throw new InvalidOperationException($"Duplicate nested type '{item.TargetTypeName}' tra ve null.");
                    }

                    childSymbolMap[item.TargetTypeName] = duplicated;
                    createdIds.Add(checked((int)duplicated.Id.Value));
                    changedIds.Add(checked((int)duplicated.Id.Value));
                    diagnostics.Add(DiagnosticRecord.Create("PEN_SYNC_TYPE_CREATED", DiagnosticSeverity.Info, $"Da tao nested type '{duplicated.Name}' tu seed '{seedSymbol.Name}'.", checked((int)duplicated.Id.Value)));
                }

                doc.Regenerate();

                var familyManager = plan.FamilyManager;
                var referenceNestedInstance = doc.GetElement(new ElementId((long)plan.NestedInstanceIds[0])) as FamilyInstance
                                            ?? throw new InvalidOperationException("Khong resolve duoc nested instance tham chieu sau khi tao type.");
                var nestedTypeElementParameter = ResolveNestedTypeElementParameter(familyManager, referenceNestedInstance, request.NestedFamilyName);
                var controlFamilyParameter = EnsureNestedTypeControlFamilyParameter(
                    familyManager,
                    referenceNestedInstance,
                    nestedTypeElementParameter,
                    request.NestedFamilyName,
                    diagnostics);
                plan.TypeControlParameterName = controlFamilyParameter.Definition?.Name ?? string.Empty;

                foreach (var item in plan.Items)
                {
                    if (!childSymbolMap.TryGetValue(item.TargetTypeName, out var targetSymbol))
                    {
                        throw new InvalidOperationException($"Khong resolve duoc nested type target '{item.TargetTypeName}'.");
                    }

                    plan.FamilyManager.CurrentType = item.ParentType;
                    doc.Regenerate();

                    familyManager.Set(controlFamilyParameter, targetSymbol.Id);
                    modifiedIds.Add(plan.NestedInstanceIds[0]);
                    changedIds.Add(plan.NestedInstanceIds[0]);
                    diagnostics.Add(DiagnosticRecord.Create("PEN_SYNC_TYPE_ASSIGNED", DiagnosticSeverity.Info, $"Parent type '{item.ParentType.Name}' da map nested family type parameter -> '{targetSymbol.Name}'.", plan.NestedInstanceIds[0]));
                    doc.Regenerate();
                }

                if (plan.OriginalCurrentType != null)
                {
                    plan.FamilyManager.CurrentType = plan.OriginalCurrentType;
                    doc.Regenerate();
                }

                transaction.Commit();
            }

            group.Assimilate();
        }

        Family? reloadedFamily = null;
        if (request.ReloadIntoProject)
        {
            if (plan.ProjectDocument == null)
            {
                throw new InvalidOperationException("Khong tim thay project document dang mo de reload family Penetration Alpha M.");
            }

            reloadedFamily = doc.LoadFamily(plan.ProjectDocument, new AlwaysOverwriteFamilyLoadOptions(request.OverwriteExistingProjectFamily));
            if (reloadedFamily == null)
            {
                throw new InvalidOperationException($"LoadFamily ve project {plan.ProjectDocument.Title} tra ve null.");
            }

            changedIds.Add(checked((int)reloadedFamily.Id.Value));
            modifiedIds.Add(checked((int)reloadedFamily.Id.Value));
            diagnostics.Add(DiagnosticRecord.Create("PEN_SYNC_FAMILY_RELOADED", DiagnosticSeverity.Info, $"Da reload family {reloadedFamily.Name} vao project {plan.ProjectDocument.Title}.", checked((int)reloadedFamily.Id.Value)));
        }

        var diff = new DiffSummary
        {
            CreatedIds = createdIds.Distinct().ToList(),
            ModifiedIds = modifiedIds.Distinct().ToList(),
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };

        var artifacts = BuildNestedTypeSyncArtifacts(services, doc, plan, request);
        artifacts.Add("createdNestedTypeCount=" + diff.CreatedIds.Count.ToString(CultureInfo.InvariantCulture));
        artifacts.Add("modifiedElementCount=" + diff.ModifiedIds.Count.ToString(CultureInfo.InvariantCulture));
        if (reloadedFamily != null)
        {
            artifacts.Add("reloadedProjectFamilyId=" + reloadedFamily.Id.Value.ToString(CultureInfo.InvariantCulture));
        }

        var review = services.BuildExecutionReview("penetration_alpha_nested_type_sync_review", diff);
        review.Issues.AddRange(diagnostics
            .Where(x => x.Severity != DiagnosticSeverity.Info)
            .Select(x => new ReviewIssue
            {
                Code = x.Code,
                Severity = x.Severity,
                Message = x.Message,
                ElementId = x.SourceId
            }));
        review.IssueCount = review.Issues.Count;

        return new ExecutionResult
        {
            OperationName = ToolNames.FamilySyncPenetrationAlphaNestedTypesSafe,
            DryRun = false,
            ChangedIds = changedIds.Distinct().ToList(),
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = artifacts,
            ReviewSummary = review
        };
    }

    internal RoundShadowCleanupPlanResponse ReportRoundShadowCleanupPlan(PlatformServices services, Document doc, RoundShadowCleanupRequest? request)
    {
        request ??= new RoundShadowCleanupRequest();
        var cleanupPlan = ResolveCleanupPlan(services, doc, request);
        var response = new RoundShadowCleanupPlanResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            TraceCommentPrefix = request.TraceCommentPrefix,
            JournalId = cleanupPlan.JournalId
        };

        foreach (var item in cleanupPlan.Items)
        {
            response.Items.Add(new RoundShadowCleanupItemDto
            {
                ElementId = item.ElementId,
                FamilyName = item.FamilyName,
                TypeName = item.TypeName,
                Comments = item.Comments,
                SourceElementId = item.SourceElementId,
                TraceMatched = item.TraceMatched,
                CanDelete = item.CanDelete,
                EstimatedDependentCount = item.EstimatedDependentCount,
                Notes = item.Notes
            });
        }

        response.Count = response.Items.Count;
        response.DeletableCount = response.Items.Count(x => x.CanDelete);
        response.Review = new ReviewReport
        {
            Name = "round_shadow_cleanup_plan",
            DocumentKey = services.GetDocumentKey(doc),
            ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
            IssueCount = response.Items.Count(x => !x.CanDelete),
            Issues = response.Items
                .Where(x => !x.CanDelete)
                .Select(x => new ReviewIssue
                {
                    Code = "ROUND_SHADOW_NOT_DELETABLE",
                    Severity = DiagnosticSeverity.Warning,
                    Message = x.Notes,
                    ElementId = x.ElementId
                })
                .ToList()
        };
        return response;
    }

    internal ExecutionResult PreviewCreateInventorySchedule(PlatformServices services, Document doc, CreatePenetrationInventoryScheduleRequest request, ToolRequestEnvelope envelope)
    {
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        var existing = FindScheduleByName(doc, request.ScheduleName);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("SCHEDULE_PREVIEW", DiagnosticSeverity.Info, existing != null ? "Schedule đã tồn tại và sẽ được cập nhật/tạo lại." : "Schedule sẽ được tạo mới."),
                DiagnosticRecord.Create("SCHEDULE_FAMILY_SCOPE", DiagnosticSeverity.Info, "Family scope: " + request.FamilyName)
            },
            Artifacts = new List<string>
            {
                "scheduleName=" + request.ScheduleName,
                "familyName=" + request.FamilyName
            }
        };
    }

    internal ExecutionResult ExecuteCreateInventorySchedule(PlatformServices services, Document doc, CreatePenetrationInventoryScheduleRequest request)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var createdIds = new List<int>();
        var beforeWarnings = doc.GetWarnings().Count;

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::schedule.create_penetration_alpha_inventory_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Create penetration inventory schedule");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        var existing = FindScheduleByName(doc, request.ScheduleName);
        if (existing != null && request.OverwriteIfExists)
        {
            doc.Delete(existing.Id);
        }

        var schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_GenericModel));
        schedule.Name = request.ScheduleName;
        var definition = schedule.Definition;
        definition.IsItemized = request.Itemized;

        var addedFields = new Dictionary<string, ScheduleField>(StringComparer.OrdinalIgnoreCase);
        AddFieldIfAvailable(doc, definition, addedFields, "Family");
        AddFieldIfAvailable(doc, definition, addedFields, "Type");
        AddFieldIfAvailable(doc, definition, addedFields, "Count");
        AddFieldIfAvailable(doc, definition, addedFields, "Level");
        AddFieldIfAvailable(doc, definition, addedFields, "Mark");
        AddFieldIfAvailable(doc, definition, addedFields, "Mii_Diameter");
        AddFieldIfAvailable(doc, definition, addedFields, "Mii_DimLength");
        AddFieldIfAvailable(doc, definition, addedFields, "Mii_ElementClass");
        AddFieldIfAvailable(doc, definition, addedFields, "Mii_ElementTier");
        AddFieldIfAvailable(doc, definition, addedFields, "Comments");

        if (!TryAddFamilyFilter(definition, addedFields, request.FamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("SCHEDULE_FILTER_NOT_ADDED", DiagnosticSeverity.Warning, "Không tìm được Family/Family and Type field để add filter."));
        }

        AddSortIfFieldExists(definition, addedFields, "Type");
        AddSortIfFieldExists(definition, addedFields, "Mii_Diameter");
        AddSortIfFieldExists(definition, addedFields, "Mii_DimLength");

        transaction.Commit();
        group.Assimilate();

        createdIds.Add(checked((int)schedule.Id.Value));
        var diff = new DiffSummary
        {
            CreatedIds = createdIds,
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };

        return new ExecutionResult
        {
            OperationName = "schedule.create_penetration_alpha_inventory_safe",
            DryRun = false,
            ChangedIds = createdIds,
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = new List<string> { "scheduleName=" + request.ScheduleName },
            ReviewSummary = services.BuildExecutionReview("schedule_create_review", diff)
        };
    }

    // ── Round Inventory Schedule ─────────────────────────────────────────────

    internal ExecutionResult PreviewCreateRoundInventorySchedule(PlatformServices services, Document doc, CreateRoundInventoryScheduleRequest request, ToolRequestEnvelope envelope)
    {
        // Đếm Round instances sẽ xuất hiện trong schedule để AI biết trước scope
        var roundCount = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Count(x => string.Equals(x.Symbol?.Family?.Name ?? string.Empty, request.FamilyName, StringComparison.OrdinalIgnoreCase));

        var existingSchedule = FindScheduleByName(doc, request.ScheduleName);
        var willOverwrite = existingSchedule != null && request.OverwriteIfExists;
        var willFail = existingSchedule != null && !request.OverwriteIfExists;

        var warnings = new List<string>();
        if (willOverwrite)
        {
            warnings.Add($"Schedule '{request.ScheduleName}' đã tồn tại — sẽ được ghi đè.");
        }

        if (willFail)
        {
            warnings.Add($"Schedule '{request.ScheduleName}' đã tồn tại và OverwriteIfExists=false — thao tác sẽ thất bại.");
        }

        if (roundCount == 0)
        {
            warnings.Add($"Không tìm thấy FamilyInstance nào với Family.Name = '{request.FamilyName}'. Schedule sẽ rỗng.");
        }

        var diagnostics = warnings
            .Select(w => DiagnosticRecord.Create("ROUND_SCHEDULE_PREVIEW", DiagnosticSeverity.Warning, w))
            .ToList();
        var token = services.Approval.IssueToken(
            envelope.ToolName,
            services.Approval.BuildFingerprint(envelope),
            services.GetDocumentKey(doc),
            envelope.Caller,
            envelope.SessionId);

        return new ExecutionResult
        {
            OperationName = ToolNames.ScheduleCreateRoundInventorySafe,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                $"FamilyName={request.FamilyName}",
                $"ScheduleName={request.ScheduleName}",
                $"RoundInstanceCount={roundCount}",
                $"WillOverwrite={willOverwrite}",
                $"IncludeLinkedFiles={request.IncludeLinkedFiles}"
            }
        };
    }

    internal ExecutionResult ExecuteCreateRoundInventorySchedule(PlatformServices services, Document doc, CreateRoundInventoryScheduleRequest request)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var createdIds = new List<int>();
        var beforeWarnings = doc.GetWarnings().Count;

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::schedule.create_round_inventory_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Create Round inventory schedule");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        var existing = FindScheduleByName(doc, request.ScheduleName);
        if (existing != null && request.OverwriteIfExists)
        {
            doc.Delete(existing.Id);
        }
        else if (existing != null)
        {
            throw new InvalidOperationException($"Schedule '{request.ScheduleName}' đã tồn tại và OverwriteIfExists=false.");
        }

        var schedule = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_GenericModel));
        schedule.Name = request.ScheduleName;
        var definition = schedule.Definition;
        definition.IsItemized = request.Itemized;
        definition.IncludeLinkedFiles = request.IncludeLinkedFiles;

        // Columns: Family, Type, Count, Level, Mark + Mii_* cho Round domain
        var addedFields = new Dictionary<string, ScheduleField>(StringComparer.OrdinalIgnoreCase);
        AddFieldIfAvailable(doc, definition, addedFields, "Family");
        AddFieldIfAvailable(doc, definition, addedFields, "Type");
        AddFieldIfAvailable(doc, definition, addedFields, "Count");
        AddFieldIfAvailable(doc, definition, addedFields, "Level");
        AddFieldIfAvailable(doc, definition, addedFields, "Mark");
        AddFieldIfAvailable(doc, definition, addedFields, "Mii_Diameter");
        AddFieldIfAvailable(doc, definition, addedFields, "Mii_DimLength");
        AddFieldIfAvailable(doc, definition, addedFields, "Mii_ElementClass");
        AddFieldIfAvailable(doc, definition, addedFields, "Mii_ElementTier");
        AddFieldIfAvailable(doc, definition, addedFields, "Comments");

        // Filter theo Family.Name == request.FamilyName (case-insensitive match qua ScheduleFilterType.Contains)
        if (!TryAddFamilyFilter(definition, addedFields, request.FamilyName))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_SCHEDULE_FILTER_WARN", DiagnosticSeverity.Warning,
                $"Không thêm được filter Family='{request.FamilyName}'. Schedule sẽ show tất cả Generic Model."));
        }

        // Sort: Type → Mii_Diameter → Mii_DimLength (nhất quán với Penetration Alpha schedule)
        AddSortIfFieldExists(definition, addedFields, "Type");
        AddSortIfFieldExists(definition, addedFields, "Mii_Diameter");
        AddSortIfFieldExists(definition, addedFields, "Mii_DimLength");

        transaction.Commit();
        group.Assimilate();

        createdIds.Add(checked((int)schedule.Id.Value));
        var diff = new DiffSummary
        {
            CreatedIds = createdIds,
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };

        return new ExecutionResult
        {
            OperationName = ToolNames.ScheduleCreateRoundInventorySafe,
            DryRun = false,
            ChangedIds = createdIds,
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                $"scheduleName={request.ScheduleName}",
                $"familyFilter={request.FamilyName}",
                $"fieldCount={addedFields.Count}"
            },
            ReviewSummary = services.BuildExecutionReview("schedule_create_review", diff)
        };
    }

    internal ExecutionResult PreviewCreateRoundShadowBatch(PlatformServices services, Document doc, CreateRoundShadowBatchRequest request, ToolRequestEnvelope envelope)
    {
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        var plan = ResolveRoundShadowPlan(doc, request);
        var creatable = plan.Items.Where(x => x.CanCreate).ToList();
        var skippedExisting = plan.Items.Count(x => x.ExistingShadowElementId.HasValue);
        var missingSizeData = plan.Items.Count(x => string.IsNullOrWhiteSpace(x.MiiDiameter) || string.IsNullOrWhiteSpace(x.MiiDimLength));

        var diagnostics = new List<DiagnosticRecord>
        {
            DiagnosticRecord.Create("ROUND_SHADOW_PREVIEW", DiagnosticSeverity.Info, $"Sources={plan.Items.Count}, creatable={creatable.Count}, skippedExisting={skippedExisting}, missingSizeData={missingSizeData}."),
            DiagnosticRecord.Create("ROUND_SHADOW_REFERENCE", plan.ReferenceInstance != null ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning, plan.ReferenceInstance != null ? $"Reference Round = {plan.ReferenceInstance.Id.Value} ({plan.ReferenceInstance.Symbol?.Name ?? string.Empty})." : "No aligned Round reference instance found; workflow will proceed without copy primitive."),
            DiagnosticRecord.Create("ROUND_SHADOW_SYMBOL", plan.PreferredSymbol != null ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning, plan.PreferredSymbol != null ? $"Create symbol = {plan.PreferredSymbol.Id.Value} ({plan.PreferredSymbol.Name})." : "No Round symbol found for shadow creation.")
        };

        diagnostics.AddRange(plan.Items
            .Where(x => !x.CanCreate)
            .Take(50)
            .Select(x => DiagnosticRecord.Create("ROUND_SHADOW_SKIP", DiagnosticSeverity.Warning, x.Notes, checked((int)x.SourceInstance.Id.Value))));

        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = creatable.Select(x => checked((int)x.SourceInstance.Id.Value)).ToList(),
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                "sourceCount=" + plan.Items.Count.ToString(CultureInfo.InvariantCulture),
                "creatableCount=" + creatable.Count.ToString(CultureInfo.InvariantCulture),
                "skippedExisting=" + skippedExisting.ToString(CultureInfo.InvariantCulture),
                "referenceRoundElementId=" + (plan.ReferenceInstance != null ? plan.ReferenceInstance.Id.Value.ToString(CultureInfo.InvariantCulture) : string.Empty),
                "roundSymbolId=" + (plan.PreferredSymbol != null ? plan.PreferredSymbol.Id.Value.ToString(CultureInfo.InvariantCulture) : string.Empty)
            }
        };
    }

    internal ExecutionResult PreviewCleanupRoundShadow(PlatformServices services, Document doc, RoundShadowCleanupRequest request, ToolRequestEnvelope envelope)
    {
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        var cleanupPlan = ResolveCleanupPlan(services, doc, request);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = cleanupPlan.Items.Where(x => x.CanDelete).Select(x => x.ElementId).ToList(),
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("ROUND_SHADOW_CLEANUP_PREVIEW", DiagnosticSeverity.Info, $"Candidates={cleanupPlan.Items.Count}, deletable={cleanupPlan.Items.Count(x => x.CanDelete)}, journal={cleanupPlan.JournalId}."),
                DiagnosticRecord.Create("ROUND_SHADOW_CLEANUP_SCOPE", DiagnosticSeverity.Info, string.IsNullOrWhiteSpace(cleanupPlan.JournalId) ? "Cleanup scope resolved from explicit element ids." : "Cleanup scope resolved from operation journal.")
            }
            .Concat(cleanupPlan.Items.Where(x => !x.CanDelete).Take(50).Select(x => DiagnosticRecord.Create("ROUND_SHADOW_CLEANUP_SKIP", DiagnosticSeverity.Warning, x.Notes, x.ElementId)))
            .ToList(),
            Artifacts = new List<string>
            {
                "journalId=" + cleanupPlan.JournalId,
                "candidateCount=" + cleanupPlan.Items.Count.ToString(CultureInfo.InvariantCulture),
                "deletableCount=" + cleanupPlan.Items.Count(x => x.CanDelete).ToString(CultureInfo.InvariantCulture)
            }
        };
    }

    internal ExecutionResult ExecuteCreateRoundShadowBatch(PlatformServices services, Document doc, CreateRoundShadowBatchRequest request)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var artifacts = new List<string>();
        var createdIds = new List<int>();
        var reviewIssues = new List<ReviewIssue>();
        var beforeWarnings = doc.GetWarnings().Count;
        var plan = ResolveRoundShadowPlan(doc, request);
        var creatable = plan.Items.Where(x => x.CanCreate).ToList();

        if (plan.PreferredSymbol == null || creatable.Count == 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_NO_WORK", DiagnosticSeverity.Warning, "No Round shadow items are eligible for creation."));
            diagnostics.AddRange(plan.Items.Where(x => !x.CanCreate).Take(100).Select(x => DiagnosticRecord.Create("ROUND_SHADOW_SKIP", DiagnosticSeverity.Warning, x.Notes, checked((int)x.SourceInstance.Id.Value))));
            return new ExecutionResult
            {
                OperationName = ToolNames.BatchCreateRoundShadowSafe,
                DryRun = false,
                Diagnostics = diagnostics,
                ReviewSummary = new ReviewReport
                {
                    Name = "penetration_round_shadow_create_review",
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

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::batch.create_round_shadow_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Create Round shadow batch safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        if (!plan.PreferredSymbol.IsActive)
        {
            plan.PreferredSymbol.Activate();
            doc.Regenerate();
        }

        foreach (var item in plan.Items.Where(x => !x.CanCreate))
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_SKIP", DiagnosticSeverity.Warning, item.Notes, checked((int)item.SourceInstance.Id.Value)));
        }

        foreach (var item in creatable)
        {
            using var subTransaction = new SubTransaction(doc);
            subTransaction.Start();
            try
            {
                var newInstance = CreateRoundShadowInstance(doc, plan.PreferredSymbol, item, request, diagnostics);

                if (newInstance == null)
                {
                    subTransaction.RollBack();
                    continue;
                }

                ApplyShadowParameters(newInstance, item, request, diagnostics);
                doc.Regenerate();

                if (TryGetAnchorPoint(newInstance, out var finalAnchor))
                {
                    var correction = item.TargetAnchor - finalAnchor;
                    if (correction.GetLength() > 1e-6)
                    {
                        ElementTransformUtils.MoveElement(doc, newInstance.Id, correction);
                        doc.Regenerate();
                    }
                }

                if (!string.Equals(newInstance.Symbol?.Family?.Name ?? string.Empty, request.RoundFamilyName, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_FAMILY_MISMATCH", DiagnosticSeverity.Error, $"Created family is `{newInstance.Symbol?.Family?.Name ?? string.Empty}`, expected `{request.RoundFamilyName}`.", checked((int)newInstance.Id.Value)));
                    subTransaction.RollBack();
                    continue;
                }

                var dependentCount = newInstance.GetDependentElements(null).Count;
                if (dependentCount > 25)
                {
                    diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_DEPENDENT_GRAPH_BLOCKED", DiagnosticSeverity.Error, $"Created instance spawned too many dependents ({dependentCount}). Rolled back to avoid cassette/dependency spam.", checked((int)newInstance.Id.Value)));
                    subTransaction.RollBack();
                    continue;
                }

                var axis = ClassifyAxis(newInstance, 5.0);
                if (request.RequireAxisAlignedResult && !axis.Status.Equals("ALIGNED", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_AXIS_REJECTED", DiagnosticSeverity.Warning, $"Rolled back source {item.SourceInstance.Id.Value} because created Round axis status = {axis.Status}. {axis.Reason}", checked((int)item.SourceInstance.Id.Value)));
                    subTransaction.RollBack();
                    continue;
                }

                subTransaction.Commit();
                createdIds.Add(checked((int)newInstance.Id.Value));
                artifacts.Add($"shadowPair:source={item.SourceInstance.Id.Value};shadow={newInstance.Id.Value};axis={axis.Status};type={newInstance.Symbol?.Name ?? string.Empty}");
                diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_CREATED", axis.Status.Equals("ALIGNED", StringComparison.OrdinalIgnoreCase) ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning, $"Created Round shadow {newInstance.Id.Value} for source {item.SourceInstance.Id.Value} with axis status = {axis.Status}.", checked((int)newInstance.Id.Value)));

                if (!axis.Status.Equals("ALIGNED", StringComparison.OrdinalIgnoreCase))
                {
                    reviewIssues.Add(new ReviewIssue
                    {
                        Code = axis.Status,
                        Severity = DiagnosticSeverity.Warning,
                        Message = axis.Reason,
                        ElementId = checked((int)newInstance.Id.Value)
                    });
                }
            }
            catch (Exception ex)
            {
                try
                {
                    subTransaction.RollBack();
                }
                catch
                {
                    // ignore rollback failure
                }
                diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_CREATE_FAILED", DiagnosticSeverity.Error, ex.Message, checked((int)item.SourceInstance.Id.Value)));
            }
        }

        transaction.Commit();
        group.Assimilate();

        var diff = new DiffSummary
        {
            CreatedIds = createdIds,
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };

        return new ExecutionResult
        {
            OperationName = ToolNames.BatchCreateRoundShadowSafe,
            DryRun = false,
            ChangedIds = createdIds,
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = artifacts,
            ReviewSummary = new ReviewReport
            {
                Name = "penetration_round_shadow_create_review",
                DocumentKey = services.GetDocumentKey(doc),
                ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
                IssueCount = reviewIssues.Count,
                Issues = reviewIssues
            }
        };
    }

    internal ExecutionResult ExecuteCleanupRoundShadow(PlatformServices services, Document doc, RoundShadowCleanupRequest request)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var cleanupPlan = ResolveCleanupPlan(services, doc, request);
        var deletable = cleanupPlan.Items.Where(x => x.CanDelete).ToList();
        if (deletable.Count == 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_CLEANUP_NO_WORK", DiagnosticSeverity.Warning, "Không có Round shadow nào đủ điều kiện để cleanup."));
            diagnostics.AddRange(cleanupPlan.Items.Where(x => !x.CanDelete).Take(100).Select(x => DiagnosticRecord.Create("ROUND_SHADOW_CLEANUP_SKIP", DiagnosticSeverity.Warning, x.Notes, x.ElementId)));
            return new ExecutionResult
            {
                OperationName = ToolNames.CleanupRoundShadowByRunSafe,
                DryRun = false,
                Diagnostics = diagnostics,
                ReviewSummary = new ReviewReport
                {
                    Name = "round_shadow_cleanup_review",
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

        var beforeWarnings = doc.GetWarnings().Count;
        var deletedRootIds = deletable.Select(x => x.ElementId).Distinct().ToList();
        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::cleanup.round_shadow_by_run_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Cleanup Round shadow batch");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        var deletedIds = doc.Delete(deletedRootIds.Select(x => new ElementId((long)x)).ToList())
            .Select(x => checked((int)x.Value))
            .Distinct()
            .ToList();

        transaction.Commit();
        group.Assimilate();

        var diff = new DiffSummary
        {
            DeletedIds = deletedIds,
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };

        return new ExecutionResult
        {
            OperationName = ToolNames.CleanupRoundShadowByRunSafe,
            DryRun = false,
            ChangedIds = deletedRootIds,
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = new List<string> { "journalId=" + cleanupPlan.JournalId, "deletedRootCount=" + deletedRootIds.Count.ToString(CultureInfo.InvariantCulture) },
            ReviewSummary = services.BuildExecutionReview("round_shadow_cleanup_review", diff)
        };
    }

    private static List<FamilyInstance> CollectPenetrationInstances(UIApplication uiapp, Document doc, PenetrationInventoryRequest request)
    {
        IEnumerable<FamilyInstance> instances;
        if (request.ViewId.HasValue)
        {
            instances = new FilteredElementCollector(doc, new ElementId((long)request.ViewId.Value))
                .OfClass(typeof(FamilyInstance))
                .Cast<FamilyInstance>();
        }
        else if (!string.IsNullOrWhiteSpace(request.ViewName))
        {
            var view = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .FirstOrDefault(x => string.Equals(x.Name ?? string.Empty, request.ViewName, StringComparison.OrdinalIgnoreCase));
            instances = view != null
                ? new FilteredElementCollector(doc, view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>()
                : new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>();
        }
        else
        {
            instances = new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>();
        }

        return instances
            .Where(x => string.Equals(x.Symbol?.Family?.Name ?? string.Empty, request.FamilyName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Symbol?.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Id.Value)
            .ToList();
    }

    private static PenetrationInventoryItemDto BuildInventoryItem(FamilyInstance instance, bool includeAxisStatus)
    {
        var axis = includeAxisStatus ? ClassifyAxis(instance, 5.0) : new AxisClassification("SKIPPED", string.Empty);
        return new PenetrationInventoryItemDto
        {
            ElementId = checked((int)instance.Id.Value),
            UniqueId = instance.UniqueId ?? string.Empty,
            FamilyName = instance.Symbol?.Family?.Name ?? string.Empty,
            TypeName = instance.Symbol?.Name ?? string.Empty,
            LevelName = instance.LevelId != ElementId.InvalidElementId ? (instance.Document.GetElement(instance.LevelId) as Level)?.Name ?? string.Empty : string.Empty,
            HostElementId = instance.Host != null ? checked((int)instance.Host.Id.Value) : (int?)null,
            HostCategoryName = instance.Host?.Category?.Name ?? string.Empty,
            MiiDiameter = ReadFirstNonEmptyParameter(instance, "Mii_Diameter", "Mii_DimDiameter"),
            MiiDimLength = ReadFirstNonEmptyParameter(instance, "Mii_DimLength", "Length"),
            MiiElementClass = ReadParameterValue(instance, "Mii_ElementClass"),
            MiiElementTier = ReadParameterValue(instance, "Mii_ElementTier"),
            Mark = ReadParameterValue(instance, "Mark"),
            AxisStatus = axis.Status,
            AxisReason = axis.Reason
        };
    }

    private static RoundExternalizationPlanItemDto BuildRoundExternalizationPlanItem(FamilyInstance round, RoundExternalizationPlanRequest request)
    {
        var item = new RoundExternalizationPlanItemDto
        {
            RoundElementId = checked((int)round.Id.Value),
            RoundUniqueId = round.UniqueId ?? string.Empty,
            RoundFamilyName = round.Symbol?.Family?.Name ?? string.Empty,
            RoundTypeName = round.Symbol?.Name ?? string.Empty,
            RoundPlacementType = (round.Symbol?.Family?.FamilyPlacementType ?? FamilyPlacementType.Invalid).ToString(),
            Mirrored = round.Mirrored,
            CanExternalize = true,
            Notes = "Ready for externalization."
        };

        var axis = ClassifyAxis(round, request.AngleToleranceDegrees > 0 ? request.AngleToleranceDegrees : 5.0);
        item.RoundStatus = axis.Status;
        item.RoundReason = axis.Reason;

        var superComponent = round.SuperComponent;
        var parentInstance = superComponent as FamilyInstance;
        if (superComponent != null)
        {
            item.ParentElementId = checked((int)superComponent.Id.Value);
            item.ParentCategoryName = superComponent.Category?.Name ?? string.Empty;
            item.ParentFamilyName = parentInstance?.Symbol?.Family?.Name ?? string.Empty;
            item.ParentTypeName = parentInstance?.Symbol?.Name ?? superComponent.Name ?? string.Empty;
            item.ParentMiiDiameter = ReadFirstNonEmptyParameter(superComponent, "Mii_Diameter", "Mii_DimDiameter");
            item.ParentMiiDimLength = ReadFirstNonEmptyParameter(superComponent, "Mii_DimLength", "Length");
            item.ParentMiiElementClass = ReadParameterValue(superComponent, "Mii_ElementClass");
            item.ParentMiiElementTier = ReadParameterValue(superComponent, "Mii_ElementTier");
            item.ParentMark = ReadParameterValue(superComponent, "Mark");
        }

        if (superComponent == null)
        {
            item.CanExternalize = false;
            item.Notes = "Round khong co SuperComponent; can review tay truoc khi externalize.";
        }
        else if (request.RequireParentFamilyMatch &&
                 !string.Equals(item.ParentFamilyName, request.ParentFamilyName, StringComparison.OrdinalIgnoreCase))
        {
            item.CanExternalize = false;
            item.Notes = $"SuperComponent {item.ParentElementId} khong thuoc family `{request.ParentFamilyName}`.";
        }

        if (!TryPopulateRoundTransform(round, item))
        {
            item.CanExternalize = false;
            item.RoundStatus = "TRANSFORM_UNAVAILABLE";
            item.RoundReason = "Khong resolve duoc transform global cua Round.";
            item.Notes = string.Equals(item.Notes, "Ready for externalization.", StringComparison.Ordinal)
                ? "Khong resolve duoc transform global cua Round."
                : item.Notes + " Khong resolve duoc transform global cua Round.";
            return item;
        }

        item.ProposedPlacementMode = DetermineExternalizationMode(item.BasisX, item.BasisY, item.BasisZ);
        var targetGeometryAxis = DetermineTargetGeometryAxis(item.BasisX);
        item.PlacementNote = BuildExternalizationPlacementNote(item.ProposedPlacementMode, targetGeometryAxis);
        ResolveExternalizationTarget(request, targetGeometryAxis, out var targetFamilyName, out var targetTypeName);
        item.ProposedTargetFamilyName = targetFamilyName;
        item.ProposedTargetTypeName = targetTypeName;
        item.SuggestedTraceComment = BuildExternalizationTraceComment(request.TraceCommentPrefix, item);

        return item;
    }

    private static RoundShadowPlan ResolveRoundShadowPlan(Document doc, CreateRoundShadowBatchRequest request)
    {
        var penetrationRequest = new PenetrationInventoryRequest
        {
            DocumentKey = request.DocumentKey,
            FamilyName = request.SourceFamilyName,
            MaxResults = request.MaxResults,
            IncludeAxisStatus = false
        };

        var penetrations = CollectPenetrationInstances(null!, doc, penetrationRequest)
            .Take(Math.Max(1, request.MaxResults))
            .ToList();

        if (request.SourceElementIds != null && request.SourceElementIds.Count > 0)
        {
            var sourceIds = new HashSet<int>(request.SourceElementIds);
            penetrations = penetrations.Where(x => sourceIds.Contains(checked((int)x.Id.Value))).ToList();
        }

        var roundSymbols = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Where(x => string.Equals(x.Family?.Name ?? string.Empty, request.RoundFamilyName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var roundInstances = new FilteredElementCollector(doc)
            .OfClass(typeof(FamilyInstance))
            .Cast<FamilyInstance>()
            .Where(x => string.Equals(x.Symbol?.Family?.Name ?? string.Empty, request.RoundFamilyName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var reference = ResolveReferenceRound(doc, roundInstances, request);
        var preferredSymbol = ResolvePreferredRoundSymbol(doc, roundSymbols, reference, request);
        var existingShadows = request.SkipIfTraceExists
            ? FindExistingShadowMap(roundInstances, request.TraceCommentPrefix)
            : new Dictionary<int, int>();

        var plan = new RoundShadowPlan
        {
            PreferredSymbol = preferredSymbol,
            ReferenceInstance = reference,
            ReferenceAnchor = XYZ.Zero
        };

        if (reference != null && TryGetAnchorPoint(reference, out var referenceAnchor))
        {
            plan.ReferenceAnchor = referenceAnchor;
        }

        foreach (var penetration in penetrations)
        {
            var item = new RoundShadowPlanItem
            {
                SourceInstance = penetration,
                MiiDiameter = ReadFirstNonEmptyParameter(penetration, "Mii_Diameter", "Mii_DimDiameter"),
                MiiDimLength = ReadFirstNonEmptyParameter(penetration, "Mii_DimLength", "Length"),
                MiiElementClass = ReadParameterValue(penetration, "Mii_ElementClass"),
                MiiElementTier = ReadParameterValue(penetration, "Mii_ElementTier"),
                ExistingShadowElementId = existingShadows.TryGetValue(checked((int)penetration.Id.Value), out var existingId) ? existingId : (int?)null,
                CanCreate = preferredSymbol != null,
                Notes = "Ready for shadow placement.",
                TargetAnchor = XYZ.Zero
            };

            if (!item.CanCreate)
            {
                item.Notes = "Round family symbol not found in project.";
            }
            else if (item.ExistingShadowElementId.HasValue)
            {
                item.CanCreate = false;
                item.Notes = $"Shadow already exists: {item.ExistingShadowElementId.Value}.";
            }
            else if (preferredSymbol != null && RequiresHost(preferredSymbol) && penetration.Host == null)
            {
                item.CanCreate = false;
                item.Notes = "Round symbol requires host/face placement but source Penetration Alpha has no host.";
            }
            else if (!TryGetAnchorPoint(penetration, out var targetAnchor))
            {
                item.CanCreate = false;
                item.Notes = "Cannot resolve target anchor point from Penetration Alpha.";
            }
            else
            {
                item.TargetAnchor = targetAnchor;
            }

            plan.Items.Add(item);
        }

        return plan;
    }

    private static FamilyInstance? ResolveReferenceRound(Document doc, IEnumerable<FamilyInstance> roundInstances, CreateRoundShadowBatchRequest request)
    {
        if (request.ReferenceRoundElementId.HasValue)
        {
            var explicitReference = doc.GetElement(new ElementId((long)request.ReferenceRoundElementId.Value)) as FamilyInstance;
            if (explicitReference != null && string.Equals(explicitReference.Symbol?.Family?.Name ?? string.Empty, request.RoundFamilyName, StringComparison.OrdinalIgnoreCase))
            {
                return explicitReference;
            }
        }

        return roundInstances
            .Where(x => string.Equals(ReadParameterValue(x, "Mark"), request.PreferredReferenceMark, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(x => IsAlignedToProject(x, 5.0))
            ?? roundInstances.FirstOrDefault(x => IsAlignedToProject(x, 5.0));
    }

    private static FamilySymbol? ResolvePreferredRoundSymbol(Document doc, IEnumerable<FamilySymbol> roundSymbols, FamilyInstance? reference, CreateRoundShadowBatchRequest request)
    {
        if (request.RoundSymbolId.HasValue)
        {
            var explicitSymbol = doc.GetElement(new ElementId((long)request.RoundSymbolId.Value)) as FamilySymbol;
            if (explicitSymbol != null && string.Equals(explicitSymbol.Family?.Name ?? string.Empty, request.RoundFamilyName, StringComparison.OrdinalIgnoreCase))
            {
                return explicitSymbol;
            }
        }

        if (reference?.Symbol != null)
        {
            return reference.Symbol;
        }

        return roundSymbols.FirstOrDefault(x => string.Equals(x.Name ?? string.Empty, "Round", StringComparison.OrdinalIgnoreCase))
            ?? roundSymbols.FirstOrDefault();
    }

    private static bool RequiresHost(FamilySymbol symbol)
    {
        var placementType = symbol.Family?.FamilyPlacementType ?? FamilyPlacementType.Invalid;
        return placementType == FamilyPlacementType.WorkPlaneBased
            || placementType == FamilyPlacementType.OneLevelBasedHosted
            || placementType == FamilyPlacementType.CurveBased
            || placementType == FamilyPlacementType.CurveDrivenStructural;
    }

    private static FamilyInstance? CreateRoundShadowInstance(Document doc, FamilySymbol symbol, RoundShadowPlanItem item, CreateRoundShadowBatchRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        var placementType = symbol.Family?.FamilyPlacementType ?? FamilyPlacementType.Invalid;
        switch (placementType)
        {
            case FamilyPlacementType.OneLevelBased:
            case FamilyPlacementType.TwoLevelsBased:
            {
                var level = ResolvePlacementLevel(doc, item.SourceInstance);
                if (level == null)
                {
                    diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_LEVEL_MISSING", DiagnosticSeverity.Error, "Không resolve được level cho Round shadow placement.", checked((int)item.SourceInstance.Id.Value)));
                    return null;
                }

                return doc.Create.NewFamilyInstance(item.TargetAnchor, symbol, level, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
            }

            case FamilyPlacementType.OneLevelBasedHosted:
            {
                var host = item.SourceInstance.Host;
                if (host == null)
                {
                    diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_HOST_MISSING", DiagnosticSeverity.Error, "Source Penetration Alpha không có host cho hosted placement.", checked((int)item.SourceInstance.Id.Value)));
                    return null;
                }

                var refDir = BuildHostedReferenceDirection(host, item.TargetAnchor);
                return doc.Create.NewFamilyInstance(item.TargetAnchor, symbol, refDir, host, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
            }

            case FamilyPlacementType.WorkPlaneBased:
            {
                var host = item.SourceInstance.Host;
                if (host == null)
                {
                    diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_HOST_MISSING", DiagnosticSeverity.Error, "Source Penetration Alpha không có host cho work-plane-based placement.", checked((int)item.SourceInstance.Id.Value)));
                    return null;
                }

                var face = ResolvePlanarFace(host, item.TargetAnchor, null);
                if (face == null)
                {
                    diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_FACE_MISSING", DiagnosticSeverity.Error, "Không resolve được planar face trên host để place Round shadow.", checked((int)item.SourceInstance.Id.Value)));
                    return null;
                }

                var projected = face.Project(item.TargetAnchor);
                var placementPoint = projected?.XYZPoint ?? item.TargetAnchor;
                var referenceDirection = BuildProjectAlignedReferenceDirection(face);
                return doc.Create.NewFamilyInstance(face, placementPoint, referenceDirection, symbol);
            }

            default:
                diagnostics.Add(DiagnosticRecord.Create("ROUND_SHADOW_PLACEMENT_UNSUPPORTED", DiagnosticSeverity.Error, $"Family placement type chưa support cho Round shadow: {placementType}.", checked((int)item.SourceInstance.Id.Value)));
                return null;
        }
    }

    private static Dictionary<int, int> FindExistingShadowMap(IEnumerable<FamilyInstance> roundInstances, string tracePrefix)
    {
        var map = new Dictionary<int, int>();
        if (string.IsNullOrWhiteSpace(tracePrefix))
        {
            return map;
        }

        foreach (var instance in roundInstances)
        {
            var comments = ReadParameterValue(instance, "Comments");
            if (string.IsNullOrWhiteSpace(comments) || !comments.StartsWith(tracePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sourceId = TryParseSourceIdFromTrace(comments);
            if (sourceId.HasValue && !map.ContainsKey(sourceId.Value))
            {
                map[sourceId.Value] = checked((int)instance.Id.Value);
            }
        }

        return map;
    }

    private static RoundShadowCleanupPlan ResolveCleanupPlan(PlatformServices services, Document doc, RoundShadowCleanupRequest request)
    {
        var elementIds = new List<int>();
        var journalId = request.JournalId ?? string.Empty;

        if (request.ElementIds != null && request.ElementIds.Count > 0)
        {
            elementIds.AddRange(request.ElementIds);
        }
        else
        {
            var recent = services.Journal.GetRecent()
                .Where(x => string.Equals(x.ToolName, ToolNames.BatchCreateRoundShadowSafe, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.Equals(x.StatusCode, StatusCodes.ExecuteSucceeded, StringComparison.OrdinalIgnoreCase))
                .Where(x => string.IsNullOrWhiteSpace(request.DocumentKey) || string.Equals(x.DocumentKey ?? string.Empty, services.GetDocumentKey(doc), StringComparison.OrdinalIgnoreCase))
                .ToList();

            OperationJournalEntry? operation = null;
            if (!string.IsNullOrWhiteSpace(request.JournalId))
            {
                operation = recent.FirstOrDefault(x => string.Equals(x.JournalId ?? string.Empty, request.JournalId, StringComparison.OrdinalIgnoreCase));
            }

            if (operation == null && request.UseLatestSuccessfulBatchWhenEmpty)
            {
                operation = recent.OrderByDescending(x => x.EndedUtc).FirstOrDefault();
            }

            if (operation != null)
            {
                journalId = operation.JournalId ?? string.Empty;
                elementIds.AddRange(operation.ChangedIds ?? new List<int>());
            }
        }

        var plan = new RoundShadowCleanupPlan
        {
            JournalId = journalId
        };

        foreach (var id in elementIds.Distinct().Take(Math.Max(1, request.MaxResults)))
        {
            var element = doc.GetElement(new ElementId((long)id));
            var instance = element as FamilyInstance;
            var comments = instance != null ? ReadParameterValue(instance, "Comments") : string.Empty;
            var traceMatched = !string.IsNullOrWhiteSpace(request.TraceCommentPrefix) && !string.IsNullOrWhiteSpace(comments) && comments.StartsWith(request.TraceCommentPrefix, StringComparison.OrdinalIgnoreCase);
            var canDelete = instance != null && (!request.RequireTraceCommentMatch || traceMatched);
            var dependentCount = element != null ? SafeDependentCount(element) : 0;

            plan.Items.Add(new RoundShadowCleanupPlanItem
            {
                ElementId = id,
                FamilyName = instance?.Symbol?.Family?.Name ?? string.Empty,
                TypeName = instance?.Symbol?.Name ?? string.Empty,
                Comments = comments,
                SourceElementId = TryParseSourceIdFromTrace(comments),
                TraceMatched = traceMatched,
                CanDelete = canDelete,
                EstimatedDependentCount = dependentCount,
                Notes = element == null
                    ? "Element không còn tồn tại."
                    : instance == null
                        ? "Element không phải FamilyInstance."
                        : !canDelete
                            ? "Trace comment không match prefix an toàn."
                            : "Ready for cleanup."
            });
        }

        return plan;
    }

    private static int SafeDependentCount(Element element)
    {
        try
        {
            return element.GetDependentElements(null).Count;
        }
        catch
        {
            return 0;
        }
    }

    private static Level? ResolvePlacementLevel(Document doc, FamilyInstance source)
    {
        if (source.LevelId != null && source.LevelId != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(source.LevelId) as Level;
            if (level != null)
            {
                return level;
            }
        }

        var scheduleLevel = source.get_Parameter(BuiltInParameter.INSTANCE_SCHEDULE_ONLY_LEVEL_PARAM);
        if (scheduleLevel != null && scheduleLevel.StorageType == StorageType.ElementId)
        {
            var level = doc.GetElement(scheduleLevel.AsElementId()) as Level;
            if (level != null)
            {
                return level;
            }
        }

        return doc.ActiveView?.GenLevel;
    }

    private static XYZ BuildHostedReferenceDirection(Element host, XYZ point)
    {
        if (host != null)
        {
            var face = ResolvePlanarFace(host, point, null);
            if (face != null)
            {
                return BuildProjectAlignedReferenceDirection(face);
            }
        }

        return XYZ.BasisZ;
    }

    private static XYZ BuildProjectAlignedReferenceDirection(PlanarFace face)
    {
        var projectedZ = ProjectVectorOntoPlane(XYZ.BasisZ, face.FaceNormal);
        if (IsNonZero(projectedZ))
        {
            return projectedZ.Normalize();
        }

        var projectedX = ProjectVectorOntoPlane(XYZ.BasisX, face.FaceNormal);
        if (IsNonZero(projectedX))
        {
            return projectedX.Normalize();
        }

        if (IsNonZero(face.XVector))
        {
            return face.XVector.Normalize();
        }

        var projectedY = ProjectVectorOntoPlane(XYZ.BasisY, face.FaceNormal);
        return IsNonZero(projectedY) ? projectedY.Normalize() : XYZ.BasisX;
    }

    private static XYZ ProjectVectorOntoPlane(XYZ vector, XYZ planeNormal)
    {
        var normal = planeNormal.Normalize();
        return vector - normal.Multiply(vector.DotProduct(normal));
    }

    private static bool IsNonZero(XYZ? vector)
    {
        return vector != null && vector.GetLength() > 1e-9;
    }

    private static PlanarFace? ResolvePlanarFace(Element host, XYZ point, XYZ? requestedNormal)
    {
        var options = new Options
        {
            ComputeReferences = true,
            DetailLevel = ViewDetailLevel.Fine,
            IncludeNonVisibleObjects = true
        };

        var faces = new List<PlanarFace>();
        CollectPlanarFaces(host.get_Geometry(options), faces);
        if (faces.Count == 0)
        {
            return null;
        }

        var targetNormal = requestedNormal != null && IsNonZero(requestedNormal) ? requestedNormal.Normalize() : null;
        return faces.Select(face =>
            {
                var projected = face.Project(point);
                var distance = projected != null ? projected.XYZPoint.DistanceTo(point) : double.MaxValue;
                var normalPenalty = targetNormal == null ? 0.0 : 1.0 - Math.Abs(face.FaceNormal.Normalize().DotProduct(targetNormal));
                return new { Face = face, Score = normalPenalty * 1000.0 + distance };
            })
            .OrderBy(x => x.Score)
            .Select(x => x.Face)
            .FirstOrDefault();
    }

    private static void CollectPlanarFaces(GeometryElement? geometryElement, IList<PlanarFace> faces)
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
                CollectPlanarFaces(instance.GetInstanceGeometry(), faces);
            }
            else if (item is GeometryElement nested)
            {
                CollectPlanarFaces(nested, faces);
            }
        }
    }

    private static void ApplyShadowParameters(FamilyInstance newInstance, RoundShadowPlanItem item, CreateRoundShadowBatchRequest request, ICollection<DiagnosticRecord> diagnostics)
    {
        if (request.SetCommentsTrace)
        {
            var trace = BuildTraceComment(request.TraceCommentPrefix, item);
            TrySetParameterValue(newInstance, new[] { "Comments" }, trace, "ROUND_SHADOW_TRACE", diagnostics);
        }

        if (request.CopyDiameter)
        {
            TrySetParameterValue(newInstance, new[] { "Mii_Diameter", "Mii_DimDiameter", "Diameter" }, item.MiiDiameter, "ROUND_SHADOW_DIAMETER", diagnostics);
        }

        if (request.CopyLength)
        {
            TrySetParameterValue(newInstance, new[] { "Mii_DimLength", "Length" }, item.MiiDimLength, "ROUND_SHADOW_LENGTH", diagnostics);
        }

        if (request.CopyElementClass)
        {
            TrySetParameterValue(newInstance, new[] { "Mii_ElementClass" }, item.MiiElementClass, "ROUND_SHADOW_CLASS", diagnostics);
        }

        if (request.CopyElementTier)
        {
            TrySetParameterValue(newInstance, new[] { "Mii_ElementTier" }, item.MiiElementTier, "ROUND_SHADOW_TIER", diagnostics);
        }
    }

    private static string BuildTraceComment(string prefix, RoundShadowPlanItem item)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}|source={1}|sourceType={2}|length={3}|diameter={4}",
            prefix,
            item.SourceInstance.Id.Value,
            item.SourceInstance.Symbol?.Name ?? string.Empty,
            item.MiiDimLength,
            item.MiiDiameter);
    }

    private static int? TryParseSourceIdFromTrace(string trace)
    {
        const string token = "source=";
        var index = trace.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        index += token.Length;
        var end = trace.IndexOf('|', index);
        var raw = end >= 0 ? trace.Substring(index, end - index) : trace.Substring(index);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null;
    }

    private static bool TrySetParameterValue(Element element, IEnumerable<string> parameterNames, string value, string codePrefix, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var parameterName in parameterNames)
        {
            var parameter = LookupWritableParameter(element, parameterName);
            if (parameter == null)
            {
                continue;
            }

            try
            {
                ParameterMutationHelper.SetParameterValue(parameter, value);
                return true;
            }
            catch (Exception ex)
            {
                diagnostics.Add(DiagnosticRecord.Create(codePrefix + "_SET_FAILED", DiagnosticSeverity.Warning, $"Cannot set {parameterName}: {ex.Message}", checked((int)element.Id.Value)));
                return false;
            }
        }

        diagnostics.Add(DiagnosticRecord.Create(codePrefix + "_PARAMETER_MISSING", DiagnosticSeverity.Warning, "Writable parameter was not found.", checked((int)element.Id.Value)));
        return false;
    }

    private static Parameter? LookupWritableParameter(Element element, string parameterName)
    {
        Parameter? parameter = null;
        if (string.Equals(parameterName, "Comments", StringComparison.OrdinalIgnoreCase))
        {
            parameter = element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (parameter != null && !parameter.IsReadOnly)
            {
                return parameter;
            }
        }

        parameter = element.LookupParameter(parameterName);
        return parameter != null && !parameter.IsReadOnly ? parameter : null;
    }

    private static string ReadFirstNonEmptyParameter(Element element, params string[] parameterNames)
    {
        foreach (var parameterName in parameterNames)
        {
            var value = ReadParameterValue(element, parameterName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static bool TryGetAnchorPoint(Element element, out XYZ point)
    {
        if (element.Location is LocationPoint locationPoint)
        {
            point = locationPoint.Point;
            return true;
        }

        if (element.Location is LocationCurve locationCurve)
        {
            point = locationCurve.Curve.Evaluate(0.5, true);
            return true;
        }

        var box = element.get_BoundingBox(null);
        if (box != null)
        {
            point = (box.Min + box.Max) * 0.5;
            return true;
        }

        point = XYZ.Zero;
        return false;
    }

    private RoundWrapperBuildPlan ResolveRoundWrapperBuildPlan(PlatformServices services, Document doc, BuildRoundProjectWrappersRequest? request)
    {
        request ??= new BuildRoundProjectWrappersRequest();
        var sourceFamily = FindFamilyByName(doc, request.SourceFamilyName);
        if (sourceFamily == null)
        {
            throw new InvalidOperationException($"Khong tim thay source family '{request.SourceFamilyName}' trong project.");
        }

        var outputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory)
            ? GetDefaultRoundWrapperOutputDirectory()
            : request.OutputDirectory.Trim();

        outputDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputDirectory));
        var safeSourceName = SanitizeFileName(sourceFamily.Name);
        var plan = new RoundWrapperBuildPlan
        {
            SourceFamily = sourceFamily,
            OutputDirectory = outputDirectory,
            SourceExtractedFilePath = Path.Combine(outputDirectory, safeSourceName + "__source.rfa")
        };

        if (request.GenerateSizeSpecificVariants)
        {
            foreach (var spec in BuildRoundWrapperVariantSpecs(services, doc, request, outputDirectory))
            {
                plan.Specs.Add(spec);
            }
        }
        else
        {
            plan.Specs.Add(BuildRoundWrapperSpec(doc, request.PlanWrapperFamilyName, request.PlanWrapperTypeName, outputDirectory));
            plan.Specs.Add(BuildRoundWrapperSpec(doc, request.ElevXWrapperFamilyName, request.ElevXWrapperTypeName, outputDirectory));
            plan.Specs.Add(BuildRoundWrapperSpec(doc, request.ElevYWrapperFamilyName, request.ElevYWrapperTypeName, outputDirectory));
        }

        return plan;
    }

    private static RoundWrapperBuildSpec BuildRoundWrapperSpec(Document doc, string familyName, string typeName, string outputDirectory)
    {
        return new RoundWrapperBuildSpec
        {
            FamilyName = familyName.Trim(),
            TypeName = typeName.Trim(),
            FilePath = Path.Combine(outputDirectory, SanitizeFileName(familyName.Trim()) + ".rfa"),
            ExistingProjectFamily = FindFamilyByName(doc, familyName),
            PlacementMode = typeName.Trim()
        };
    }

    private IEnumerable<RoundWrapperBuildSpec> BuildRoundWrapperVariantSpecs(
        PlatformServices services,
        Document doc,
        BuildRoundProjectWrappersRequest request,
        string outputDirectory)
    {
        var plan = PlanRoundExternalization(services, doc, new RoundExternalizationPlanRequest
        {
            DocumentKey = services.GetDocumentKey(doc),
            ParentFamilyName = "Penetration Alpha",
            RoundFamilyName = request.SourceFamilyName,
            MaxResults = 10000,
            PlanWrapperFamilyName = request.PlanWrapperFamilyName,
            PlanWrapperTypeName = request.PlanWrapperTypeName,
            ElevXWrapperFamilyName = request.ElevXWrapperFamilyName,
            ElevXWrapperTypeName = request.ElevXWrapperTypeName,
            ElevYWrapperFamilyName = request.ElevYWrapperFamilyName,
            ElevYWrapperTypeName = request.ElevYWrapperTypeName
        });

        var specs = new List<RoundWrapperBuildSpec>();
        var uniqueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Items.Where(x => x.CanExternalize))
        {
            var placementMode = item.ProposedPlacementMode ?? string.Empty;
            var baseFamilyName = item.ProposedTargetFamilyName?.Trim() ?? string.Empty;
            var baseTypeName = item.ProposedTargetTypeName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(baseFamilyName) || string.IsNullOrWhiteSpace(baseTypeName))
            {
                continue;
            }

            var lengthValueString = item.ParentMiiDimLength ?? string.Empty;
            var diameterValueString = item.ParentMiiDiameter ?? string.Empty;
            var familyName = baseFamilyName.Trim();
            var typeName = BuildRoundVariantTypeName(baseTypeName, lengthValueString, diameterValueString);
            var key = string.Concat(familyName, "|", typeName);
            if (!uniqueKeys.Add(key))
            {
                continue;
            }

            specs.Add(new RoundWrapperBuildSpec
            {
                FamilyName = familyName,
                TypeName = typeName,
                FilePath = Path.Combine(outputDirectory, SanitizeFileName(familyName) + ".rfa"),
                ExistingProjectFamily = FindFamilyByName(doc, familyName),
                SizeLengthValueString = lengthValueString,
                SizeDiameterValueString = diameterValueString,
                PlacementMode = placementMode
            });
        }

        return specs
            .OrderBy(x => x.FamilyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ExtractFamilyToPath(Document projectDoc, Family family, string filePath, bool overwrite)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? throw new InvalidOperationException("Output directory invalid."));
        if (File.Exists(filePath) && !overwrite)
        {
            return filePath;
        }

        var familyDoc = projectDoc.EditFamily(family);
        try
        {
            var options = new SaveAsOptions
            {
                OverwriteExistingFile = true
            };
            familyDoc.SaveAs(filePath, options);
            return filePath;
        }
        finally
        {
            familyDoc.Close(false);
        }
    }

    private static Family? BuildWrapperFamilyFile(
        Document projectDoc,
        Family sourceFamily,
        IReadOnlyList<RoundWrapperBuildSpec> specs,
        bool overwrite,
        bool loadIntoProject,
        bool overwriteExistingProjectFamilies,
        List<DiagnosticRecord> diagnostics)
    {
        if (specs == null || specs.Count == 0)
        {
            throw new InvalidOperationException("Khong co wrapper spec nao de build.");
        }

        var primarySpec = specs[0];
        Directory.CreateDirectory(Path.GetDirectoryName(primarySpec.FilePath) ?? throw new InvalidOperationException("Output directory invalid."));
        if (File.Exists(primarySpec.FilePath) && !overwrite)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_FILE_REUSED", DiagnosticSeverity.Info, $"Reuse file co san: {primarySpec.FilePath}."));
            return null;
        }

        var templatePath = ResolveRoundWrapperTemplatePath(projectDoc.Application);
        var wrapperDoc = projectDoc.Application.NewFamilyDocument(templatePath);
        try
        {
            if (!wrapperDoc.IsFamilyDocument)
            {
                throw new InvalidOperationException($"Template wrapper {templatePath} khong mo duoc nhu family document.");
            }

            using var transaction = new Transaction(wrapperDoc, "Build Round project wrapper");
            transaction.Start();
            AgentFailureHandling.Configure(transaction, diagnostics);

            var ownerFamily = wrapperDoc.OwnerFamily ?? throw new InvalidOperationException("Family document khong co OwnerFamily.");
            ownerFamily.Name = primarySpec.FamilyName;
            var sharedParameter = ownerFamily.get_Parameter(BuiltInParameter.FAMILY_SHARED);
            if (sharedParameter != null && !sharedParameter.IsReadOnly && sharedParameter.AsInteger() != 0)
            {
                sharedParameter.Set(0);
            }
            var alwaysVerticalParameter = ownerFamily.get_Parameter(BuiltInParameter.FAMILY_ALWAYS_VERTICAL);
            if (alwaysVerticalParameter != null && !alwaysVerticalParameter.IsReadOnly && alwaysVerticalParameter.AsInteger() != 0)
            {
                alwaysVerticalParameter.Set(0);
                diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_ALWAYS_VERTICAL_DISABLED", DiagnosticSeverity.Info, $"Da tat Always Vertical cho wrapper {primarySpec.FamilyName}."));
            }
            var workPlaneBasedParameter = ownerFamily.get_Parameter(BuiltInParameter.FAMILY_WORK_PLANE_BASED);
            if (workPlaneBasedParameter != null && !workPlaneBasedParameter.IsReadOnly)
            {
                if (workPlaneBasedParameter.AsInteger() != 0)
                {
                    workPlaneBasedParameter.Set(0);
                }

                diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_WORKPLANE_BASED_DISABLED", DiagnosticSeverity.Info, $"Da tat Work Plane-Based cho wrapper {primarySpec.FamilyName} de uu tien hostless project-axis placement."));
            }
            var typeMap = EnsureWrapperTypes(wrapperDoc, specs);
            var visibilityMap = EnsureWrapperVisibilityParameters(wrapperDoc, specs);
            ConfigureWrapperTypeVisibility(wrapperDoc, specs, typeMap, visibilityMap);
            var sourceGraphicsStyle = TryResolveSourceRoundGraphicsStyle(projectDoc, sourceFamily, diagnostics);
            var wrapperMaterialName = ResolveRoundWrapperMaterialName(sourceGraphicsStyle, diagnostics);
            var wrapperMaterial = string.IsNullOrWhiteSpace(wrapperMaterialName)
                ? null
                : FindOrCreateMaterialByName(wrapperDoc, wrapperMaterialName, diagnostics);
            var wrapperSubcategory = sourceGraphicsStyle == null || string.IsNullOrWhiteSpace(sourceGraphicsStyle.SubcategoryName)
                ? null
                : EnsureWrapperSubcategory(wrapperDoc, sourceGraphicsStyle.SubcategoryName, wrapperMaterial, diagnostics);

            foreach (var spec in specs)
            {
                EmbedNativeRoundGeometryInWrapper(wrapperDoc, spec, visibilityMap[spec.TypeName], wrapperMaterial, wrapperSubcategory, diagnostics);
            }

            transaction.Commit();

            var options = new SaveAsOptions
            {
                OverwriteExistingFile = true
            };
            wrapperDoc.SaveAs(primarySpec.FilePath, options);
            diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_FILE_BUILT", DiagnosticSeverity.Info, $"Da build file wrapper {primarySpec.FamilyName} ({specs.Count} types) -> {primarySpec.FilePath}."));

            if (!loadIntoProject)
            {
                return null;
            }

            if (projectDoc.IsModifiable)
            {
                throw new InvalidOperationException("Target project document dang modifiable; khong the LoadFamily tu family document.");
            }

            var loadedFamily = wrapperDoc.LoadFamily(projectDoc, new AlwaysOverwriteFamilyLoadOptions(overwriteExistingProjectFamilies));
            if (loadedFamily == null)
            {
                throw new InvalidOperationException($"LoadFamily(Document, IFamilyLoadOptions) tra ve null cho wrapper {primarySpec.FamilyName}.");
            }

            diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_FAMILY_LOADED", DiagnosticSeverity.Info, $"Da load family {loadedFamily.Name} vao project bang familyDoc.LoadFamily(...).", checked((int)loadedFamily.Id.Value)));
            return loadedFamily;
        }
        finally
        {
            wrapperDoc.Close(false);
        }
    }

    private static string ResolveRoundWrapperTemplatePath(Autodesk.Revit.ApplicationServices.Application application)
    {
        var version = application.VersionNumber ?? "2024";
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", $"RVT {version}", "Family Templates"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Autodesk", $"RVT {version}", "Family Templates", "English")
        };

        var candidateNames = new[]
        {
            "Metric Generic Model.rft",
            "Metric Generic Model face based.rft"
        };

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var candidateName in candidateNames)
            {
                var exact = Directory.EnumerateFiles(root, candidateName, SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(exact))
                {
                    return exact;
                }
            }

            var loose = Directory.EnumerateFiles(root, "*.rft", SearchOption.AllDirectories)
                .FirstOrDefault(path =>
                {
                    var name = Path.GetFileName(path);
                    return name.IndexOf("Metric Generic Model", StringComparison.OrdinalIgnoreCase) >= 0
                           && name.IndexOf("face", StringComparison.OrdinalIgnoreCase) < 0;
                });
            if (!string.IsNullOrWhiteSpace(loose))
            {
                return loose!;
            }

            loose = Directory.EnumerateFiles(root, "*.rft", SearchOption.AllDirectories)
                .FirstOrDefault(path =>
                {
                    var name = Path.GetFileName(path);
                    return name.IndexOf("Generic Model", StringComparison.OrdinalIgnoreCase) >= 0
                           && name.IndexOf("face", StringComparison.OrdinalIgnoreCase) >= 0;
                });
            if (!string.IsNullOrWhiteSpace(loose))
            {
                return loose!;
            }
        }

        throw new InvalidOperationException("Khong tim thay family template cho Round wrapper (uu tien Metric Generic Model.rft, fallback face based).");
    }

    private static void EmbedNativeRoundGeometryInWrapper(
        Document wrapperDoc,
        RoundWrapperBuildSpec spec,
        FamilyParameter visibilityParameter,
        Material? wrapperMaterial,
        Category? wrapperSubcategory,
        ICollection<DiagnosticRecord> diagnostics)
    {
        var geometryPlan = BuildRoundWrapperGeometryPlan(spec, diagnostics);
        var sketchPlane = SketchPlane.Create(wrapperDoc, Plane.CreateByNormalAndOrigin(geometryPlan.SketchPlaneNormal, geometryPlan.SketchPlaneOrigin));
        var profile = BuildCircularProfile(geometryPlan.ProfileCenter, geometryPlan.Radius, geometryPlan.ProfileXAxis, geometryPlan.ProfileYAxis);
        var solid = wrapperDoc.FamilyCreate.NewExtrusion(true, profile, sketchPlane, geometryPlan.ExtrusionDepth);
        if (solid == null)
        {
            throw new InvalidOperationException("Khong tao duoc native Round extrusion cho clean project family.");
        }

        AssociateVisibilityParameter(wrapperDoc, solid, visibilityParameter, diagnostics, $"ROUND_WRAPPER_VISIBILITY_{spec.TypeName}");
        if (wrapperSubcategory != null)
        {
            ApplySubcategoryToElement(solid, wrapperSubcategory, diagnostics, $"ROUND_WRAPPER_SUBCATEGORY_{spec.TypeName}");
        }

        if (wrapperMaterial != null)
        {
            ApplyMaterialToElement(solid, wrapperMaterial, diagnostics, $"ROUND_WRAPPER_MATERIAL_{spec.TypeName}");
        }

        wrapperDoc.Regenerate();
        diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_NATIVE_GEOMETRY_CREATED", DiagnosticSeverity.Info, $"Da tao native Round geometry {spec.TypeName} cho family {wrapperDoc.Title}.", checked((int)solid.Id.Value)));
    }

    private static CurveArrArray BuildCircularProfile(XYZ center, double radius, XYZ xAxis, XYZ yAxis)
    {
        var normalizedX = SafeNormalize(xAxis);
        var normalizedY = SafeNormalize(yAxis);
        var profile = new CurveArrArray();
        var loop = new CurveArray();
        loop.Append(Arc.Create(center, radius, 0.0, Math.PI, normalizedX, normalizedY));
        loop.Append(Arc.Create(center, radius, Math.PI, Math.PI * 2.0, normalizedX, normalizedY));
        profile.Append(loop);
        return profile;
    }

    private static RoundWrapperGeometryPlan BuildRoundWrapperGeometryPlan(RoundWrapperBuildSpec spec, ICollection<DiagnosticRecord> diagnostics)
    {
        var length = TryParseImperialLengthStringToFeet(spec.SizeLengthValueString, out var parsedLength) && parsedLength > 1e-6
            ? parsedLength
            : 7.0 / 12.0;
        var diameter = TryParseImperialLengthStringToFeet(spec.SizeDiameterValueString, out var parsedDiameter) && parsedDiameter > 1e-6
            ? parsedDiameter
            : 6.0 / 12.0;

        var halfLength = Math.Max(length * 0.5, 1.0 / 32.0);
        var radius = Math.Max(diameter * 0.5, 1.0 / 32.0);

        diagnostics.Add(DiagnosticRecord.Create(
            "ROUND_WRAPPER_GEOMETRY_PLAN",
            DiagnosticSeverity.Info,
            $"Wrapper geometry axis={spec.TypeName}, length={length:0.######}ft, diameter={diameter:0.######}ft."));

        var geometryAxis = ResolveWrapperGeometryAxisFromTypeName(spec.TypeName);
        if (string.Equals(geometryAxis, "AXIS_Z", StringComparison.OrdinalIgnoreCase))
        {
            return new RoundWrapperGeometryPlan
            {
                GeometryAxis = geometryAxis,
                SketchPlaneNormal = XYZ.BasisZ,
                SketchPlaneOrigin = new XYZ(0.0, 0.0, -halfLength),
                ProfileCenter = new XYZ(0.0, 0.0, -halfLength),
                ProfileXAxis = XYZ.BasisX,
                ProfileYAxis = XYZ.BasisY,
                Radius = radius,
                ExtrusionDepth = length
            };
        }

        if (string.Equals(geometryAxis, "AXIS_Y", StringComparison.OrdinalIgnoreCase))
        {
            return new RoundWrapperGeometryPlan
            {
                GeometryAxis = geometryAxis,
                SketchPlaneNormal = XYZ.BasisY,
                SketchPlaneOrigin = new XYZ(0.0, -halfLength, 0.0),
                ProfileCenter = new XYZ(0.0, -halfLength, 0.0),
                ProfileXAxis = XYZ.BasisZ,
                ProfileYAxis = XYZ.BasisX,
                Radius = radius,
                ExtrusionDepth = length
            };
        }

        return new RoundWrapperGeometryPlan
        {
            GeometryAxis = "AXIS_X",
            SketchPlaneNormal = XYZ.BasisX,
            SketchPlaneOrigin = new XYZ(-halfLength, 0.0, 0.0),
            ProfileCenter = new XYZ(-halfLength, 0.0, 0.0),
            ProfileXAxis = XYZ.BasisY,
            ProfileYAxis = XYZ.BasisZ,
            Radius = radius,
            ExtrusionDepth = length
        };
    }

    private static string ResolveWrapperGeometryAxisFromTypeName(string wrapperTypeName)
    {
        if (string.IsNullOrWhiteSpace(wrapperTypeName))
        {
            return string.Empty;
        }

        var separatorIndex = wrapperTypeName.IndexOf("__", StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            return wrapperTypeName.Substring(0, separatorIndex);
        }

        return wrapperTypeName;
    }

    private static Dictionary<string, FamilyType> EnsureWrapperTypes(Document familyDoc, IReadOnlyList<RoundWrapperBuildSpec> specs)
    {
        var familyManager = familyDoc.FamilyManager ?? throw new InvalidOperationException("Family document khong co FamilyManager.");
        var desiredTypeNames = specs
            .Select(x => x.TypeName?.Trim() ?? string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (desiredTypeNames.Count == 0)
        {
            throw new InvalidOperationException("Khong co wrapper type nao de tao.");
        }

        if (familyManager.CurrentType == null && familyManager.Types.Cast<FamilyType>().All(x => x == null))
        {
            familyManager.NewType(desiredTypeNames[0]);
        }

        foreach (var desiredTypeName in desiredTypeNames)
        {
            var existingType = familyManager.Types
                .Cast<FamilyType>()
                .FirstOrDefault(x => string.Equals(x.Name, desiredTypeName, StringComparison.OrdinalIgnoreCase));
            if (existingType == null)
            {
                familyManager.NewType(desiredTypeName);
            }
        }

        var removableTypes = familyManager.Types
            .Cast<FamilyType>()
            .Where(x => !desiredTypeNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        foreach (var removableType in removableTypes)
        {
            if (familyManager.Types.Cast<FamilyType>().Count() <= desiredTypeNames.Count)
            {
                break;
            }

            familyManager.CurrentType = removableType;
            familyManager.DeleteCurrentType();
        }

        var typeMap = familyManager.Types
            .Cast<FamilyType>()
            .Where(x => desiredTypeNames.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(x => x.Name, x => x, StringComparer.OrdinalIgnoreCase);
        foreach (var desiredTypeName in desiredTypeNames)
        {
            if (!typeMap.ContainsKey(desiredTypeName))
            {
                throw new InvalidOperationException($"Khong tao duoc wrapper type {desiredTypeName}.");
            }
        }

        familyManager.CurrentType = typeMap[desiredTypeNames[0]];
        return typeMap;
    }

    private static Dictionary<string, FamilyParameter> EnsureWrapperVisibilityParameters(Document familyDoc, IReadOnlyList<RoundWrapperBuildSpec> specs)
    {
        var familyManager = familyDoc.FamilyManager ?? throw new InvalidOperationException("Family document khong co FamilyManager.");
        var parameterMap = familyManager.Parameters
            .Cast<FamilyParameter>()
            .ToDictionary(x => x.Definition.Name, x => x, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, FamilyParameter>(StringComparer.OrdinalIgnoreCase);

        foreach (var spec in specs)
        {
            var parameterName = BuildWrapperVisibilityParameterName(spec.TypeName);
            if (!parameterMap.TryGetValue(parameterName, out var familyParameter))
            {
                familyParameter = familyManager.AddParameter(parameterName, GroupTypeId.Visibility, SpecTypeId.Boolean.YesNo, false);
                parameterMap[parameterName] = familyParameter;
            }

            result[spec.TypeName] = familyParameter;
        }

        return result;
    }

    private static void ConfigureWrapperTypeVisibility(
        Document familyDoc,
        IReadOnlyList<RoundWrapperBuildSpec> specs,
        IReadOnlyDictionary<string, FamilyType> typeMap,
        IReadOnlyDictionary<string, FamilyParameter> visibilityMap)
    {
        var familyManager = familyDoc.FamilyManager ?? throw new InvalidOperationException("Family document khong co FamilyManager.");
        var distinctSpecs = specs
            .GroupBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var currentSpec in distinctSpecs)
        {
            familyManager.CurrentType = typeMap[currentSpec.TypeName];
            foreach (var targetSpec in distinctSpecs)
            {
                familyManager.Set(visibilityMap[targetSpec.TypeName], string.Equals(currentSpec.TypeName, targetSpec.TypeName, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
            }
        }

        familyManager.CurrentType = typeMap[distinctSpecs[0].TypeName];
    }

    private static string BuildWrapperVisibilityParameterName(string typeName)
    {
        var sanitized = new string((typeName ?? string.Empty)
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray())
            .Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "VIS_WRAPPER_DEFAULT" : "VIS_" + sanitized;
    }

    private static void AssociateVisibilityParameter(Document familyDoc, Element element, FamilyParameter visibilityParameter, ICollection<DiagnosticRecord> diagnostics, string code)
    {
        var visibleParam = element.get_Parameter(BuiltInParameter.IS_VISIBLE_PARAM);
        if (visibleParam == null || visibleParam.IsReadOnly)
        {
            diagnostics.Add(DiagnosticRecord.Create(code + "_PARAMETER_MISSING", DiagnosticSeverity.Warning, "Khong tim thay visibility parameter de associate.", checked((int)element.Id.Value)));
            return;
        }

        try
        {
            familyDoc.FamilyManager.AssociateElementParameterToFamilyParameter(visibleParam, visibilityParameter);
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create(code + "_ASSOCIATE_FAILED", DiagnosticSeverity.Warning, ex.Message, checked((int)element.Id.Value)));
        }
    }

    private static Family? FindFamilyByName(Document doc, string familyName)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(Family))
            .Cast<Family>()
            .FirstOrDefault(x => string.Equals(x.Name ?? string.Empty, familyName ?? string.Empty, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetDefaultRoundWrapperOutputDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Contracts.Common.BridgeConstants.AppDataFolderName,
            "generated",
            "round_project_wrappers");
    }

    private static RoundSourceGraphicsStyleSnapshot? TryResolveSourceRoundGraphicsStyle(Document projectDoc, Family sourceFamily, ICollection<DiagnosticRecord> diagnostics)
    {
        Document? familyDoc = null;
        try
        {
            familyDoc = projectDoc.EditFamily(sourceFamily);
            var style = FindFirstRoundSourceGraphicsStyle(familyDoc);
            if (style != null)
            {
                if (!string.IsNullOrWhiteSpace(style.MaterialName))
                {
                    diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_SOURCE_MATERIAL_FOUND", DiagnosticSeverity.Info, $"Source family {sourceFamily.Name} material = {style.MaterialName}."));
                }

                if (!string.IsNullOrWhiteSpace(style.SubcategoryName))
                {
                    diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_SOURCE_SUBCATEGORY_FOUND", DiagnosticSeverity.Info, $"Source family {sourceFamily.Name} subcategory = {style.SubcategoryName}."));
                }

                return style;
            }

            diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_SOURCE_STYLE_NOT_FOUND", DiagnosticSeverity.Info, $"Source family {sourceFamily.Name} khong co explicit material/subcategory tren geometry; wrapper se giu theo category/default material."));
            return null;
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_SOURCE_STYLE_READ_FAILED", DiagnosticSeverity.Warning, ex.Message));
            return null;
        }
        finally
        {
            if (familyDoc != null)
            {
                try
                {
                    familyDoc.Close(false);
                }
                catch
                {
                    // ignore cleanup failure
                }
            }
        }
    }

    private static string ResolveRoundWrapperMaterialName(RoundSourceGraphicsStyleSnapshot? sourceGraphicsStyle, ICollection<DiagnosticRecord> diagnostics)
    {
        const string preferredMaterialName = "Mii_Penetration";
        diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_TARGET_MATERIAL_SELECTED", DiagnosticSeverity.Info, $"Wrapper se uu tien material {preferredMaterialName}."));

        if (!string.IsNullOrWhiteSpace(preferredMaterialName))
        {
            return preferredMaterialName;
        }

        return sourceGraphicsStyle?.MaterialName ?? string.Empty;
    }

    private static RoundSourceGraphicsStyleSnapshot? FindFirstRoundSourceGraphicsStyle(Document familyDoc)
    {
        foreach (var genericForm in new FilteredElementCollector(familyDoc)
                     .OfClass(typeof(GenericForm))
                     .WhereElementIsNotElementType()
                     .Cast<GenericForm>())
        {
            var style = TryGetElementGraphicsStyle(familyDoc, genericForm);
            if (style != null)
            {
                return style;
            }
        }

        foreach (var element in new FilteredElementCollector(familyDoc)
                     .WhereElementIsNotElementType()
                     .WhereElementIsViewIndependent())
        {
            var style = TryGetElementGraphicsStyle(familyDoc, element);
            if (style != null)
            {
                return style;
            }
        }

        var familyCategory = familyDoc.OwnerFamily?.FamilyCategory;
        if (familyCategory != null)
        {
            foreach (var subcategory in familyCategory.SubCategories.Cast<Category>())
            {
                var subcategoryMaterialName = subcategory.Material?.Name ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(subcategoryMaterialName))
                {
                    return new RoundSourceGraphicsStyleSnapshot
                    {
                        MaterialName = subcategoryMaterialName,
                        SubcategoryName = subcategory.Name ?? string.Empty
                    };
                }

                if (!string.IsNullOrWhiteSpace(subcategory.Name))
                {
                    return new RoundSourceGraphicsStyleSnapshot
                    {
                        MaterialName = string.Empty,
                        SubcategoryName = subcategory.Name
                    };
                }
            }

            var familyCategoryMaterialName = familyCategory.Material?.Name ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(familyCategoryMaterialName))
            {
                return new RoundSourceGraphicsStyleSnapshot
                {
                    MaterialName = familyCategoryMaterialName,
                    SubcategoryName = string.Empty
                };
            }
        }

        return null;
    }

    private static RoundSourceGraphicsStyleSnapshot? TryGetElementGraphicsStyle(Document doc, Element element)
    {
        var explicitMaterialName = TryGetElementMaterialName(doc, element);
        var subcategory = TryGetElementSubcategory(element);
        var subcategoryName = subcategory?.Name ?? string.Empty;
        var subcategoryMaterialName = subcategory?.Material?.Name ?? string.Empty;
        var materialName = !string.IsNullOrWhiteSpace(explicitMaterialName)
            ? explicitMaterialName
            : subcategoryMaterialName;
        if (string.IsNullOrWhiteSpace(materialName) && string.IsNullOrWhiteSpace(subcategoryName))
        {
            return null;
        }

        return new RoundSourceGraphicsStyleSnapshot
        {
            MaterialName = materialName,
            SubcategoryName = subcategoryName
        };
    }

    private static string TryGetElementMaterialName(Document doc, Element element)
    {
        var materialParameter = element.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
        if (materialParameter == null || materialParameter.StorageType != StorageType.ElementId)
        {
            return string.Empty;
        }

        var materialId = materialParameter.AsElementId();
        if (materialId == null || materialId == ElementId.InvalidElementId || materialId.Value <= 0)
        {
            return string.Empty;
        }

        return (doc.GetElement(materialId) as Material)?.Name ?? string.Empty;
    }

    private static Category? TryGetElementSubcategory(Element element)
    {
        return element is GenericForm genericForm
            ? genericForm.Subcategory
            : null;
    }

    private static Material? FindOrCreateMaterialByName(Document doc, string materialName, ICollection<DiagnosticRecord> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(materialName))
        {
            return null;
        }

        var existing = new FilteredElementCollector(doc)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .FirstOrDefault(x => string.Equals(x.Name ?? string.Empty, materialName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing;
        }

        try
        {
            var materialId = Material.Create(doc, materialName);
            var created = doc.GetElement(materialId) as Material;
            if (created != null)
            {
                diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_MATERIAL_CREATED", DiagnosticSeverity.Info, $"Da tao material {materialName} trong wrapper family.", checked((int)created.Id.Value)));
            }

            return created;
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_MATERIAL_CREATE_FAILED", DiagnosticSeverity.Warning, ex.Message));
            return null;
        }
    }

    private static Category? EnsureWrapperSubcategory(Document familyDoc, string subcategoryName, Material? material, ICollection<DiagnosticRecord> diagnostics)
    {
        var familyCategory = familyDoc.OwnerFamily?.FamilyCategory;
        if (familyCategory == null || string.IsNullOrWhiteSpace(subcategoryName))
        {
            return null;
        }

        Category? subcategory = familyCategory.SubCategories
            .Cast<Category>()
            .FirstOrDefault(x => string.Equals(x.Name ?? string.Empty, subcategoryName, StringComparison.OrdinalIgnoreCase));
        if (subcategory == null)
        {
            try
            {
                subcategory = familyDoc.Settings.Categories.NewSubcategory(familyCategory, subcategoryName);
                diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_SUBCATEGORY_CREATED", DiagnosticSeverity.Info, $"Da tao subcategory {subcategoryName} cho wrapper family."));
            }
            catch (Exception ex)
            {
                diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_SUBCATEGORY_CREATE_FAILED", DiagnosticSeverity.Warning, ex.Message));
                return null;
            }
        }

        if (material != null)
        {
            try
            {
                subcategory.Material = material;
            }
            catch (Exception ex)
            {
                diagnostics.Add(DiagnosticRecord.Create("ROUND_WRAPPER_SUBCATEGORY_MATERIAL_SET_FAILED", DiagnosticSeverity.Warning, ex.Message));
            }
        }

        return subcategory;
    }

    private static void ApplySubcategoryToElement(Element element, Category subcategory, ICollection<DiagnosticRecord> diagnostics, string code)
    {
        try
        {
            if (element is GenericForm genericForm)
            {
                genericForm.Subcategory = subcategory;
                return;
            }

            diagnostics.Add(DiagnosticRecord.Create(code + "_UNSUPPORTED", DiagnosticSeverity.Warning, "Element khong ho tro gan subcategory truc tiep.", checked((int)element.Id.Value)));
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create(code + "_SET_FAILED", DiagnosticSeverity.Warning, ex.Message, checked((int)element.Id.Value)));
        }
    }

    private static void ApplyMaterialToElement(Element element, Material material, ICollection<DiagnosticRecord> diagnostics, string code)
    {
        var materialParameter = element.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
        if (materialParameter == null || materialParameter.IsReadOnly || materialParameter.StorageType != StorageType.ElementId)
        {
            diagnostics.Add(DiagnosticRecord.Create(code + "_PARAMETER_MISSING", DiagnosticSeverity.Warning, "Khong tim thay material parameter de gan cho wrapper geometry.", checked((int)element.Id.Value)));
            return;
        }

        try
        {
            materialParameter.Set(material.Id);
        }
        catch (Exception ex)
        {
            diagnostics.Add(DiagnosticRecord.Create(code + "_SET_FAILED", DiagnosticSeverity.Warning, ex.Message, checked((int)element.Id.Value)));
        }
    }

    private static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "family";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = input.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "family" : sanitized;
    }

    private static string BuildRoundVariantTypeName(string baseTypeName, string lengthValueString, string diameterValueString)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}__L{1}__D{2}",
            baseTypeName.Trim(),
            BuildSizeToken(lengthValueString),
            BuildSizeToken(diameterValueString));
    }

    private static string BuildSizeToken(string valueString)
    {
        if (TryParseImperialLengthStringToFeet(valueString, out var feet))
        {
            var total256thsOfInch = (int)Math.Round(feet * 12.0 * 256.0, MidpointRounding.AwayFromZero);
            return total256thsOfInch.ToString(CultureInfo.InvariantCulture);
        }

        var fallback = new string((valueString ?? string.Empty).Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(fallback) ? "0" : fallback;
    }

    private static bool TryParseImperialLengthStringToFeet(string valueString, out double valueFeet)
    {
        valueFeet = 0.0;
        if (string.IsNullOrWhiteSpace(valueString))
        {
            return false;
        }

        var text = valueString.Trim();
        var sign = 1.0;
        if (text.StartsWith("-", StringComparison.Ordinal))
        {
            sign = -1.0;
            text = text.Substring(1).Trim();
        }

        var feet = 0.0;
        var feetMarker = text.IndexOf('\'');
        if (feetMarker >= 0)
        {
            var feetPart = text.Substring(0, feetMarker).Trim();
            if (!string.IsNullOrWhiteSpace(feetPart) &&
                !double.TryParse(feetPart, NumberStyles.Float, CultureInfo.InvariantCulture, out feet))
            {
                return false;
            }

            text = text.Substring(feetMarker + 1).Trim();
        }

        text = text.Replace("\"", string.Empty).Trim();
        if (text.StartsWith("-", StringComparison.Ordinal))
        {
            text = text.Substring(1).Trim();
        }

        double inches = 0.0;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var tokens = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (token.IndexOf('/') >= 0)
                {
                    var fractionParts = token.Split('/');
                    if (fractionParts.Length != 2 ||
                        !double.TryParse(fractionParts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) ||
                        !double.TryParse(fractionParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) ||
                        Math.Abs(denominator) < 1e-9)
                    {
                        return false;
                    }

                    inches += numerator / denominator;
                }
                else if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var whole))
                {
                    inches += whole;
                }
                else
                {
                    return false;
                }
            }
        }

        valueFeet = sign * (feet + inches / 12.0);
        return true;
    }

    private static string ReadParameterValue(Element element, string parameterName)
    {
        var parameter = element.LookupParameter(parameterName);
        if (parameter == null)
        {
            return string.Empty;
        }

        var valueString = parameter.AsValueString();
        if (!string.IsNullOrWhiteSpace(valueString))
        {
            return valueString;
        }

        return PlatformServices.ParameterValue(parameter);
    }

    private static bool IsAlignedToProject(FamilyInstance instance, double toleranceDegrees)
    {
        return string.Equals(ClassifyAxis(instance, toleranceDegrees).Status, "ALIGNED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryPopulateRoundTransform(FamilyInstance round, RoundExternalizationPlanItemDto item)
    {
        try
        {
            var transform = round.GetTransform();
            if (transform == null)
            {
                return false;
            }

            var origin = TryGetAnchorPoint(round, out var anchorPoint)
                ? anchorPoint
                : (transform.Origin ?? XYZ.Zero);
            var basisX = SafeNormalize(transform.BasisX);
            var basisY = SafeNormalize(transform.BasisY);
            var basisZ = SafeNormalize(transform.BasisZ);

            item.Origin = ToVector(origin);
            item.BasisX = ToVector(basisX);
            item.BasisY = ToVector(basisY);
            item.BasisZ = ToVector(basisZ);
            item.AngleXDegrees = ComputeAngleDegrees(basisX, XYZ.BasisX, false);
            item.AngleYDegrees = ComputeAngleDegrees(basisY, XYZ.BasisY, false);
            item.AngleZDegrees = ComputeAngleDegrees(basisZ, XYZ.BasisZ, false);
            item.RotationAroundProjectZDegrees = ComputeSignedPlanRotationDegrees(basisX);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static AxisVectorDto ToVector(XYZ vector)
    {
        return new AxisVectorDto
        {
            X = vector.X,
            Y = vector.Y,
            Z = vector.Z
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

    private static string DetermineExternalizationMode(AxisVectorDto basisX, AxisVectorDto basisY, AxisVectorDto basisZ)
    {
        var absX = Math.Abs(basisX.Z);
        var absY = Math.Abs(basisY.Z);
        var absZ = Math.Abs(basisZ.Z);

        if (absZ >= absX && absZ >= absY)
        {
            return "PLAN_ZUP";
        }

        return absX >= absY ? "ELEV_XUP" : "ELEV_YUP";
    }

    private static string DetermineTargetGeometryAxis(AxisVectorDto basisX)
    {
        var absX = Math.Abs(basisX.X);
        var absY = Math.Abs(basisX.Y);
        var absZ = Math.Abs(basisX.Z);

        if (absZ >= absX && absZ >= absY)
        {
            return "AXIS_Z";
        }

        return absX >= absY ? "AXIS_X" : "AXIS_Y";
    }

    private static string BuildExternalizationPlacementNote(string placementMode, string targetGeometryAxis)
    {
        return placementMode switch
        {
            "PLAN_ZUP" => $"Dat Round ngoai project bang clean family align project axes; geometry chay theo {targetGeometryAxis}.",
            "ELEV_XUP" => $"Dat Round ngoai project bang clean family align project axes; geometry chay theo {targetGeometryAxis}.",
            "ELEV_YUP" => $"Dat Round ngoai project bang clean family align project axes; geometry chay theo {targetGeometryAxis}.",
            _ => "Can review tay vi khong classify duoc placement mode."
        };
    }

    private static void ResolveExternalizationTarget(RoundExternalizationPlanRequest request, string targetGeometryAxis, out string familyName, out string typeName)
    {
        switch (targetGeometryAxis)
        {
            case "AXIS_X":
                familyName = request.PlanWrapperFamilyName;
                typeName = request.PlanWrapperTypeName;
                break;
            case "AXIS_Z":
                familyName = request.ElevXWrapperFamilyName;
                typeName = request.ElevXWrapperTypeName;
                break;
            case "AXIS_Y":
                familyName = request.ElevYWrapperFamilyName;
                typeName = request.ElevYWrapperTypeName;
                break;
            default:
                familyName = string.Empty;
                typeName = string.Empty;
                break;
        }
    }

    private static string BuildExternalizationTraceComment(string prefix, RoundExternalizationPlanItemDto item)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}|oldRound={1}|parent={2}|mode={3}|parentType={4}",
            prefix,
            item.RoundElementId,
            item.ParentElementId.HasValue ? item.ParentElementId.Value.ToString(CultureInfo.InvariantCulture) : string.Empty,
            item.ProposedPlacementMode,
            item.ParentTypeName);
    }

    private static AxisClassification ClassifyAxis(FamilyInstance instance, double toleranceDegrees)
    {
        try
        {
            var transform = instance.GetTransform();
            var angleZ = ToDegrees(transform.BasisZ.AngleTo(XYZ.BasisZ));
            if (angleZ > toleranceDegrees && Math.Abs(angleZ - 180.0) > toleranceDegrees)
            {
                return new AxisClassification("TILTED_OUT_OF_PROJECT_Z", $"BasisZ lệch {angleZ.ToString("0.###", CultureInfo.InvariantCulture)}° so với trục Z project.");
            }

            var angleX = ToDegrees(transform.BasisX.AngleTo(XYZ.BasisX));
            var angleY = ToDegrees(transform.BasisY.AngleTo(XYZ.BasisY));
            if ((angleX > toleranceDegrees && Math.Abs(angleX - 180.0) > toleranceDegrees) ||
                (angleY > toleranceDegrees && Math.Abs(angleY - 180.0) > toleranceDegrees))
            {
                return new AxisClassification("ROTATED_IN_VIEW", $"BasisX/BasisY lệch project axes (X={angleX:0.###}°, Y={angleY:0.###}°).");
            }

            if (instance.Mirrored)
            {
                return new AxisClassification("MIRRORED", "Instance đang mirrored.");
            }

            return new AxisClassification("ALIGNED", "Khớp trục project.");
        }
        catch (Exception ex)
        {
            return new AxisClassification("TRANSFORM_UNAVAILABLE", ex.Message);
        }
    }

    private static double ToDegrees(double radians)
    {
        return radians * 180.0 / Math.PI;
    }

    private static void EnsurePenetrationParentFamilyDocument(Document doc, SyncPenetrationAlphaNestedTypesRequest request)
    {
        if (!doc.IsFamilyDocument)
        {
            throw new InvalidOperationException("Tool nay chi duoc phep chay tren family document Penetration Alpha M.");
        }

        var ownerFamilyName = doc.OwnerFamily?.Name ?? string.Empty;
        var titleWithoutExtension = Path.GetFileNameWithoutExtension(doc.Title ?? string.Empty);
        var pathWithoutExtension = Path.GetFileNameWithoutExtension(doc.PathName ?? string.Empty);
        var candidates = new[]
        {
            ownerFamilyName,
            titleWithoutExtension,
            pathWithoutExtension
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        if (!candidates.Contains(request.ParentFamilyName, StringComparer.OrdinalIgnoreCase))
        {
            var actualLabel = candidates.Count > 0 ? string.Join(" / ", candidates) : string.Empty;
            throw new InvalidOperationException($"Family document dang target la '{actualLabel}', khong phai '{request.ParentFamilyName}'.");
        }
    }

    private static NestedPenetrationTypeSyncPlan AnalyzeNestedPenetrationTypeSync(
        UIApplication uiapp,
        PlatformServices services,
        Document familyDoc,
        SyncPenetrationAlphaNestedTypesRequest request,
        ICollection<DiagnosticRecord> diagnostics)
    {
        var familyManager = familyDoc.FamilyManager ?? throw new InvalidOperationException("Family document khong co FamilyManager.");
        var parentTypes = familyManager.Types
            .Cast<FamilyType>()
            .Where(x => x != null)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (parentTypes.Count == 0)
        {
            throw new InvalidOperationException($"Family {request.ParentFamilyName} khong co type nao.");
        }

        var nestedInstances = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType()
            .Cast<FamilyInstance>()
            .Where(x => string.Equals(x.Symbol?.Family?.Name ?? string.Empty, request.NestedFamilyName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Id.Value)
            .ToList();
        if (nestedInstances.Count == 0)
        {
            throw new InvalidOperationException($"Khong tim thay nested instance nao cua family '{request.NestedFamilyName}' trong family doc '{familyDoc.Title}'.");
        }

        if (request.RequireSingleNestedInstance && nestedInstances.Count != 1)
        {
            throw new InvalidOperationException($"Yeu cau chi 1 nested instance '{request.NestedFamilyName}', nhung tim thay {nestedInstances.Count}.");
        }

        var childSymbols = CollectNestedFamilySymbols(familyDoc, request.NestedFamilyName);
        if (childSymbols.Count == 0)
        {
            throw new InvalidOperationException($"Khong tim thay nested family symbols cua '{request.NestedFamilyName}' trong family doc '{familyDoc.Title}'.");
        }

        var childSymbolMap = childSymbols
            .GroupBy(x => x.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToDictionary(x => x.Name ?? string.Empty, x => x, StringComparer.OrdinalIgnoreCase);

        var preferredSeedSymbol = !string.IsNullOrWhiteSpace(request.PreferredSeedTypeName) &&
                                  childSymbolMap.TryGetValue(request.PreferredSeedTypeName.Trim(), out var preferred)
            ? preferred
            : childSymbols.FirstOrDefault();

        var plan = new NestedPenetrationTypeSyncPlan
        {
            ParentFamilyName = request.ParentFamilyName,
            NestedFamilyName = request.NestedFamilyName,
            FamilyManager = familyManager,
            OriginalCurrentType = familyManager.CurrentType,
            PreferredSeedSymbol = preferredSeedSymbol,
            ProjectDocument = ResolveProjectDocumentForReload(uiapp, services, familyDoc, request)
        };

        var referenceNestedTypeParameter = ResolveNestedTypeElementParameter(familyManager, nestedInstances[0], request.NestedFamilyName);
        var associatedTypeControl = familyManager.GetAssociatedFamilyParameter(referenceNestedTypeParameter);
        plan.TypeControlParameterName = associatedTypeControl?.Definition?.Name ?? BuildNestedTypeControlParameterName(request.NestedFamilyName);
        plan.TypeControlParameterExists = associatedTypeControl != null;

        plan.ParentTypes.AddRange(parentTypes);
        plan.NestedInstanceIds.AddRange(nestedInstances.Select(x => checked((int)x.Id.Value)));

        foreach (var parentType in parentTypes)
        {
            familyManager.CurrentType = parentType;
            familyDoc.Regenerate();

            var referenceNestedInstance = familyDoc.GetElement(nestedInstances[0].Id) as FamilyInstance
                                          ?? nestedInstances[0];
            var currentChildSymbol = referenceNestedInstance.Symbol;
            var targetTypeName = parentType.Name ?? string.Empty;
            childSymbolMap.TryGetValue(targetTypeName, out var existingTargetSymbol);

            plan.Items.Add(new NestedPenetrationTypeSyncPlanItem
            {
                ParentType = parentType,
                CurrentChildSymbol = currentChildSymbol,
                ExistingTargetSymbol = existingTargetSymbol,
                SeedSymbol = currentChildSymbol ?? preferredSeedSymbol,
                TargetTypeName = targetTypeName,
                ReferenceNestedInstanceId = checked((int)referenceNestedInstance.Id.Value)
            });
        }

        if (plan.OriginalCurrentType != null)
        {
            familyManager.CurrentType = plan.OriginalCurrentType;
            familyDoc.Regenerate();
        }

        diagnostics.Add(DiagnosticRecord.Create("PEN_SYNC_PARENT_TYPE_COUNT", DiagnosticSeverity.Info, $"Parent type count = {plan.ParentTypes.Count}."));
        diagnostics.Add(DiagnosticRecord.Create("PEN_SYNC_NESTED_INSTANCE_COUNT", DiagnosticSeverity.Info, $"Nested instance count = {plan.NestedInstanceIds.Count}."));
        diagnostics.Add(DiagnosticRecord.Create("PEN_SYNC_EXISTING_CHILD_TYPE_COUNT", DiagnosticSeverity.Info, $"Existing nested type count = {childSymbols.Count}."));

        return plan;
    }

    private static List<FamilySymbol> CollectNestedFamilySymbols(Document familyDoc, string nestedFamilyName)
    {
        return new FilteredElementCollector(familyDoc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Where(x => string.Equals(x.Family?.Name ?? string.Empty, nestedFamilyName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Parameter ResolveNestedTypeElementParameter(FamilyManager familyManager, FamilyInstance nestedInstance, string nestedFamilyName)
    {
        var candidates = new[]
        {
            nestedInstance.LookupParameter("Type"),
            nestedInstance.LookupParameter("Family and Type"),
            nestedInstance.LookupParameter("Family"),
            nestedInstance.Parameters.Cast<Parameter>().FirstOrDefault(x => string.Equals(x.Definition?.Name ?? string.Empty, "Type", StringComparison.OrdinalIgnoreCase))
        }.Where(x => x != null).Distinct().ToList();

        foreach (var candidate in candidates)
        {
            if (candidate == null)
            {
                continue;
            }

            if (familyManager.GetAssociatedFamilyParameter(candidate) != null || familyManager.CanElementParameterBeAssociated(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"Khong tim thay element parameter co the associate de dieu khien nested family type cho '{nestedFamilyName}'.");
    }

    private static FamilyParameter EnsureNestedTypeControlFamilyParameter(
        FamilyManager familyManager,
        FamilyInstance nestedInstance,
        Parameter nestedTypeElementParameter,
        string nestedFamilyName,
        ICollection<DiagnosticRecord> diagnostics)
    {
        var existingAssociation = familyManager.GetAssociatedFamilyParameter(nestedTypeElementParameter);
        if (existingAssociation != null)
        {
            diagnostics.Add(DiagnosticRecord.Create("PEN_SYNC_TYPE_CONTROL_REUSED", DiagnosticSeverity.Info, $"Reuse family type control parameter '{existingAssociation.Definition?.Name ?? string.Empty}'.", checked((int)nestedInstance.Id.Value)));
            return existingAssociation;
        }

        var parameterName = BuildNestedTypeControlParameterName(nestedFamilyName);
        var familyParameters = familyManager.Parameters.Cast<FamilyParameter>().ToList();
        var familyTypeControl = familyParameters.FirstOrDefault(x => string.Equals(x.Definition?.Name ?? string.Empty, parameterName, StringComparison.OrdinalIgnoreCase));
        if (familyTypeControl == null)
        {
            var nestedCategory = nestedInstance.Symbol?.Family?.FamilyCategory
                                 ?? nestedInstance.Symbol?.Category
                                 ?? throw new InvalidOperationException($"Khong resolve duoc nested family category cho '{nestedFamilyName}'.");
            familyTypeControl = familyManager.AddParameter(parameterName, GroupTypeId.General, nestedCategory, false);
            diagnostics.Add(DiagnosticRecord.Create("PEN_SYNC_TYPE_CONTROL_CREATED", DiagnosticSeverity.Info, $"Da tao family type control parameter '{parameterName}'.", checked((int)nestedInstance.Id.Value)));
        }

        if (!familyManager.CanElementParameterBeAssociated(nestedTypeElementParameter))
        {
            throw new InvalidOperationException($"Element parameter '{nestedTypeElementParameter.Definition?.Name ?? string.Empty}' khong the associate voi family parameter de dieu khien nested type.");
        }

        familyManager.AssociateElementParameterToFamilyParameter(nestedTypeElementParameter, familyTypeControl);
        diagnostics.Add(DiagnosticRecord.Create("PEN_SYNC_TYPE_CONTROL_ASSOCIATED", DiagnosticSeverity.Info, $"Da associate element parameter '{nestedTypeElementParameter.Definition?.Name ?? string.Empty}' -> family parameter '{familyTypeControl.Definition?.Name ?? string.Empty}'.", checked((int)nestedInstance.Id.Value)));
        return familyTypeControl;
    }

    private static string BuildNestedTypeControlParameterName(string nestedFamilyName)
    {
        var sanitized = new string((nestedFamilyName ?? string.Empty)
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray())
            .Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "BIM765T_NESTED_TYPE_CONTROL" : "BIM765T_" + sanitized + "_TYPE";
    }

    private static Document? ResolveProjectDocumentForReload(
        UIApplication uiapp,
        PlatformServices services,
        Document familyDoc,
        SyncPenetrationAlphaNestedTypesRequest request)
    {
        if (!request.ReloadIntoProject)
        {
            return null;
        }

        var openProjectDocs = familyDoc.Application.Documents
            .Cast<Document>()
            .Where(x => x != null && !x.IsFamilyDocument)
            .ToList();
        if (openProjectDocs.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(request.ProjectDocumentKey))
        {
            return openProjectDocs.FirstOrDefault(x => string.Equals(services.GetDocumentKey(x), request.ProjectDocumentKey.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var activeDoc = uiapp.ActiveUIDocument?.Document;
        if (activeDoc != null && !activeDoc.IsFamilyDocument)
        {
            return activeDoc;
        }

        return openProjectDocs.FirstOrDefault();
    }

    private static List<string> BuildNestedTypeSyncArtifacts(PlatformServices services, Document familyDoc, NestedPenetrationTypeSyncPlan plan, SyncPenetrationAlphaNestedTypesRequest request)
    {
        var artifacts = new List<string>
        {
            "familyDocumentKey=" + services.GetDocumentKey(familyDoc),
            "familyTitle=" + familyDoc.Title,
            "parentFamilyName=" + plan.ParentFamilyName,
            "nestedFamilyName=" + plan.NestedFamilyName,
            "parentTypeCount=" + plan.ParentTypes.Count.ToString(CultureInfo.InvariantCulture),
            "nestedInstanceCount=" + plan.NestedInstanceIds.Count.ToString(CultureInfo.InvariantCulture),
            "missingChildTypeCount=" + plan.Items.Count(x => x.RequiresCreate).ToString(CultureInfo.InvariantCulture),
            "assignCount=" + plan.Items.Count(x => x.RequiresAssign).ToString(CultureInfo.InvariantCulture),
            "reloadIntoProject=" + request.ReloadIntoProject.ToString()
        };

        if (plan.ProjectDocument != null)
        {
            artifacts.Add("projectDocumentKey=" + services.GetDocumentKey(plan.ProjectDocument));
            artifacts.Add("projectTitle=" + plan.ProjectDocument.Title);
        }

        artifacts.AddRange(plan.Items.Select(x =>
            $"map={x.ParentType.Name}|current={x.CurrentChildSymbol?.Name ?? "<none>"}|target={x.TargetTypeName}|seed={x.SeedSymbol?.Name ?? "<none>"}|requiresCreate={x.RequiresCreate}|requiresAssign={x.RequiresAssign}"));

        return artifacts;
    }

    private static ViewSchedule? FindScheduleByName(Document doc, string scheduleName)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewSchedule))
            .Cast<ViewSchedule>()
            .FirstOrDefault(x => string.Equals(x.Name ?? string.Empty, scheduleName, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddFieldIfAvailable(Document doc, ScheduleDefinition definition, IDictionary<string, ScheduleField> addedFields, string fieldName)
    {
        if (addedFields.ContainsKey(fieldName))
        {
            return;
        }

        var schedulable = definition.GetSchedulableFields()
            .FirstOrDefault(x => string.Equals(x.GetName(doc), fieldName, StringComparison.OrdinalIgnoreCase));
        if (schedulable == null)
        {
            return;
        }

        var field = definition.AddField(schedulable);
        addedFields[fieldName] = field;
    }

    private static bool TryAddFamilyFilter(ScheduleDefinition definition, IDictionary<string, ScheduleField> addedFields, string familyName)
    {
        if (addedFields.TryGetValue("Family", out var familyField))
        {
            definition.AddFilter(new ScheduleFilter(familyField.FieldId, ScheduleFilterType.Contains, familyName));
            return true;
        }

        if (addedFields.TryGetValue("Family and Type", out var familyAndTypeField))
        {
            definition.AddFilter(new ScheduleFilter(familyAndTypeField.FieldId, ScheduleFilterType.Contains, familyName));
            return true;
        }

        return false;
    }

    private static void AddSortIfFieldExists(ScheduleDefinition definition, IDictionary<string, ScheduleField> addedFields, string fieldName)
    {
        if (addedFields.TryGetValue(fieldName, out var field))
        {
            definition.AddSortGroupField(new ScheduleSortGroupField(field.FieldId));
        }
    }

    private sealed class NestedPenetrationTypeSyncPlan
    {
        internal string ParentFamilyName { get; set; } = string.Empty;
        internal string NestedFamilyName { get; set; } = string.Empty;
        internal FamilyManager FamilyManager { get; set; } = null!;
        internal FamilyType? OriginalCurrentType { get; set; }
        internal FamilySymbol? PreferredSeedSymbol { get; set; }
        internal Document? ProjectDocument { get; set; }
        internal string TypeControlParameterName { get; set; } = string.Empty;
        internal bool TypeControlParameterExists { get; set; }
        internal List<FamilyType> ParentTypes { get; } = new List<FamilyType>();
        internal List<int> NestedInstanceIds { get; } = new List<int>();
        internal List<NestedPenetrationTypeSyncPlanItem> Items { get; } = new List<NestedPenetrationTypeSyncPlanItem>();
    }

    private sealed class NestedPenetrationTypeSyncPlanItem
    {
        internal FamilyType ParentType { get; set; } = null!;
        internal FamilySymbol? CurrentChildSymbol { get; set; }
        internal FamilySymbol? ExistingTargetSymbol { get; set; }
        internal FamilySymbol? SeedSymbol { get; set; }
        internal string TargetTypeName { get; set; } = string.Empty;
        internal int ReferenceNestedInstanceId { get; set; }
        internal bool RequiresCreate => ExistingTargetSymbol == null;
        internal bool RequiresAssign => ExistingTargetSymbol == null || CurrentChildSymbol == null || ExistingTargetSymbol.Id != CurrentChildSymbol.Id;
    }

    private sealed class AlwaysOverwriteFamilyLoadOptions : IFamilyLoadOptions
    {
        private readonly bool _overwriteParameterValues;

        internal AlwaysOverwriteFamilyLoadOptions(bool overwriteParameterValues)
        {
            _overwriteParameterValues = overwriteParameterValues;
        }

        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = _overwriteParameterValues;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = _overwriteParameterValues;
            return true;
        }
    }

    private sealed class AxisClassification
    {
        internal AxisClassification(string status, string reason)
        {
            Status = status;
            Reason = reason;
        }

        internal string Status { get; }
        internal string Reason { get; }
    }

    private sealed class RoundShadowPlan
    {
        internal FamilySymbol? PreferredSymbol { get; set; }
        internal FamilyInstance? ReferenceInstance { get; set; }
        internal XYZ ReferenceAnchor { get; set; } = XYZ.Zero;
        internal List<RoundShadowPlanItem> Items { get; } = new List<RoundShadowPlanItem>();
    }

    private sealed class RoundShadowCleanupPlan
    {
        internal string JournalId { get; set; } = string.Empty;
        internal List<RoundShadowCleanupPlanItem> Items { get; } = new List<RoundShadowCleanupPlanItem>();
    }

    private sealed class RoundShadowPlanItem
    {
        internal FamilyInstance SourceInstance { get; set; } = null!;
        internal string MiiDiameter { get; set; } = string.Empty;
        internal string MiiDimLength { get; set; } = string.Empty;
        internal string MiiElementClass { get; set; } = string.Empty;
        internal string MiiElementTier { get; set; } = string.Empty;
        internal XYZ TargetAnchor { get; set; } = XYZ.Zero;
        internal bool CanCreate { get; set; }
        internal int? ExistingShadowElementId { get; set; }
        internal string Notes { get; set; } = string.Empty;
    }

    private sealed class RoundShadowCleanupPlanItem
    {
        internal int ElementId { get; set; }
        internal string FamilyName { get; set; } = string.Empty;
        internal string TypeName { get; set; } = string.Empty;
        internal string Comments { get; set; } = string.Empty;
        internal int? SourceElementId { get; set; }
        internal bool TraceMatched { get; set; }
        internal bool CanDelete { get; set; }
        internal int EstimatedDependentCount { get; set; }
        internal string Notes { get; set; } = string.Empty;
    }

    private sealed class RoundWrapperBuildPlan
    {
        internal Family SourceFamily { get; set; } = null!;
        internal string OutputDirectory { get; set; } = string.Empty;
        internal string SourceExtractedFilePath { get; set; } = string.Empty;
        internal List<RoundWrapperBuildSpec> Specs { get; } = new List<RoundWrapperBuildSpec>();
    }

    private sealed class RoundWrapperBuildSpec
    {
        internal string FamilyName { get; set; } = string.Empty;
        internal string TypeName { get; set; } = string.Empty;
        internal string FilePath { get; set; } = string.Empty;
        internal Family? ExistingProjectFamily { get; set; }
        internal string PlacementMode { get; set; } = string.Empty;
        internal string SizeLengthValueString { get; set; } = string.Empty;
        internal string SizeDiameterValueString { get; set; } = string.Empty;
    }

    private sealed class RoundWrapperGeometryPlan
    {
        internal string GeometryAxis { get; set; } = string.Empty;
        internal XYZ SketchPlaneNormal { get; set; } = XYZ.BasisX;
        internal XYZ SketchPlaneOrigin { get; set; } = XYZ.Zero;
        internal XYZ ProfileCenter { get; set; } = XYZ.Zero;
        internal XYZ ProfileXAxis { get; set; } = XYZ.BasisY;
        internal XYZ ProfileYAxis { get; set; } = XYZ.BasisZ;
        internal double Radius { get; set; }
        internal double ExtrusionDepth { get; set; }
    }

    private sealed class RoundSourceGraphicsStyleSnapshot
    {
        internal string MaterialName { get; set; } = string.Empty;
        internal string SubcategoryName { get; set; } = string.Empty;
    }
}
