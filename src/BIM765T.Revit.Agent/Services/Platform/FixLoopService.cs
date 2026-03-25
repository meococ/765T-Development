using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class FixLoopService
{
    private readonly PlatformServices _platform;
    private readonly MutationService _mutation;
    private readonly AuditService _audit;
    private readonly SheetViewManagementService _sheetView;
    private readonly TemplateSheetAnalysisService _templateSheetAnalysis;
    private readonly object _runGate = new object();
    private readonly ConcurrentDictionary<string, FixLoopRun> _runs = new ConcurrentDictionary<string, FixLoopRun>(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedPlaybook> _playbookCache = new ConcurrentDictionary<string, CachedPlaybook>(StringComparer.OrdinalIgnoreCase);
    private const int MaxRetainedRuns = 100;
    private const string DefaultPlaybookName = "default.fix_loop_v1";

    internal FixLoopService(
        PlatformServices platform,
        MutationService mutation,
        AuditService audit,
        SheetViewManagementService sheetView,
        TemplateSheetAnalysisService templateSheetAnalysis)
    {
        _platform = platform;
        _mutation = mutation;
        _audit = audit;
        _sheetView = sheetView;
        _templateSheetAnalysis = templateSheetAnalysis;
    }

    internal FixCandidateReviewResponse ReviewCandidates(UIApplication uiapp, Document doc, FixLoopPlanRequest request)
    {
        var normalized = Normalize(request);
        var built = BuildScenario(uiapp, doc, normalized);
        return new FixCandidateReviewResponse
        {
            DocumentKey = _platform.GetDocumentKey(doc),
            ScenarioName = normalized.ScenarioName,
            PlaybookName = built.Playbook.PlaybookName,
            PlaybookSource = built.PlaybookSource,
            Issues = built.Issues,
            CandidateActions = FixLoopDecisionEngine.SortActions(built.Actions),
            RecommendedActionIds = FixLoopDecisionEngine.SelectDefaultActionIds(built.Actions),
            Diagnostics = built.Diagnostics
        };
    }

    internal FixLoopRun Plan(UIApplication uiapp, Document doc, FixLoopPlanRequest request)
    {
        var normalized = Normalize(request);
        var built = BuildScenario(uiapp, doc, normalized);
        var orderedActions = FixLoopDecisionEngine.SortActions(built.Actions);
        var recommendedActionIds = FixLoopDecisionEngine.SelectDefaultActionIds(orderedActions);
        var fixableIssues = built.Issues.Count(x => !string.Equals(x.Fixability, "blocked", StringComparison.OrdinalIgnoreCase));
        var expectedIssueDelta = Math.Min(fixableIssues, FixLoopDecisionEngine.GetExpectedIssueDelta(orderedActions.Where(x => recommendedActionIds.Contains(x.ActionId, StringComparer.OrdinalIgnoreCase))));
        var run = new FixLoopRun
        {
            RunId = Guid.NewGuid().ToString("N"),
            ScenarioName = normalized.ScenarioName,
            PlaybookName = built.Playbook.PlaybookName,
            PlaybookSource = built.PlaybookSource,
            Status = built.Actions.Any(x => x.IsExecutable) ? "planned" : "blocked",
            DocumentKey = _platform.GetDocumentKey(doc),
            InputJson = JsonUtil.Serialize(normalized),
            ExpectedContextJson = JsonUtil.Serialize(_platform.BuildContextFingerprint(uiapp, doc)),
            Issues = built.Issues,
            CandidateActions = orderedActions,
            RecommendedActionIds = recommendedActionIds,
            BlockedReasons = orderedActions.Where(x => !x.IsExecutable && !string.IsNullOrWhiteSpace(x.BlockedReason)).Select(x => x.BlockedReason).Concat(built.BlockedReasons).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Diagnostics = built.Diagnostics,
            Evidence = new FixLoopEvidenceBundle
            {
                PlanSummary = built.PlanSummary,
                IssueCount = built.Issues.Count,
                ProposedActionCount = orderedActions.Count,
                AppliedActionCount = 0,
                ArtifactKeys = built.Artifacts,
                RecommendedActionIds = recommendedActionIds,
                ExpectedIssueDelta = expectedIssueDelta
            },
            Verification = new FixLoopVerificationResult
            {
                Status = "pending",
                ReviewToolName = ResolveScenarioReviewTool(normalized.ScenarioName),
                ExpectedIssueCount = built.Issues.Count,
                ExpectedIssueDelta = expectedIssueDelta
            }
        };

        StoreRun(run, evictIfNeeded: true);
        return run;
    }

    internal FixLoopRun GetRun(string runId)
    {
        if (!_runs.TryGetValue(runId, out var run))
        {
            throw new InvalidOperationException(StatusCodes.FixLoopRunNotFound);
        }

        return run;
    }

    internal ExecutionResult PreviewApply(UIApplication uiapp, Document doc, FixLoopApplyRequest request)
    {
        var run = GetRun(request.RunId);
        var selected = ResolveSelectedActions(run, request.ActionIds);
        var diagnostics = new List<DiagnosticRecord>();
        var changedIds = selected.SelectMany(x => x.ElementIds).Distinct().ToList();

        if (!request.AllowMutations)
        {
            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_MUTATIONS_NOT_ALLOWED", DiagnosticSeverity.Error, "AllowMutations=false so fix loop apply will not execute."));
        }

        if (selected.Count == 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_ACTIONS_EMPTY", DiagnosticSeverity.Error, "No candidate actions were selected for apply."));
        }

        diagnostics.AddRange(run.CandidateActions.Where(x => !x.IsExecutable && !string.IsNullOrWhiteSpace(x.BlockedReason))
            .Select(x => DiagnosticRecord.Create("FIX_LOOP_ACTION_BLOCKED", DiagnosticSeverity.Warning, $"{x.ActionId}: {x.BlockedReason}")));

        return new ExecutionResult
        {
            OperationName = ToolNames.WorkflowFixLoopApply,
            DryRun = true,
            ConfirmationRequired = true,
            ChangedIds = changedIds,
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                $"RunId={run.RunId}",
                $"Scenario={run.ScenarioName}",
                $"SelectedActions={selected.Count}",
                $"CandidateActions={run.CandidateActions.Count}"
            }
        };
    }

    internal FixLoopRun Apply(UIApplication uiapp, Document doc, FixLoopApplyRequest request)
    {
        var run = GetRun(request.RunId);
        if (string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(run.Status, "verified", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(StatusCodes.WorkflowAlreadyCompleted);
        }

        var selected = ResolveSelectedActions(run, request.ActionIds);
        var diagnostics = new List<DiagnosticRecord>(run.Diagnostics);
        var changedIds = new HashSet<int>(run.ChangedIds);
        var appliedActionIds = new List<string>(run.AppliedActionIds);
        var blockedReasons = new List<string>(run.BlockedReasons);

        foreach (var action in selected)
        {
            if (!action.IsExecutable)
            {
                if (!string.IsNullOrWhiteSpace(action.BlockedReason))
                {
                    blockedReasons.Add(action.BlockedReason);
                }

                continue;
            }

            try
            {
                var result = ExecuteAction(uiapp, doc, action);
                foreach (var id in result.ChangedIds)
                {
                    changedIds.Add(id);
                }

                diagnostics.AddRange(result.Diagnostics);
                appliedActionIds.Add(action.ActionId);
                run.Evidence.ResultPayloads.Add(JsonUtil.Serialize(result));
            }
            catch (Exception ex)
            {
                diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_ACTION_FAILED", DiagnosticSeverity.Error, $"{action.ActionId}: {ex.Message}"));
                blockedReasons.Add($"{action.ActionId}: {ex.Message}");
            }
        }

        run.AppliedActionIds = appliedActionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        run.ChangedIds = changedIds.ToList();
        run.Diagnostics = diagnostics;
        run.BlockedReasons = blockedReasons.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        run.AppliedUtc = DateTime.UtcNow;
        run.Evidence.AppliedActionCount = run.AppliedActionIds.Count;
        run.Evidence.SelectedActionIds = selected.Select(x => x.ActionId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        run.Evidence.ExpectedIssueDelta = FixLoopDecisionEngine.GetExpectedIssueDelta(selected);
        run.Status = run.AppliedActionIds.Count > 0 ? "applied" : "blocked";

        var verification = VerifyCore(uiapp, doc, run, 200);
        run.Verification = verification;
        run.VerifiedUtc = verification.VerifiedUtc;
        run.Evidence.ActualIssueDelta = verification.ActualIssueDelta;
        run.Evidence.VerificationStatus = verification.Status;
        run.Status = DeriveRunStatus(run, verification);
        StoreRun(run);
        return run;
    }

    internal FixLoopRun Verify(UIApplication uiapp, Document doc, FixLoopVerifyRequest request)
    {
        var run = GetRun(request.RunId);
        var verification = VerifyCore(uiapp, doc, run, request.MaxResidualIssues);
        run.Verification = verification;
        run.VerifiedUtc = verification.VerifiedUtc;
        run.Evidence.ActualIssueDelta = verification.ActualIssueDelta;
        run.Evidence.VerificationStatus = verification.Status;
        run.Status = DeriveRunStatus(run, verification);
        StoreRun(run);
        return run;
    }

    private FixLoopVerificationResult VerifyCore(UIApplication uiapp, Document doc, FixLoopRun run, int maxResidualIssues)
    {
        var request = JsonUtil.DeserializeRequired<FixLoopPlanRequest>(run.InputJson);
        var rebuilt = BuildScenario(uiapp, doc, request);
        var selectedActionIds = run.Evidence.SelectedActionIds ?? new List<string>();
        var selectedExpectedDelta = selectedActionIds.Count > 0
            ? FixLoopDecisionEngine.GetExpectedIssueDelta(run.CandidateActions.Where(x => selectedActionIds.Contains(x.ActionId, StringComparer.OrdinalIgnoreCase)))
            : run.Verification.ExpectedIssueDelta;
        var blockedIssueCount = run.Issues.Count(x => string.Equals(x.Fixability, "blocked", StringComparison.OrdinalIgnoreCase));
        var expectedRemainingMax = Math.Max(blockedIssueCount, run.Issues.Count - selectedExpectedDelta);
        var actualIssueCount = rebuilt.Issues.Count;
        var actualDelta = Math.Max(0, run.Issues.Count - actualIssueCount);

        var verification = new FixLoopVerificationResult
        {
            Status = actualIssueCount <= expectedRemainingMax
                ? "pass"
                : actualIssueCount < run.Issues.Count
                    ? "partial"
                    : "blocked",
            ReviewToolName = ResolveScenarioReviewTool(run.ScenarioName),
            VerifiedUtc = DateTime.UtcNow,
            ExpectedIssueCount = run.Verification.ExpectedIssueCount,
            ActualIssueCount = actualIssueCount,
            ExpectedIssueDelta = selectedExpectedDelta,
            ActualIssueDelta = actualDelta,
            ResidualIssues = rebuilt.Issues.Take(Math.Max(1, maxResidualIssues)).ToList(),
            BlockedReasons = rebuilt.Actions.Where(x => !x.IsExecutable && !string.IsNullOrWhiteSpace(x.BlockedReason)).Select(x => x.BlockedReason).Concat(rebuilt.BlockedReasons).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Diagnostics = rebuilt.Diagnostics
        };

        if (actualIssueCount > expectedRemainingMax)
        {
            verification.Diagnostics.Add(DiagnosticRecord.Create(
                "FIX_LOOP_EXPECTED_DELTA_MISMATCH",
                DiagnosticSeverity.Warning,
                $"Residual issues = {actualIssueCount}, expected <= {expectedRemainingMax}."));
        }

        return verification;
    }

    private string DeriveRunStatus(FixLoopRun run, FixLoopVerificationResult verification)
    {
        if (string.Equals(verification.Status, "pass", StringComparison.OrdinalIgnoreCase))
        {
            return "verified";
        }

        if (run.AppliedActionIds.Count > 0)
        {
            return "partial";
        }

        return "blocked";
    }

    private ExecutionResult ExecuteAction(UIApplication uiapp, Document doc, FixLoopCandidateAction action)
    {
        if (string.Equals(action.ToolName, ToolNames.ParameterBatchFillSafe, StringComparison.OrdinalIgnoreCase))
        {
            return _mutation.ExecuteBatchFillParameter(_platform, doc, JsonUtil.DeserializeRequired<BatchFillParameterRequest>(action.PayloadJson));
        }

        if (string.Equals(action.ToolName, ToolNames.ParameterCopyBetweenSafe, StringComparison.OrdinalIgnoreCase))
        {
            return _mutation.ExecuteCopyParameters(_platform, doc, JsonUtil.DeserializeRequired<CopyParametersBetweenRequest>(action.PayloadJson));
        }

        if (string.Equals(action.ToolName, ToolNames.DataImportSafe, StringComparison.OrdinalIgnoreCase))
        {
            return _mutation.ExecuteDataImport(_platform, doc, JsonUtil.DeserializeRequired<DataImportRequest>(action.PayloadJson));
        }

        if (string.Equals(action.ToolName, ToolNames.ElementDeleteSafe, StringComparison.OrdinalIgnoreCase))
        {
            var payload = JsonUtil.DeserializeRequired<DeleteElementsRequest>(action.PayloadJson);
            var impact = _mutation.AnalyzeDeleteImpact(doc, payload.ElementIds);
            if (impact.HasUnexpectedDependents && !payload.AllowDependentDeletes)
            {
                return new ExecutionResult
                {
                    OperationName = action.ToolName,
                    Diagnostics = impact.Diagnostics,
                    Artifacts = impact.Artifacts,
                    ChangedIds = new List<int>()
                };
            }

            return _mutation.ExecuteDelete(_platform, doc, payload);
        }

        if (string.Equals(action.ToolName, ToolNames.ViewSetTemplateSafe, StringComparison.OrdinalIgnoreCase))
        {
            return _sheetView.ExecuteSetViewTemplate(uiapp, _platform, doc, JsonUtil.DeserializeRequired<SetViewTemplateRequest>(action.PayloadJson));
        }

        throw new InvalidOperationException($"Unsupported fix-loop action tool: {action.ToolName}");
    }

    private List<FixLoopCandidateAction> ResolveSelectedActions(FixLoopRun run, IList<string> actionIds)
    {
        if (actionIds == null || actionIds.Count == 0)
        {
            var defaultIds = FixLoopDecisionEngine.SelectDefaultActionIds(run.CandidateActions);
            var defaultWanted = new HashSet<string>(defaultIds, StringComparer.OrdinalIgnoreCase);
            return run.CandidateActions.Where(x => x.IsExecutable && defaultWanted.Contains(x.ActionId)).ToList();
        }

        var wanted = new HashSet<string>(actionIds, StringComparer.OrdinalIgnoreCase);
        return run.CandidateActions.Where(x => wanted.Contains(x.ActionId)).ToList();
    }

    private ScenarioBuildResult BuildScenario(UIApplication uiapp, Document doc, FixLoopPlanRequest request)
    {
        var playbook = ResolvePlaybook(doc, request.PlaybookName);
        var scenarioName = NormalizeScenario(request.ScenarioName);
        switch (scenarioName)
        {
            case "parameter_hygiene":
                return BuildParameterHygiene(uiapp, doc, request, playbook.Playbook, playbook.Source);
            case "safe_cleanup":
                return BuildSafeCleanup(doc, request, playbook.Playbook, playbook.Source);
            case "view_template_compliance_assist":
                return BuildViewTemplateCompliance(doc, request, playbook.Playbook, playbook.Source);
            default:
                throw new InvalidOperationException(StatusCodes.FixLoopScenarioNotSupported);
        }
    }

    private ScenarioBuildResult BuildParameterHygiene(UIApplication uiapp, Document doc, FixLoopPlanRequest request, FixLoopPlaybook playbook, string playbookSource)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var issues = new List<FixLoopIssue>();
        var elementIds = ResolveElementScope(uiapp, doc, request, Math.Max(request.MaxIssues * 10, 500));
        var parameterNames = (request.RequiredParameterNames ?? new List<string>())
            .Concat(playbook.ParameterHygieneRules.Select(x => x.ParameterName))
            .Where(x => !string.IsNullOrWhiteSpace(x) && x != "*")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var groupedActions = new Dictionary<string, ParameterActionGroup>(StringComparer.OrdinalIgnoreCase);
        var missingRuleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ruleCandidates = playbook.ParameterHygieneRules.Select(ToParameterRuleCandidate).ToList();

        if (elementIds.Count == 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_SCOPE_EMPTY", DiagnosticSeverity.Warning, "Parameter hygiene could not resolve any elements in scope."));
        }

        if (parameterNames.Count == 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_PARAMETER_RULES_EMPTY", DiagnosticSeverity.Warning, "No RequiredParameterNames or playbook parameter rules were provided."));
        }

        foreach (var id in elementIds)
        {
            var element = doc.GetElement(new ElementId((long)id));
            if (element == null)
            {
                continue;
            }

            var categoryName = element.Category?.Name ?? string.Empty;
            var familyName = ResolveElementFamilyName(doc, element);
            var elementName = element.Name ?? string.Empty;

            foreach (var parameterName in parameterNames)
            {
                var parameter = element.LookupParameter(parameterName);
                var value = parameter != null ? (parameter.AsValueString() ?? parameter.AsString() ?? string.Empty) : string.Empty;
                if (parameter == null || string.IsNullOrWhiteSpace(value))
                {
                    var rule = FixLoopDecisionEngine.ResolveParameterRule(parameterName, categoryName, familyName, elementName, ruleCandidates);
                    var fixability = rule == null ? "blocked" : "supervised";
                    var ruleId = rule != null ? BuildParameterRuleId(parameterName, rule) : parameterName;
                    issues.Add(new FixLoopIssue
                    {
                        IssueId = $"param:{id}:{parameterName}",
                        IssueClass = "parameter_missing_or_empty",
                        Code = parameter == null ? "PARAMETER_MISSING" : "PARAMETER_EMPTY",
                        Severity = DiagnosticSeverity.Warning,
                        Message = $"Parameter `{parameterName}` is missing or empty on element {id}.",
                        ElementId = id,
                        Confidence = 1.0,
                        Fixability = fixability,
                        RuleId = ruleId
                    });

                    if (rule == null)
                    {
                        if (missingRuleKeys.Add(parameterName))
                        {
                            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_PARAMETER_RULE_MISSING", DiagnosticSeverity.Warning, $"No playbook rule found for parameter '{parameterName}' on category '{categoryName}' family '{familyName}'."));   
                        }
                    }
                    else
                    {
                        var actionGroupKey = BuildParameterActionGroupKey(parameterName, rule);
                        ParameterActionGroup group;
                        if (!groupedActions.TryGetValue(actionGroupKey, out group))
                        {
                            group = new ParameterActionGroup
                            {
                                ParameterName = parameterName,
                                Rule = rule,
                                ActionId = BuildParameterActionId(parameterName, groupedActions.Count + 1, rule)
                            };
                            groupedActions[actionGroupKey] = group;
                        }

                        group.TargetIds.Add(id);
                        group.SampleCategoryName = string.IsNullOrWhiteSpace(group.SampleCategoryName) ? categoryName : group.SampleCategoryName;
                        group.SampleFamilyName = string.IsNullOrWhiteSpace(group.SampleFamilyName) ? familyName : group.SampleFamilyName;
                        group.SampleElementName = string.IsNullOrWhiteSpace(group.SampleElementName) ? elementName : group.SampleElementName;
                    }

                    if (issues.Count >= request.MaxIssues)
                    {
                        break;
                    }
                }
            }

            if (issues.Count >= request.MaxIssues)
            {
                break;
            }
        }

        var actions = new List<FixLoopCandidateAction>();
        foreach (var group in groupedActions.Values.OrderByDescending(x => x.Rule.Priority).ThenBy(x => x.ParameterName, StringComparer.OrdinalIgnoreCase))
        {
            if (actions.Count >= request.MaxActions)
            {
                diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_ACTION_LIMIT_REACHED", DiagnosticSeverity.Info, $"Reached MaxActions={request.MaxActions}; skipped the remaining fix candidates."));
                break;
            }

            var parameterName = group.ParameterName;
            var targetIds = group.TargetIds.Distinct().ToList();
            var rule = group.Rule;
            var reviewOnly = string.Equals(rule.Recommendation, "review_only", StringComparison.OrdinalIgnoreCase);
            var decisionReason = BuildParameterDecisionReason(parameterName, group.SampleCategoryName, group.SampleFamilyName, group.SampleElementName, rule);

            FixLoopCandidateAction? action = null;
            if (string.Equals(rule.Strategy, "batch_fill", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(rule.FillValue))
            {
                action = new FixLoopCandidateAction
                {
                    ActionId = group.ActionId,
                    ToolName = ToolNames.ParameterBatchFillSafe,
                    Title = $"Fill parameter '{parameterName}' for {targetIds.Count} elements",
                    RiskLevel = string.IsNullOrWhiteSpace(rule.RiskLevel) ? "medium" : rule.RiskLevel,
                    RequiresApproval = true,
                    IsExecutable = true,
                    PayloadJson = JsonUtil.Serialize(new BatchFillParameterRequest
                    {
                        DocumentKey = _platform.GetDocumentKey(doc),
                        ParameterName = parameterName,
                        FillValue = rule.FillValue,
                        FillMode = "OnlyEmpty",
                        ElementIds = targetIds
                    }),
                    ElementIds = targetIds,
                    Verification = new FixLoopVerificationCriteria
                    {
                        ReviewToolName = ToolNames.ReviewParameterCompleteness,
                        Description = $"Re-check completeness for parameter '{parameterName}'.",
                        ExpectedIssueDelta = targetIds.Count,
                        ExpectedRemainingMax = 0,
                        VerificationPayloadJson = JsonUtil.Serialize(new ReviewParameterCompletenessRequest
                        {
                            DocumentKey = _platform.GetDocumentKey(doc),
                            ElementIds = targetIds,
                            RequiredParameterNames = new List<string> { parameterName }
                        })
                    },
                    DecisionReason = decisionReason,
                    Priority = rule.Priority,
                    IsRecommended = !reviewOnly
                };
            }
            else if (string.Equals(rule.Strategy, "copy_from_source", StringComparison.OrdinalIgnoreCase) && rule.SourceElementId > 0)
            {
                action = new FixLoopCandidateAction
                {
                    ActionId = group.ActionId,
                    ToolName = ToolNames.ParameterCopyBetweenSafe,
                    Title = $"Copy parameter '{parameterName}' from source {rule.SourceElementId}",
                    RiskLevel = string.IsNullOrWhiteSpace(rule.RiskLevel) ? "medium" : rule.RiskLevel,
                    RequiresApproval = true,
                    IsExecutable = true,
                    PayloadJson = JsonUtil.Serialize(new CopyParametersBetweenRequest
                    {
                        DocumentKey = _platform.GetDocumentKey(doc),
                        SourceElementId = rule.SourceElementId,
                        TargetElementIds = targetIds,
                        ParameterNames = new List<string> { parameterName },
                        SkipReadOnly = true
                    }),
                    ElementIds = targetIds,
                    Verification = new FixLoopVerificationCriteria
                    {
                        ReviewToolName = ToolNames.ReviewParameterCompleteness,
                        Description = $"Re-check completeness for parameter '{parameterName}'.",
                        ExpectedIssueDelta = targetIds.Count,
                        ExpectedRemainingMax = 0,
                        VerificationPayloadJson = JsonUtil.Serialize(new ReviewParameterCompletenessRequest
                        {
                            DocumentKey = _platform.GetDocumentKey(doc),
                            ElementIds = targetIds,
                            RequiredParameterNames = new List<string> { parameterName }
                        })
                    },
                    DecisionReason = decisionReason,
                    Priority = rule.Priority,
                    IsRecommended = !reviewOnly
                };
            }
            else if (string.Equals(rule.Strategy, "import_file", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(request.ImportFilePath) &&
                     !string.IsNullOrWhiteSpace(request.MatchParameterName))
            {
                action = new FixLoopCandidateAction
                {
                    ActionId = group.ActionId,
                    ToolName = ToolNames.DataImportSafe,
                    Title = $"Import values for '{parameterName}' from file",
                    RiskLevel = string.IsNullOrWhiteSpace(rule.RiskLevel) ? "high" : rule.RiskLevel,
                    RequiresApproval = true,
                    IsExecutable = true,
                    PayloadJson = JsonUtil.Serialize(new DataImportRequest
                    {
                        DocumentKey = _platform.GetDocumentKey(doc),
                        InputPath = request.ImportFilePath,
                        MatchParameterName = request.MatchParameterName,
                        Format = "csv",
                        SkipReadOnly = true
                    }),
                    ElementIds = targetIds,
                    Verification = new FixLoopVerificationCriteria
                    {
                        ReviewToolName = ToolNames.ReviewParameterCompleteness,
                        Description = $"Re-check completeness for parameter '{parameterName}'.",
                        ExpectedIssueDelta = targetIds.Count,
                        ExpectedRemainingMax = 0,
                        VerificationPayloadJson = JsonUtil.Serialize(new ReviewParameterCompletenessRequest
                        {
                            DocumentKey = _platform.GetDocumentKey(doc),
                            ElementIds = targetIds,
                            RequiredParameterNames = new List<string> { parameterName }
                        })
                    },
                    DecisionReason = decisionReason,
                    Priority = rule.Priority,
                    IsRecommended = !reviewOnly
                };
            }

            if (action == null)
            {
                diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_PARAMETER_ACTION_BLOCKED", DiagnosticSeverity.Warning, $"Rule for parameter '{parameterName}' does not have enough data to execute (strategy={rule.Strategy})."));
                continue;
            }

            actions.Add(action);
            foreach (var issue in issues.Where(x => string.Equals(x.RuleId, BuildParameterRuleId(parameterName, rule), StringComparison.OrdinalIgnoreCase)))
            {
                issue.Fixability = "supervised";
                issue.SuggestedAction = action.ActionId;
            }
        }

        actions = FixLoopDecisionEngine.SortActions(actions);

        return new ScenarioBuildResult
        {
            Playbook = playbook,
            PlaybookSource = playbookSource,
            PlanSummary = "Parameter hygiene detect -> propose fill/copy/import -> verify completeness delta.",
            Issues = issues,
            Actions = actions,
            Diagnostics = diagnostics,
            Artifacts = new List<string> { $"ScopeCount={elementIds.Count}", $"IssueCount={issues.Count}", $"ActionCount={actions.Count}" }
        };
    }

    private ScenarioBuildResult BuildSafeCleanup(Document doc, FixLoopPlanRequest request, FixLoopPlaybook playbook, string playbookSource)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var issues = new List<FixLoopIssue>();
        var actions = new List<FixLoopCandidateAction>();
        var blockedReasons = new List<string>();
        var chunkSize = Math.Max(1, playbook.SafeCleanup?.ChunkSize ?? 25);

        if (playbook.SafeCleanup == null || (!playbook.SafeCleanup.DeleteDuplicateGroups && !playbook.SafeCleanup.DeleteUnusedViews))
        {
            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_CLEANUP_RULES_EMPTY", DiagnosticSeverity.Warning, "Playbook safe_cleanup does not enable duplicate or unused-view cleanup."));
        }

        if (playbook.SafeCleanup != null && playbook.SafeCleanup.DeleteDuplicateGroups)
        {
            var duplicateAudit = _audit.AuditDuplicates(_platform, doc, new DuplicateElementsRequest
            {
                DocumentKey = _platform.GetDocumentKey(doc),
                CategoryNames = request.CategoryNames ?? new List<string>(),
                MaxResults = request.MaxIssues
            });

            var groupIndex = 0;
            foreach (var group in duplicateAudit.DuplicateGroups.Take(request.MaxIssues))
            {
                groupIndex++;
                var keeperId = group.ElementIds.FirstOrDefault();
                var deleteIds = group.ElementIds.Skip(1).Distinct().ToList();
                issues.Add(new FixLoopIssue
                {
                    IssueId = $"dup:{groupIndex}",
                    IssueClass = "duplicate_group",
                    Code = "DUPLICATE_GROUP",
                    Severity = DiagnosticSeverity.Warning,
                    Message = $"Duplicate group with {group.ElementIds.Count} elements in category '{group.Category}'.",
                    ElementId = keeperId > 0 ? (int?)keeperId : null,
                    Confidence = 0.9,
                    Fixability = deleteIds.Count > 0 ? "supervised" : "blocked",
                    RuleId = "duplicates"
                });

                if (deleteIds.Count == 0)
                {
                    blockedReasons.Add($"Duplicate group {groupIndex} did not contain delete candidates.");
                    continue;
                }

                if (actions.Count >= request.MaxActions)
                {
                    diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_ACTION_LIMIT_REACHED", DiagnosticSeverity.Info, $"Reached MaxActions={request.MaxActions}; skipped remaining cleanup actions."));
                    break;
                }

                var impact = _mutation.AnalyzeDeleteImpact(doc, deleteIds);
                var executable = !impact.HasUnexpectedDependents;
                actions.Add(new FixLoopCandidateAction
                {
                    ActionId = $"delete_duplicates:{groupIndex}",
                    ToolName = ToolNames.ElementDeleteSafe,
                    Title = $"Delete {deleteIds.Count} duplicate elements from group {groupIndex}",
                    RiskLevel = impact.HasUnexpectedDependents ? "high" : "medium",
                    RequiresApproval = true,
                    IsExecutable = executable,
                    BlockedReason = impact.HasUnexpectedDependents ? "Delete preview found dependent elements outside payload." : string.Empty,
                    PayloadJson = JsonUtil.Serialize(new DeleteElementsRequest
                    {
                        DocumentKey = _platform.GetDocumentKey(doc),
                        ElementIds = deleteIds,
                        AllowDependentDeletes = false
                    }),
                    ElementIds = deleteIds,
                    Verification = new FixLoopVerificationCriteria
                    {
                        ReviewToolName = ToolNames.AuditDuplicateElements,
                        Description = "Re-run duplicate detection for the same categories.",
                        ExpectedIssueDelta = 1,
                        ExpectedRemainingMax = Math.Max(0, duplicateAudit.DuplicateGroupCount - 1),
                        VerificationPayloadJson = JsonUtil.Serialize(new DuplicateElementsRequest
                        {
                            DocumentKey = _platform.GetDocumentKey(doc),
                            CategoryNames = request.CategoryNames ?? new List<string>(),
                            MaxResults = request.MaxIssues
                        })
                    }
                });
            }
        }

        if (playbook.SafeCleanup != null && playbook.SafeCleanup.DeleteUnusedViews && actions.Count < request.MaxActions)
        {
            var unusedViews = _audit.AuditUnusedViews(_platform, doc, new UnusedViewsRequest
            {
                DocumentKey = _platform.GetDocumentKey(doc),
                IncludeSchedules = false,
                IncludeLegends = false,
                ExcludeTemplates = true,
                MaxResults = request.MaxIssues
            });

            foreach (var view in unusedViews.UnusedViews.Take(request.MaxIssues))
            {
                issues.Add(new FixLoopIssue
                {
                    IssueId = $"unused_view:{view.ViewId}",
                    IssueClass = "unused_view",
                    Code = "UNUSED_VIEW",
                    Severity = DiagnosticSeverity.Warning,
                    Message = $"View '{view.ViewName}' is not placed on any sheet.",
                    ElementId = view.ViewId,
                    Confidence = 1.0,
                    Fixability = "supervised",
                    RuleId = "unused_views"
                });
            }

            foreach (var chunk in Chunk(unusedViews.UnusedViews.Select(x => x.ViewId).Distinct().ToList(), chunkSize))
            {
                if (actions.Count >= request.MaxActions)
                {
                    diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_ACTION_LIMIT_REACHED", DiagnosticSeverity.Info, $"Reached MaxActions={request.MaxActions}; skipped remaining cleanup actions."));
                    break;
                }

                var impact = _mutation.AnalyzeDeleteImpact(doc, chunk);
                var executable = !impact.HasUnexpectedDependents;
                actions.Add(new FixLoopCandidateAction
                {
                    ActionId = $"delete_unused_views:{actions.Count + 1}",
                    ToolName = ToolNames.ElementDeleteSafe,
                    Title = $"Delete {chunk.Count} unused views",
                    RiskLevel = impact.HasUnexpectedDependents ? "high" : "medium",
                    RequiresApproval = true,
                    IsExecutable = executable,
                    BlockedReason = impact.HasUnexpectedDependents ? "Delete preview found dependent elements outside payload." : string.Empty,
                    PayloadJson = JsonUtil.Serialize(new DeleteElementsRequest
                    {
                        DocumentKey = _platform.GetDocumentKey(doc),
                        ElementIds = chunk,
                        AllowDependentDeletes = false
                    }),
                    ElementIds = chunk,
                    Verification = new FixLoopVerificationCriteria
                    {
                        ReviewToolName = ToolNames.AuditUnusedViews,
                        Description = "Re-run unused view audit.",
                        ExpectedIssueDelta = chunk.Count,
                        ExpectedRemainingMax = Math.Max(0, unusedViews.UnusedCount - chunk.Count),
                        VerificationPayloadJson = JsonUtil.Serialize(new UnusedViewsRequest
                        {
                            DocumentKey = _platform.GetDocumentKey(doc),
                            IncludeSchedules = false,
                            IncludeLegends = false,
                            ExcludeTemplates = true,
                            MaxResults = request.MaxIssues
                        })
                    }
                });
            }
        }

        if (playbook.SafeCleanup != null && playbook.SafeCleanup.IncludeUnusedFamiliesReview)
        {
            diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_UNUSED_FAMILIES_REVIEW_ONLY", DiagnosticSeverity.Info, "Unused families remain review-only in the fix loop to avoid deleting type/library dependencies blindly."));
        }

        return new ScenarioBuildResult
        {
            Playbook = playbook,
            PlaybookSource = playbookSource,
            PlanSummary = "Safe cleanup detect -> preview dependency impact -> propose delete -> verify issue count delta.",
            Issues = issues.Take(request.MaxIssues).ToList(),
            Actions = actions.Take(request.MaxActions).ToList(),
            Diagnostics = diagnostics,
            BlockedReasons = blockedReasons,
            Artifacts = new List<string>
            {
                $"IssueCount={issues.Count}",
                $"ActionCount={actions.Count}",
                $"ChunkSize={chunkSize}"
            }
        };
    }

    private ScenarioBuildResult BuildViewTemplateCompliance(Document doc, FixLoopPlanRequest request, FixLoopPlaybook playbook, string playbookSource)
    {
        var diagnostics = new List<DiagnosticRecord>();
        var issues = new List<FixLoopIssue>();
        var actions = new List<FixLoopCandidateAction>();
        var blockedReasons = new List<string>();
        var ruleCandidates = playbook.ViewTemplateRules.Select(ToViewTemplateRuleCandidate).ToList();
        var compliance = _templateSheetAnalysis.AuditViewTemplateCompliance(_platform, doc, new ViewTemplateComplianceRequest
        {
            DocumentKey = _platform.GetDocumentKey(doc),
            IncludeTemplateHealth = true,
            IncludeSheetOrganization = true,
            IncludeChainAnalysis = true,
            NamingPattern = string.Empty
        });

        diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_TEMPLATE_COMPLIANCE_BASELINE", DiagnosticSeverity.Info, $"Baseline compliance score {compliance.OverallScore} ({compliance.OverallGrade})."));

        var scopeViews = new List<View>();
        var sheetNumberByViewId = new Dictionary<long, string>();
        if (request.ViewId.HasValue && request.ViewId.Value > 0)
        {
            var singleView = doc.GetElement(new ElementId((long)request.ViewId.Value)) as View;
            if (singleView != null && !singleView.IsTemplate)
            {
                scopeViews.Add(singleView);
            }
        }
        else if (request.SheetId.HasValue && request.SheetId.Value > 0)
        {
            var sheet = doc.GetElement(new ElementId((long)request.SheetId.Value)) as ViewSheet;
            if (sheet != null)
            {
                foreach (var viewportId in sheet.GetAllViewports())
                {
                    var viewport = doc.GetElement(viewportId) as Viewport;
                    var view = viewport != null ? doc.GetElement(viewport.ViewId) as View : null;
                    if (view != null && !view.IsTemplate)
                    {
                        scopeViews.Add(view);
                        sheetNumberByViewId[view.Id.Value] = sheet.SheetNumber ?? string.Empty;
                    }
                }
            }
        }
        else
        {
            var viewIdsOnSheets = new HashSet<long>();
            foreach (var viewport in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            {
                viewIdsOnSheets.Add(viewport.ViewId.Value);
                var sheet = doc.GetElement(viewport.SheetId) as ViewSheet;
                if (sheet != null)
                {
                    sheetNumberByViewId[viewport.ViewId.Value] = sheet.SheetNumber ?? string.Empty;
                }
            }

            scopeViews = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(x => !x.IsTemplate && viewIdsOnSheets.Contains(x.Id.Value))
                .ToList();
        }

        var maxIssues = Math.Max(1, request.MaxIssues);
        foreach (var view in scopeViews.OrderBy(x => x.Name ?? string.Empty).Take(maxIssues))
        {
            var currentTemplate = view.ViewTemplateId != ElementId.InvalidElementId ? doc.GetElement(view.ViewTemplateId) as View : null;
            var currentTemplateName = currentTemplate != null ? currentTemplate.Name ?? string.Empty : string.Empty;
            string sheetNumber;
            sheetNumberByViewId.TryGetValue(view.Id.Value, out sheetNumber);

            var matchedCandidate = FixLoopDecisionEngine.ResolveViewTemplateRule(
                view.ViewType.ToString(),
                view.Name ?? string.Empty,
                currentTemplateName,
                sheetNumber ?? string.Empty,
                ruleCandidates);
            var matchedRule = matchedCandidate != null ? ToViewTemplatePlaybookRule(matchedCandidate) : null;
            var targetTemplate = matchedRule != null ? ResolveTargetTemplate(doc, matchedRule) : null;

            if (matchedRule == null)
            {
                issues.Add(new FixLoopIssue
                {
                    IssueId = $"view_template_rule:{view.Id.Value}",
                    IssueClass = "view_template_rule_missing",
                    Code = "VIEW_TEMPLATE_RULE_MISSING",
                    Severity = DiagnosticSeverity.Info,
                    Message = $"No playbook template rule matched view '{view.Name}'.",
                    ElementId = checked((int)view.Id.Value),
                    Confidence = 0.7,
                    Fixability = "blocked"
                });
                blockedReasons.Add($"No playbook rule matched view '{view.Name}'.");
                continue;
            }

            var needsTemplate = currentTemplate == null;
            var mismatchedTemplate = currentTemplate != null && targetTemplate != null && !string.Equals(currentTemplateName, targetTemplate.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!needsTemplate && !mismatchedTemplate)
            {
                continue;
            }

            var issue = new FixLoopIssue
            {
                IssueId = $"view_template:{view.Id.Value}",
                IssueClass = needsTemplate ? "view_missing_template" : "view_template_mismatch",
                Code = needsTemplate ? "VIEW_MISSING_TEMPLATE" : "VIEW_TEMPLATE_MISMATCH",
                Severity = DiagnosticSeverity.Warning,
                Message = needsTemplate
                    ? $"View '{view.Name}' has no template assigned."
                    : $"View '{view.Name}' uses template '{currentTemplateName}' but playbook expects '{matchedRule.TargetTemplateName}'.",
                ElementId = checked((int)view.Id.Value),
                Confidence = 0.95,
                Fixability = targetTemplate != null ? "supervised" : "blocked",
                RuleId = matchedRule.ViewType
            };

            if (targetTemplate == null)
            {
                blockedReasons.Add($"Target template '{matchedRule.TargetTemplateName}' was not found for view '{view.Name}'.");
                issues.Add(issue);
                continue;
            }

            if (actions.Count >= request.MaxActions)
            {
                diagnostics.Add(DiagnosticRecord.Create("FIX_LOOP_ACTION_LIMIT_REACHED", DiagnosticSeverity.Info, $"Reached MaxActions={request.MaxActions}; skipped remaining template actions."));
                issues.Add(issue);
                continue;
            }

            var actionId = $"set_template:{view.Id.Value}";
            issue.SuggestedAction = actionId;
            issues.Add(issue);
            var reviewOnly = string.Equals(matchedRule.Recommendation, "review_only", StringComparison.OrdinalIgnoreCase);
            actions.Add(new FixLoopCandidateAction
            {
                ActionId = actionId,
                ToolName = ToolNames.ViewSetTemplateSafe,
                Title = $"Apply template '{targetTemplate.Name}' to view '{view.Name}'",
                RiskLevel = string.IsNullOrWhiteSpace(matchedRule.RiskLevel) ? "low" : matchedRule.RiskLevel,
                RequiresApproval = true,
                IsExecutable = true,
                PayloadJson = JsonUtil.Serialize(new SetViewTemplateRequest
                {
                    DocumentKey = _platform.GetDocumentKey(doc),
                    ViewId = checked((int)view.Id.Value),
                    TemplateId = checked((int)targetTemplate.Id.Value),
                    TemplateName = targetTemplate.Name ?? string.Empty,
                    RemoveTemplate = false
                }),
                ElementIds = new List<int> { checked((int)view.Id.Value) },
                Verification = new FixLoopVerificationCriteria
                {
                    ReviewToolName = ToolNames.AuditViewTemplateCompliance,
                    Description = "Re-check view template compliance after assignment.",
                    ExpectedIssueDelta = 1,
                    ExpectedRemainingMax = Math.Max(0, issues.Count - 1),
                    VerificationPayloadJson = JsonUtil.Serialize(new ViewTemplateComplianceRequest
                    {
                        DocumentKey = _platform.GetDocumentKey(doc),
                        IncludeTemplateHealth = true,
                        IncludeSheetOrganization = true,
                        IncludeChainAnalysis = true,
                        NamingPattern = string.Empty
                    })
                },
                DecisionReason = BuildViewTemplateDecisionReason(view, currentTemplateName, sheetNumber ?? string.Empty, matchedRule),
                Priority = matchedRule.Priority,
                IsRecommended = !reviewOnly
            });
        }

        actions = FixLoopDecisionEngine.SortActions(actions);

        return new ScenarioBuildResult
        {
            Playbook = playbook,
            PlaybookSource = playbookSource,
            PlanSummary = "Template compliance detect -> propose set_template actions -> verify residual template issues.",
            Issues = issues,
            Actions = actions,
            Diagnostics = diagnostics,
            BlockedReasons = blockedReasons,
            Artifacts = new List<string>
            {
                $"ComplianceScore={compliance.OverallScore}",
                $"ComplianceGrade={compliance.OverallGrade}",
                $"ScopeViews={scopeViews.Count}",
                $"ActionCount={actions.Count}"
            }
        };
    }

    private FixLoopPlanRequest Normalize(FixLoopPlanRequest request)
    {
        request = request ?? new FixLoopPlanRequest();
        request.ScenarioName = NormalizeScenario(request.ScenarioName);
        request.PlaybookName = string.IsNullOrWhiteSpace(request.PlaybookName) ? DefaultPlaybookName : request.PlaybookName.Trim();
        request.MaxIssues = Math.Max(1, request.MaxIssues);
        request.MaxActions = Math.Max(1, request.MaxActions);
        request.ElementIds = request.ElementIds ?? new List<int>();
        request.CategoryNames = request.CategoryNames ?? new List<string>();
        request.RequiredParameterNames = request.RequiredParameterNames ?? new List<string>();
        request.ImportFilePath = request.ImportFilePath ?? string.Empty;
        request.MatchParameterName = request.MatchParameterName ?? string.Empty;
        return request;
    }

    private static string NormalizeScenario(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(scenarioName))
        {
            return "parameter_hygiene";
        }

        var normalized = scenarioName.Trim().Replace('-', '_').Replace(' ', '_').ToLowerInvariant();
        if (normalized == "view_template_compliance")
        {
            return "view_template_compliance_assist";
        }

        return normalized;
    }

    private List<int> ResolveElementScope(UIApplication uiapp, Document doc, FixLoopPlanRequest request, int maxElements)
    {
        var ids = new HashSet<int>();

        if (request.ElementIds != null)
        {
            foreach (var id in request.ElementIds.Where(x => x > 0))
            {
                ids.Add(id);
                if (ids.Count >= maxElements)
                {
                    return ids.ToList();
                }
            }
        }

        if (ids.Count == 0 && request.UseCurrentSelectionWhenEmpty && uiapp.ActiveUIDocument != null && uiapp.ActiveUIDocument.Document.Equals(doc))
        {
            foreach (var id in uiapp.ActiveUIDocument.Selection.GetElementIds().Select(x => checked((int)x.Value)))
            {
                ids.Add(id);
                if (ids.Count >= maxElements)
                {
                    return ids.ToList();
                }
            }
        }

        if (ids.Count == 0)
        {
            IEnumerable<Element> collector = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements();
            if (request.CategoryNames != null && request.CategoryNames.Count > 0)
            {
                collector = collector.Where(x => x.Category != null && request.CategoryNames.Any(c => string.Equals(c, x.Category.Name, StringComparison.OrdinalIgnoreCase)));
            }

            foreach (var element in collector)
            {
                ids.Add(checked((int)element.Id.Value));
                if (ids.Count >= maxElements)
                {
                    break;
                }
            }
        }

        return ids.ToList();
    }

    private ResolvedPlaybook ResolvePlaybook(Document doc, string playbookName)
    {
        var normalizedName = string.IsNullOrWhiteSpace(playbookName) ? DefaultPlaybookName : playbookName.Trim();
        var appDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BIM765T.Revit.Agent", "playbooks");
        var roots = new List<string> { appDataRoot };
        string repoRoot;
        if (TryFindRepoRoot(AppDomain.CurrentDomain.BaseDirectory, out repoRoot))
        {
            roots.Add(Path.Combine(repoRoot, "docs", "agent", "playbooks"));
        }

        foreach (var root in roots)
        {
            foreach (var candidatePath in FixLoopDecisionEngine.GetProjectOverridePathCandidates(root, normalizedName, doc.Title ?? string.Empty, doc.PathName ?? string.Empty))
            {
                if (!File.Exists(candidatePath))
                {
                    continue;
                }

                var lastWriteUtc = File.GetLastWriteTimeUtc(candidatePath);
                CachedPlaybook cached;
                if (_playbookCache.TryGetValue(candidatePath, out cached) && cached.LastWriteUtc == lastWriteUtc)
                {
                    return new ResolvedPlaybook
                    {
                        Source = cached.Source,
                        Playbook = cached.Playbook
                    };
                }

                var loaded = new CachedPlaybook
                {
                    LastWriteUtc = lastWriteUtc,
                    Source = candidatePath,
                    Playbook = JsonUtil.DeserializeRequired<FixLoopPlaybook>(File.ReadAllText(candidatePath))
                };
                _playbookCache[candidatePath] = loaded;
                return new ResolvedPlaybook
                {
                    Source = loaded.Source,
                    Playbook = loaded.Playbook
                };
            }
        }

        return new ResolvedPlaybook
        {
            Source = "code_default",
            Playbook = BuildCodeDefaultPlaybook(normalizedName)
        };
    }

    private static bool TryFindRepoRoot(string startDirectory, out string repoRoot)
    {
        var dir = new DirectoryInfo(startDirectory);
        while (dir != null)
        {
            var sln = Path.Combine(dir.FullName, "BIM765T.Revit.Agent.sln");
            if (File.Exists(sln))
            {
                repoRoot = dir.FullName;
                return true;
            }

            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) && Directory.Exists(Path.Combine(dir.FullName, "docs", "agent")))
            {
                repoRoot = dir.FullName;
                return true;
            }

            dir = dir.Parent;
        }

        repoRoot = string.Empty;
        return false;
    }

    private static FixLoopPlaybook BuildCodeDefaultPlaybook(string playbookName)
    {
        return new FixLoopPlaybook
        {
            PlaybookName = string.IsNullOrWhiteSpace(playbookName) ? DefaultPlaybookName : playbookName,
            Version = "1.0",
            ParameterHygieneRules = new List<ParameterHygieneRule>
            {
                new ParameterHygieneRule { ParameterName = "Comments", Strategy = "batch_fill", FillValue = "REVIEW_REQUIRED", RiskLevel = "medium", Priority = 30, Recommendation = "recommended" },
                new ParameterHygieneRule { ParameterName = "Mark", Strategy = "import_file", RiskLevel = "high", Priority = 10, Recommendation = "review_only" },
                new ParameterHygieneRule { ParameterName = "Type Comments", Strategy = "batch_fill", FillValue = "REVIEW_REQUIRED", RiskLevel = "medium", Priority = 20, Recommendation = "recommended" }
            },
            SafeCleanup = new SafeCleanupPlaybook
            {
                DeleteDuplicateGroups = true,
                DeleteUnusedViews = true,
                IncludeUnusedFamiliesReview = true,
                ChunkSize = 25
            },
            ViewTemplateRules = new List<ViewTemplatePlaybookRule>
            {
                new ViewTemplatePlaybookRule { ViewType = "FloorPlan", TargetTemplateName = "PLAN", TargetTemplateNameContains = "PLAN", RiskLevel = "low", Priority = 20, Recommendation = "recommended" },
                new ViewTemplatePlaybookRule { ViewType = "CeilingPlan", TargetTemplateName = "RCP", TargetTemplateNameContains = "RCP", RiskLevel = "low", Priority = 20, Recommendation = "recommended" },
                new ViewTemplatePlaybookRule { ViewType = "Section", TargetTemplateName = "SECTION", TargetTemplateNameContains = "SECTION", RiskLevel = "low", Priority = 20, Recommendation = "recommended" },
                new ViewTemplatePlaybookRule { ViewType = "Elevation", TargetTemplateName = "ELEV", TargetTemplateNameContains = "ELEV", RiskLevel = "low", Priority = 20, Recommendation = "recommended" },
                new ViewTemplatePlaybookRule { ViewType = "ThreeD", TargetTemplateName = "3D", TargetTemplateNameContains = "3D", RiskLevel = "low", Priority = 20, Recommendation = "recommended" }
            }
        };
    }

    private static string BuildParameterRuleId(string parameterName, ParameterRuleCandidate rule)
    {
        return string.Join("|", new[]
        {
            parameterName ?? string.Empty,
            rule.CategoryName ?? string.Empty,
            rule.FamilyName ?? string.Empty,
            rule.ElementNameContains ?? string.Empty,
            rule.Strategy ?? string.Empty
        });
    }

    private static string BuildParameterActionGroupKey(string parameterName, ParameterRuleCandidate rule)
    {
        return string.Join("|", new[]
        {
            parameterName ?? string.Empty,
            rule.Strategy ?? string.Empty,
            rule.FillValue ?? string.Empty,
            rule.SourceElementId.ToString(CultureInfo.InvariantCulture),
            rule.RiskLevel ?? string.Empty,
            rule.Recommendation ?? string.Empty,
            rule.Priority.ToString(CultureInfo.InvariantCulture)
        });
    }

    private static string BuildParameterActionId(string parameterName, int index, ParameterRuleCandidate rule)
    {
        var prefix = string.Equals(rule.Strategy, "copy_from_source", StringComparison.OrdinalIgnoreCase)
            ? "param_copy"
            : string.Equals(rule.Strategy, "import_file", StringComparison.OrdinalIgnoreCase)
                ? "param_import"
                : "param_fill";
        return $"{prefix}:{parameterName}:{index}";
    }

    private static string BuildParameterDecisionReason(string parameterName, string categoryName, string familyName, string elementName, ParameterRuleCandidate rule)
    {
        var fragments = new List<string> { $"Matched rule for parameter '{parameterName}'" };
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            fragments.Add($"category '{categoryName}'");
        }

        if (!string.IsNullOrWhiteSpace(familyName))
        {
            fragments.Add($"family '{familyName}'");
        }

        if (!string.IsNullOrWhiteSpace(rule.ElementNameContains))
        {
            fragments.Add($"element-name contains '{rule.ElementNameContains}'");
        }
        else if (!string.IsNullOrWhiteSpace(elementName))
        {
            fragments.Add($"sample element '{elementName}'");
        }

        return string.Join(", ", fragments) + ".";
    }

    private static string BuildViewTemplateDecisionReason(View view, string currentTemplateName, string sheetNumber, ViewTemplatePlaybookRule rule)
    {
        var fragments = new List<string> { $"Matched template rule for view type '{view.ViewType}'" };
        if (!string.IsNullOrWhiteSpace(rule.ViewNameContains))
        {
            fragments.Add($"view-name contains '{rule.ViewNameContains}'");
        }

        if (!string.IsNullOrWhiteSpace(rule.CurrentTemplateNameContains))
        {
            fragments.Add($"current template contains '{rule.CurrentTemplateNameContains}'");
        }

        if (!string.IsNullOrWhiteSpace(rule.SheetNumberPrefix))
        {
            fragments.Add($"sheet prefix '{rule.SheetNumberPrefix}'");
        }
        else if (!string.IsNullOrWhiteSpace(sheetNumber))
        {
            fragments.Add($"sheet '{sheetNumber}'");
        }

        if (!string.IsNullOrWhiteSpace(currentTemplateName))
        {
            fragments.Add($"current template '{currentTemplateName}'");
        }

        return string.Join(", ", fragments) + ".";
    }

    private static string ResolveElementFamilyName(Document doc, Element element)
    {
        if (element is FamilyInstance familyInstance && familyInstance.Symbol != null)
        {
            return familyInstance.Symbol.FamilyName ?? string.Empty;
        }

        if (element.GetTypeId() != ElementId.InvalidElementId)
        {
            var type = doc.GetElement(element.GetTypeId()) as ElementType;
            if (type is FamilySymbol symbol)
            {
                return symbol.FamilyName ?? string.Empty;
            }

            if (type != null)
            {
                var familyParameter = type.LookupParameter("Family Name");
                if (familyParameter != null)
                {
                    return familyParameter.AsString() ?? familyParameter.AsValueString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }

    private static ParameterRuleCandidate ToParameterRuleCandidate(ParameterHygieneRule rule)
    {
        return new ParameterRuleCandidate
        {
            ParameterName = string.IsNullOrWhiteSpace(rule.ParameterName) ? "*" : rule.ParameterName,
            CategoryName = rule.CategoryName ?? string.Empty,
            FamilyName = rule.FamilyName ?? string.Empty,
            ElementNameContains = rule.ElementNameContains ?? string.Empty,
            Strategy = rule.Strategy ?? string.Empty,
            FillValue = rule.FillValue ?? string.Empty,
            SourceElementId = rule.SourceElementId,
            RiskLevel = rule.RiskLevel ?? string.Empty,
            Recommendation = rule.Recommendation ?? string.Empty,
            Priority = rule.Priority
        };
    }

    private static ViewTemplateRuleCandidate ToViewTemplateRuleCandidate(ViewTemplatePlaybookRule rule)
    {
        return new ViewTemplateRuleCandidate
        {
            ViewType = rule.ViewType ?? string.Empty,
            ViewNameContains = rule.ViewNameContains ?? string.Empty,
            CurrentTemplateNameContains = rule.CurrentTemplateNameContains ?? string.Empty,
            SheetNumberPrefix = rule.SheetNumberPrefix ?? string.Empty,
            TargetTemplateName = rule.TargetTemplateName ?? string.Empty,
            TargetTemplateNameContains = rule.TargetTemplateNameContains ?? string.Empty,
            RiskLevel = rule.RiskLevel ?? string.Empty,
            Recommendation = rule.Recommendation ?? string.Empty,
            Priority = rule.Priority
        };
    }

    private static ViewTemplatePlaybookRule ToViewTemplatePlaybookRule(ViewTemplateRuleCandidate rule)
    {
        return new ViewTemplatePlaybookRule
        {
            ViewType = rule.ViewType,
            ViewNameContains = rule.ViewNameContains,
            CurrentTemplateNameContains = rule.CurrentTemplateNameContains,
            SheetNumberPrefix = rule.SheetNumberPrefix,
            TargetTemplateName = rule.TargetTemplateName,
            TargetTemplateNameContains = rule.TargetTemplateNameContains,
            RiskLevel = rule.RiskLevel,
            Recommendation = rule.Recommendation,
            Priority = rule.Priority
        };
    }

    private View? ResolveTargetTemplate(Document doc, ViewTemplatePlaybookRule resolvedRule)
    {
        var templates = new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(x => x.IsTemplate)
            .ToList();

        var target = templates.FirstOrDefault(x => string.Equals(x.Name, resolvedRule.TargetTemplateName, StringComparison.OrdinalIgnoreCase));
        if (target != null)
        {
            return target;
        }

        if (!string.IsNullOrWhiteSpace(resolvedRule.TargetTemplateNameContains))
        {
            target = templates.FirstOrDefault(x => (x.Name ?? string.Empty).IndexOf(resolvedRule.TargetTemplateNameContains, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        return target;
    }

    private static List<List<int>> Chunk(List<int> ids, int chunkSize)
    {
        var result = new List<List<int>>();
        if (ids == null || ids.Count == 0)
        {
            return result;
        }

        for (var index = 0; index < ids.Count; index += chunkSize)
        {
            result.Add(ids.Skip(index).Take(chunkSize).ToList());
        }

        return result;
    }

    private static string ResolveScenarioReviewTool(string scenarioName)
    {
        switch (NormalizeScenario(scenarioName))
        {
            case "parameter_hygiene":
                return ToolNames.ReviewParameterCompleteness;
            case "safe_cleanup":
                return ToolNames.AuditComplianceReport;
            case "view_template_compliance_assist":
                return ToolNames.AuditViewTemplateCompliance;
            default:
                return ToolNames.ReviewFixCandidates;
        }
    }

    private void StoreRun(FixLoopRun run, bool evictIfNeeded = false)
    {
        lock (_runGate)
        {
            _runs[run.RunId] = run;
            if (evictIfNeeded)
            {
                EvictCompletedRunsIfNeededCore();
            }
        }
    }

    private void EvictCompletedRunsIfNeededCore()
    {
        if (_runs.Count <= MaxRetainedRuns)
        {
            return;
        }

        var overflow = _runs.Count - MaxRetainedRuns;
        foreach (var item in _runs.Values.OrderBy(x => x.PlannedUtc).Take(overflow).ToList())
        {
            FixLoopRun removed;
            _runs.TryRemove(item.RunId, out removed);
        }
    }
}

internal sealed class ScenarioBuildResult
{
    internal FixLoopPlaybook Playbook { get; set; } = new FixLoopPlaybook();
    internal string PlaybookSource { get; set; } = string.Empty;
    internal string PlanSummary { get; set; } = string.Empty;
    internal List<FixLoopIssue> Issues { get; set; } = new List<FixLoopIssue>();
    internal List<FixLoopCandidateAction> Actions { get; set; } = new List<FixLoopCandidateAction>();
    internal List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();
    internal List<string> BlockedReasons { get; set; } = new List<string>();
    internal List<string> Artifacts { get; set; } = new List<string>();
}

internal sealed class ResolvedPlaybook
{
    internal FixLoopPlaybook Playbook { get; set; } = new FixLoopPlaybook();
    internal string Source { get; set; } = string.Empty;
}

internal sealed class CachedPlaybook
{
    internal DateTime LastWriteUtc { get; set; }
    internal FixLoopPlaybook Playbook { get; set; } = new FixLoopPlaybook();
    internal string Source { get; set; } = string.Empty;
}

[DataContract]
internal sealed class FixLoopPlaybook
{
    [DataMember(Order = 1)]
    public string PlaybookName { get; set; } = "default.fix_loop_v1";

    [DataMember(Order = 2)]
    public string Version { get; set; } = "1.0";

    [DataMember(Order = 3)]
    public List<ParameterHygieneRule> ParameterHygieneRules { get; set; } = new List<ParameterHygieneRule>();

    [DataMember(Order = 4)]
    public SafeCleanupPlaybook SafeCleanup { get; set; } = new SafeCleanupPlaybook();

    [DataMember(Order = 5)]
    public List<ViewTemplatePlaybookRule> ViewTemplateRules { get; set; } = new List<ViewTemplatePlaybookRule>();
}

[DataContract]
internal sealed class ParameterHygieneRule
{
    [DataMember(Order = 1)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Strategy { get; set; } = "batch_fill";

    [DataMember(Order = 3)]
    public string FillValue { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int SourceElementId { get; set; }

    [DataMember(Order = 5)]
    public string RiskLevel { get; set; } = "medium";

    [DataMember(Order = 6)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string ElementNameContains { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public int Priority { get; set; }

    [DataMember(Order = 10)]
    public string Recommendation { get; set; } = "recommended";
}

[DataContract]
internal sealed class SafeCleanupPlaybook
{
    [DataMember(Order = 1)]
    public bool DeleteDuplicateGroups { get; set; } = true;

    [DataMember(Order = 2)]
    public bool DeleteUnusedViews { get; set; } = true;

    [DataMember(Order = 3)]
    public bool IncludeUnusedFamiliesReview { get; set; } = true;

    [DataMember(Order = 4)]
    public int ChunkSize { get; set; } = 25;
}

[DataContract]
internal sealed class ViewTemplatePlaybookRule
{
    [DataMember(Order = 1)]
    public string ViewType { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ViewNameContains { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string TargetTemplateName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string TargetTemplateNameContains { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string RiskLevel { get; set; } = "low";

    [DataMember(Order = 6)]
    public string CurrentTemplateNameContains { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string SheetNumberPrefix { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public int Priority { get; set; }

    [DataMember(Order = 9)]
    public string Recommendation { get; set; } = "recommended";
}

internal sealed class ParameterActionGroup
{
    internal string ActionId { get; set; } = string.Empty;
    internal string ParameterName { get; set; } = string.Empty;
    internal ParameterRuleCandidate Rule { get; set; } = new ParameterRuleCandidate();
    internal List<int> TargetIds { get; } = new List<int>();
    internal string SampleCategoryName { get; set; } = string.Empty;
    internal string SampleFamilyName { get; set; } = string.Empty;
    internal string SampleElementName { get; set; } = string.Empty;
}
