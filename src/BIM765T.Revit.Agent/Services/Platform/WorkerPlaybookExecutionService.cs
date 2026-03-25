using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure;
using BIM765T.Revit.Agent.Infrastructure.Time;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.Copilot.Core.Brain;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class WorkerPlaybookExecutionService
{
    private const string PendingPrefix = "playbook.execute::";

    private readonly PlatformServices _platform;
    private readonly PlaybookLoaderService _playbooks;
    private readonly ISystemClock _clock;

    internal WorkerPlaybookExecutionService(PlatformServices platform, PlaybookLoaderService playbooks, ISystemClock clock)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _playbooks = playbooks ?? throw new ArgumentNullException(nameof(playbooks));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    internal static string BuildPendingToolName(string playbookId)
    {
        return PendingPrefix + (playbookId ?? string.Empty);
    }

    internal bool TryPrepare(
        UIApplication uiapp,
        Document doc,
        ToolRequestEnvelope source,
        WorkerConversationSessionState session,
        string userMessage,
        PlaybookRecommendation recommendation,
        PlaybookPreviewResponse preview,
        out WorkerPlaybookPreviewResult result)
    {
        result = new WorkerPlaybookPreviewResult();
        if (recommendation == null || string.IsNullOrWhiteSpace(recommendation.PlaybookId))
        {
            return false;
        }

        if (!_playbooks.TryGet(recommendation.PlaybookId, out var playbook) || !Supports(playbook))
        {
            return false;
        }

        var plan = BuildSheetPlan(doc, userMessage, playbook, preview);
        var previewCard = CreateCard(
            ToolNames.PlaybookPreview,
            plan.IsExecutable ? StatusCodes.ReadSucceeded : StatusCodes.InvalidRequest,
            plan.IsExecutable,
            plan.PreviewSummary,
            plan,
            plan.IsExecutable ? WorkerStages.Planning : WorkerStages.Recovery,
            100,
            "Worker resolve standards + level scope + preflight truoc khi mutate sheet package.",
            Math.Max(0.78d, recommendation.Confidence),
            plan.RecoveryHints,
            WorkerExecutionTiers.Tier0,
            false);

        if (!plan.IsExecutable)
        {
            result = WorkerPlaybookPreviewResult.Blocked(
                plan.PreviewSummary,
                previewCard,
                new WorkerActionCard
                {
                    ActionKind = WorkerActionKinds.Clarify,
                    Title = "Clarify sheet package scope",
                    Summary = plan.PreviewSummary,
                    ToolName = playbook.PlaybookId,
                    IsPrimary = true,
                    ExecutionTier = WorkerExecutionTiers.Tier0,
                    WhyThisAction = "Sheet package phai ro level, naming, title block, va template truoc khi authoring.",
                    Confidence = Math.Max(0.72d, recommendation.Confidence),
                    RecoveryHint = plan.RecoveryHints.FirstOrDefault() ?? "Bo sung levels/standards roi preview lai.",
                    AutoExecutionEligible = false
                });
            return true;
        }

        var pendingSummary = BuildPendingSummary(playbook, plan);
        result = new WorkerPlaybookPreviewResult
        {
            Handled = true,
            AwaitingApproval = true,
            ResponseText = $"Em da lap plan authoring cho {plan.Levels.Count} sheet package. {pendingSummary}",
            PendingApproval = new WorkerPendingApprovalState
            {
                PendingActionId = Guid.NewGuid().ToString("N"),
                ToolName = BuildPendingToolName(playbook.PlaybookId),
                PayloadJson = JsonUtil.Serialize(plan),
                Summary = pendingSummary,
                ExpiresUtc = _clock.UtcNow.AddMinutes(Math.Max(10, _platform.Settings.ApprovalTokenTtlMinutes)),
                AutoExecutionEligible = true,
                ExpectedContextJson = JsonUtil.Serialize(_platform.BuildContextFingerprint(uiapp, doc))
            },
            ToolCards = new List<WorkerToolCard> { previewCard },
            ActionCards = new List<WorkerActionCard>
            {
                new WorkerActionCard
                {
                    ActionKind = WorkerActionKinds.Approve,
                    Title = "Approve playbook package",
                    Summary = pendingSummary,
                    ToolName = BuildPendingToolName(playbook.PlaybookId),
                    RequiresApproval = true,
                    IsPrimary = true,
                    ExecutionTier = WorkerExecutionTiers.Tier1,
                    WhyThisAction = "Sheet package la mutation low-risk, chunk theo level, nhung van can 1 checkpoint orchestration.",
                    Confidence = Math.Max(0.82d, recommendation.Confidence),
                    RecoveryHint = "Neu scope hoac standards doi, preview lai playbook truoc khi execute.",
                    AutoExecutionEligible = true
                },
                new WorkerActionCard
                {
                    ActionKind = WorkerActionKinds.Reject,
                    Title = "Reject playbook package",
                    Summary = "Huy preview nay neu muon doi levels, template, hoac standards.",
                    ToolName = BuildPendingToolName(playbook.PlaybookId),
                    ExecutionTier = WorkerExecutionTiers.Tier1,
                    WhyThisAction = "Checkpoint de tranh create hang loat khi standards/context chua chac.",
                    Confidence = 0.97d,
                    RecoveryHint = "Anh co the noi scope moi, vi du 'tao sheet A tang 2-4'.",
                    AutoExecutionEligible = false
                }
            }
        };
        return true;
    }

    internal bool TryExecutePending(
        UIApplication uiapp,
        Document doc,
        ToolRequestEnvelope source,
        WorkerConversationSessionState session,
        WorkerPendingApprovalState pending,
        out WorkerPlaybookExecutionResult result)
    {
        result = new WorkerPlaybookExecutionResult();
        if (pending == null || string.IsNullOrWhiteSpace(pending.ToolName) || !pending.ToolName.StartsWith(PendingPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var playbookId = pending.ToolName.Substring(PendingPrefix.Length);
        if (!_playbooks.TryGet(playbookId, out var playbook) || !Supports(playbook))
        {
            result = WorkerPlaybookExecutionResult.Failed(
                $"Khong load duoc playbook runtime cho {playbookId}.",
                CreateCard(
                    pending.ToolName,
                    StatusCodes.InternalError,
                    false,
                    $"Pending approval tham chieu playbook {playbookId} nhung runtime khong resolve duoc.",
                    pending,
                    WorkerStages.Recovery,
                    100,
                    "Execute fail-closed neu runtime playbook khong hop le.",
                    0.38d,
                    new[] { "Kiem tra pack playbooks va export lai catalog." },
                    WorkerExecutionTiers.Tier1,
                    false));
            return true;
        }

        result = ExecuteSheetPlan(uiapp, doc, source, session, playbook, pending);
        return true;
    }
    private WorkerPlaybookExecutionResult ExecuteSheetPlan(
        UIApplication uiapp,
        Document doc,
        ToolRequestEnvelope source,
        WorkerConversationSessionState session,
        PlaybookDefinition playbook,
        WorkerPendingApprovalState pending)
    {
        var plan = JsonUtil.DeserializeRequired<SheetPackagePlan>(pending.PayloadJson);
        if (!string.Equals(plan.DocumentKey, _platform.GetDocumentKey(doc), StringComparison.OrdinalIgnoreCase))
        {
            return WorkerPlaybookExecutionResult.Failed(
                "Document da doi so voi plan preview. Em khong execute playbook nay tren document khac.",
                CreateCard(
                    pending.ToolName,
                    StatusCodes.ContextMismatch,
                    false,
                    "Document key hien tai khong khop voi plan preview.",
                    plan,
                    WorkerStages.Recovery,
                    100,
                    "Playbook execution fail-closed neu document drift de tranh create sai file.",
                    0.45d,
                    new[] { "Preview lai playbook tren document hien tai." },
                    WorkerExecutionTiers.Tier1,
                    false));
        }

        var toolCards = new List<WorkerToolCard>();
        var artifactRefs = new List<string>();
        var createdSheetIds = new List<int>();
        var createdViewIds = new List<int>();
        var createdSheetNumbers = new List<string>();
        var residuals = new List<string>();

        foreach (var levelPlan in plan.Levels)
        {
            var sheetOutcome = RunMutationTool(
                uiapp,
                source,
                session,
                ToolNames.SheetCreateSafe,
                new CreateSheetRequest
                {
                    DocumentKey = plan.DocumentKey,
                    SheetNumber = levelPlan.SheetNumber,
                    SheetName = levelPlan.SheetName,
                    TitleBlockTypeName = plan.TitleBlockName
                },
                $"{levelPlan.LevelName}: create_sheet",
                WorkerExecutionTiers.Tier1,
                autoExecutionEligible: true);
            toolCards.Add(sheetOutcome.Card);
            artifactRefs.AddRange(sheetOutcome.Artifacts);
            if (!sheetOutcome.Succeeded || sheetOutcome.Result == null || sheetOutcome.Result.ChangedIds.Count == 0)
            {
                residuals.Add($"{levelPlan.LevelName}: khong tao duoc sheet.");
                continue;
            }

            var sheetId = sheetOutcome.Result.ChangedIds[0];
            createdSheetIds.Add(sheetId);
            createdSheetNumbers.Add(levelPlan.SheetNumber);

            var viewOutcome = RunMutationTool(
                uiapp,
                source,
                session,
                ToolNames.ViewCreateProjectViewSafe,
                new CreateProjectViewRequest
                {
                    DocumentKey = plan.DocumentKey,
                    ViewKind = levelPlan.ViewKind,
                    Discipline = plan.Discipline,
                    LevelName = levelPlan.LevelName,
                    ViewName = levelPlan.ViewName,
                    TemplateName = plan.ViewTemplateName,
                    ScaleText = plan.ScaleText,
                    ActivateAfterCreate = false
                },
                $"{levelPlan.LevelName}: create_view",
                WorkerExecutionTiers.Tier1,
                autoExecutionEligible: true);
            toolCards.Add(viewOutcome.Card);
            artifactRefs.AddRange(viewOutcome.Artifacts);
            if (!viewOutcome.Succeeded || viewOutcome.Result == null || viewOutcome.Result.ChangedIds.Count == 0)
            {
                residuals.Add($"{levelPlan.LevelName}: khong tao duoc view.");
                continue;
            }

            var viewId = viewOutcome.Result.ChangedIds[0];
            createdViewIds.Add(viewId);

            var placeOutcome = RunMutationTool(
                uiapp,
                source,
                session,
                ToolNames.SheetPlaceViewsSafe,
                new PlaceViewsOnSheetRequest
                {
                    DocumentKey = plan.DocumentKey,
                    SheetId = sheetId,
                    Views = new List<ViewPlacementItem>
                    {
                        new ViewPlacementItem
                        {
                            ViewId = viewId,
                            CenterX = 1.0d,
                            CenterY = 0.5d
                        }
                    }
                },
                $"{levelPlan.LevelName}: place_view",
                WorkerExecutionTiers.Tier1,
                autoExecutionEligible: true);
            toolCards.Add(placeOutcome.Card);
            artifactRefs.AddRange(placeOutcome.Artifacts);
            if (!placeOutcome.Succeeded)
            {
                residuals.Add($"{levelPlan.LevelName}: khong place duoc view len sheet.");
                continue;
            }

            var reviewOutcome = RunReadTool(
                uiapp,
                source,
                session,
                ToolNames.ReviewSheetSummary,
                new SheetSummaryRequest
                {
                    DocumentKey = plan.DocumentKey,
                    SheetId = sheetId,
                    MaxPlacedViews = 10,
                    RequiredParameterNames = plan.RequiredSheetParameters.ToList()
                },
                $"Review sheet {levelPlan.SheetNumber} ({levelPlan.LevelName}) sau khi dat view.",
                WorkerStages.Verification,
                95d,
                WorkerExecutionTiers.Tier0);
            toolCards.Add(reviewOutcome.Card);
            artifactRefs.AddRange(reviewOutcome.Artifacts);
        }

        ToolResponseEnvelope? qcResponse = null;
        if (createdSheetIds.Count > 0)
        {
            var qcOutcome = RunReadTool(
                uiapp,
                source,
                session,
                ToolNames.ReviewSmartQc,
                new SmartQcRequest
                {
                    DocumentKey = plan.DocumentKey,
                    RulesetName = "base-rules",
                    SheetIds = createdSheetIds,
                    SheetNumbers = createdSheetNumbers,
                    RequiredParameterNames = plan.RequiredSheetParameters.ToList(),
                    MaxSheets = Math.Max(5, createdSheetIds.Count),
                    MaxFindings = 200
                },
                $"Aggregate QC cho {createdSheetIds.Count} sheet vua tao.",
                WorkerStages.Verification,
                100d,
                WorkerExecutionTiers.Tier0);
            toolCards.Add(qcOutcome.Card);
            artifactRefs.AddRange(qcOutcome.Artifacts);
            qcResponse = qcOutcome.Response;
        }

        var responseText = BuildExecutionSummary(plan, createdSheetNumbers, createdViewIds.Count, residuals, qcResponse);
        var result = new WorkerPlaybookExecutionResult
        {
            Handled = true,
            Succeeded = createdSheetIds.Count > 0,
            ConsumesPendingApproval = true,
            ResponseText = responseText,
            ToolCards = toolCards,
            ArtifactRefs = artifactRefs.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };

        if (residuals.Count > 0)
        {
            result.ActionCards.Add(new WorkerActionCard
            {
                ActionKind = WorkerActionKinds.Clarify,
                Title = "Refine standards / levels",
                Summary = $"Con {residuals.Count} residual(s). Xem report va chay lai voi scope hep hon neu can.",
                ToolName = pending.ToolName,
                ExecutionTier = WorkerExecutionTiers.Tier1,
                WhyThisAction = "Sheet package la lane chunkable: co the rerun theo tung level hoac sau khi sua standards.",
                Confidence = 0.78d,
                RecoveryHint = residuals.First(),
                AutoExecutionEligible = false
            });
        }

        return result;
    }

    private MutationToolOutcome RunMutationTool(
        UIApplication uiapp,
        ToolRequestEnvelope source,
        WorkerConversationSessionState session,
        string toolName,
        object payload,
        string summaryPrefix,
        string executionTier,
        bool autoExecutionEligible)
    {
        if (!AgentHost.TryGetCurrent(out var runtime) || runtime == null)
        {
            return MutationToolOutcome.Fail(CreateCard(toolName, StatusCodes.InternalError, false, $"Runtime chua san sang cho {toolName}.", payload, WorkerStages.Recovery, 100, "Khong co runtime thi khong duoc bypass tool lane.", 0.35d, new[] { "Kiem tra AgentHost/ToolRegistry." }, executionTier, false));
        }

        var payloadJson = JsonUtil.Serialize(payload);
        var previewEnvelope = BuildNestedEnvelope(session, source, toolName, payloadJson, true, string.Empty, string.Empty, string.Empty);
        var preview = runtime.ToolExecutor.Execute(uiapp, previewEnvelope);
        if (!preview.Succeeded)
        {
            return MutationToolOutcome.Fail(CreateCard(toolName, preview.StatusCode, false, $"{summaryPrefix} preview fail: {FirstDiagnostic(preview)}", preview.PayloadJson, WorkerStages.Recovery, 100, "Dung som o preview de khong mutate sai scope.", 0.48d, BuildDiagnosticHints(preview), executionTier, false, preview.Artifacts));
        }

        var previewResult = JsonUtil.DeserializeRequired<ExecutionResult>(preview.PayloadJson);
        var executeEnvelope = BuildNestedEnvelope(session, source, toolName, payloadJson, false, preview.ApprovalToken, preview.PreviewRunId, previewResult.ResolvedContext != null ? JsonUtil.Serialize(previewResult.ResolvedContext) : string.Empty);
        var execute = runtime.ToolExecutor.Execute(uiapp, executeEnvelope);
        if (!execute.Succeeded)
        {
            return MutationToolOutcome.Fail(CreateCard(toolName, execute.StatusCode, false, $"{summaryPrefix} execute fail: {FirstDiagnostic(execute)}", execute.PayloadJson, WorkerStages.Recovery, 100, "Execute fail du token/context va duoc surfacing ro cho worker.", 0.52d, BuildDiagnosticHints(execute), executionTier, false, execute.Artifacts));
        }

        var executeResult = JsonUtil.DeserializeRequired<ExecutionResult>(execute.PayloadJson);
        return MutationToolOutcome.Ok(
            CreateCard(toolName, execute.StatusCode, true, $"{summaryPrefix} ok • changed={executeResult.ChangedIds.Count}.", execute.PayloadJson, WorkerStages.Execution, 100, "Nested mutation van di qua dry-run -> approval token -> execute trong playbook lane.", 0.86d, new[] { "Neu can verify them, xem review.sheet_summary hoac review.smart_qc." }, executionTier, autoExecutionEligible, execute.Artifacts),
            executeResult,
            execute.Artifacts);
    }

    private ReadToolOutcome RunReadTool(
        UIApplication uiapp,
        ToolRequestEnvelope source,
        WorkerConversationSessionState session,
        string toolName,
        object payload,
        string summary,
        string stage,
        double progress,
        string executionTier)
    {
        if (!AgentHost.TryGetCurrent(out var runtime) || runtime == null)
        {
            return ReadToolOutcome.Fail(CreateCard(toolName, StatusCodes.InternalError, false, $"Runtime chua san sang cho {toolName}.", payload, WorkerStages.Recovery, 100, "Khong co runtime thi khong co evidence/QC nhat quan.", 0.35d, new[] { "Kiem tra AgentHost." }, executionTier, false));
        }

        var request = BuildNestedEnvelope(session, source, toolName, JsonUtil.Serialize(payload), false, string.Empty, string.Empty, string.Empty);
        var response = runtime.ToolExecutor.Execute(uiapp, request);
        return response.Succeeded
            ? ReadToolOutcome.Ok(CreateCard(toolName, response.StatusCode, true, summary, response.PayloadJson, stage, progress, "Read/QC step cung di qua tool lane chung de evidence/report nhat quan.", 0.9d, new[] { "Co the doi chieu voi standards pack neu can." }, executionTier, true, response.Artifacts), response, response.Artifacts)
            : ReadToolOutcome.Fail(CreateCard(toolName, response.StatusCode, false, $"{summary} • {FirstDiagnostic(response)}", response.PayloadJson, WorkerStages.Recovery, 100, "QC fail duoc surfacing ro de worker khong bao cao ao.", 0.42d, BuildDiagnosticHints(response), executionTier, false, response.Artifacts));
    }

    private static ToolRequestEnvelope BuildNestedEnvelope(WorkerConversationSessionState session, ToolRequestEnvelope source, string toolName, string payloadJson, bool dryRun, string approvalToken, string previewRunId, string expectedContextJson)
    {
        return new ToolRequestEnvelope
        {
            RequestId = Guid.NewGuid().ToString("N"),
            ToolName = toolName,
            PayloadJson = payloadJson,
            Caller = string.IsNullOrWhiteSpace(source.Caller) ? "worker.playbook" : source.Caller,
            SessionId = session.SessionId,
            DryRun = dryRun,
            TargetDocument = string.IsNullOrWhiteSpace(source.TargetDocument) ? session.DocumentKey : source.TargetDocument,
            TargetView = source.TargetView,
            ExpectedContextJson = expectedContextJson ?? string.Empty,
            ApprovalToken = approvalToken ?? string.Empty,
            ScopeDescriptorJson = source.ScopeDescriptorJson,
            RequestedAtUtc = DateTime.UtcNow,
            PreviewRunId = previewRunId ?? string.Empty,
            CorrelationId = Guid.NewGuid().ToString("N"),
            ProtocolVersion = source.ProtocolVersion,
            RequestedPriority = ToolQueuePriorities.Normal
        };
    }
    private SheetPackagePlan BuildSheetPlan(Document doc, string userMessage, PlaybookDefinition playbook, PlaybookPreviewResponse preview)
    {
        var standards = (preview?.Standards?.Values ?? new List<StandardsResolvedValue>())
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.RequestedKey))
            .GroupBy(x => x.RequestedKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Last().Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);

        var discipline = playbook.PlaybookId.IndexOf("mep", StringComparison.OrdinalIgnoreCase) >= 0 ? "mep" : "architectural";
        var viewStandardKey = discipline == "mep" ? "mep_plan" : "architectural_plan";
        var missing = new List<string>();
        var sheetFormat = GetRequiredStandard(standards, $"naming.json#sheet.{discipline}.format", missing);
        var viewFormat = GetRequiredStandard(standards, "naming.json#view.floor_plan", missing);
        var templateName = GetRequiredStandard(standards, $"templates.json#view_templates.{viewStandardKey}", missing);
        var titleBlockName = GetRequiredStandard(standards, "title_blocks.json#title_blocks.A1", missing);
        var scaleText = GetRequiredStandard(standards, $"scales.json#view_scales.{viewStandardKey}", missing);
        var filters = ParseJsonStringList(GetOptionalStandard(standards, $"filters.json#sheet_filters.{viewStandardKey}"));
        var requiredSheetParameters = ParseJsonStringList(GetOptionalStandard(standards, "parameters.json#required_sheet_parameters"));
        var levels = ResolveLevels(doc, userMessage);
        if (levels.Count == 0)
        {
            missing.Add("levels");
        }

        var plan = new SheetPackagePlan
        {
            PlaybookId = playbook.PlaybookId,
            WorkspaceId = preview?.WorkspaceId ?? "default",
            DocumentKey = _platform.GetDocumentKey(doc),
            UserMessage = userMessage ?? string.Empty,
            Discipline = discipline,
            TitleBlockName = titleBlockName,
            ViewTemplateName = templateName,
            ScaleText = scaleText,
            Filters = filters,
            RequiredSheetParameters = requiredSheetParameters,
            StandardsSummary = preview?.Standards?.Summary ?? string.Empty
        };

        if (missing.Count > 0)
        {
            plan.PreviewSummary = $"Playbook {playbook.PlaybookId} chua du du lieu de execute. Missing: {string.Join(", ", missing.Distinct(StringComparer.OrdinalIgnoreCase))}.";
            plan.RecoveryHints.Add("Cap nhat standards pack hoac noi ro levels trong request.");
            return plan;
        }

        var existingSheetNumbers = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>().Select(x => x.SheetNumber ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        var existingViewNames = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(x => !x.IsTemplate).Select(x => x.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        var availableTitleBlocks = new HashSet<string>(new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().Select(x => x.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        var availableTemplates = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(x => x.IsTemplate).Select(x => x.Name ?? string.Empty), StringComparer.OrdinalIgnoreCase);
        var residuals = new List<string>();

        if (!availableTitleBlocks.Contains(titleBlockName))
        {
            residuals.Add($"Title block '{titleBlockName}' khong ton tai trong model.");
        }

        if (!string.IsNullOrWhiteSpace(templateName) && !availableTemplates.Contains(templateName))
        {
            residuals.Add($"View template '{templateName}' khong ton tai trong model.");
        }

        var quotedName = ExtractQuotedName(userMessage ?? string.Empty);
        var seq = 1;
        foreach (var level in levels)
        {
            var levelName = level.Name ?? $"Level {seq}";
            var levelNumber = ResolveLevelNumber(levelName, seq);
            var sheetNumber = FormatSheetNumber(sheetFormat, levelNumber, 1);
            var sheetName = string.IsNullOrWhiteSpace(quotedName)
                ? $"{(discipline == "mep" ? "MEP Plan" : "Architectural Plan")} - {levelName}"
                : levels.Count > 1 ? $"{quotedName} - {levelName}" : quotedName;
            var viewName = FormatViewName(viewFormat, discipline, levelName, "Documentation");

            if (existingSheetNumbers.Contains(sheetNumber))
            {
                residuals.Add($"Sheet number '{sheetNumber}' da ton tai cho level {levelName}.");
            }

            if (existingViewNames.Contains(viewName))
            {
                residuals.Add($"View '{viewName}' da ton tai cho level {levelName}.");
            }

            plan.Levels.Add(new SheetLevelPlan
            {
                LevelId = checked((int)level.Id.Value),
                LevelName = levelName,
                SheetNumber = sheetNumber,
                SheetName = sheetName,
                ViewName = viewName,
                ViewKind = discipline == "mep" ? "mep_plan" : "floor_plan"
            });
            seq++;
        }

        if (residuals.Count > 0)
        {
            plan.PreviewSummary = $"Playbook {playbook.PlaybookId} bi block o preflight. {string.Join(" ", residuals.Take(4))}".Trim();
            plan.RecoveryHints.AddRange(residuals.Take(6));
            return plan;
        }

        plan.IsExecutable = true;
        var sheetRange = plan.Levels.Count == 1
            ? plan.Levels[0].SheetNumber
            : $"{plan.Levels.First().SheetNumber} -> {plan.Levels.Last().SheetNumber}";
        plan.PreviewSummary = $"Se tao {plan.Levels.Count} sheet package ({sheetRange}); title block={plan.TitleBlockName}; template={plan.ViewTemplateName}; scale={plan.ScaleText}; filters={(plan.Filters.Count > 0 ? string.Join(", ", plan.Filters) : "template-managed")}; required params={plan.RequiredSheetParameters.Count}.";
        plan.RecoveryHints.Add("Neu standards doi, preview lai playbook de refresh naming/template/title block.");
        if (plan.Filters.Count > 0)
        {
            plan.RecoveryHints.Add("Wave nay rely vao view template de mang filter/graphics; explicit filter authoring se la primitive tiep theo." );
        }

        return plan;
    }

    private static bool Supports(PlaybookDefinition playbook)
    {
        return playbook != null && string.Equals(playbook.Lane, "sheet_authoring", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPendingSummary(PlaybookDefinition playbook, SheetPackagePlan plan)
    {
        var range = plan.Levels.Count == 0
            ? "khong co level"
            : plan.Levels.Count == 1
                ? plan.Levels[0].SheetNumber
                : $"{plan.Levels.First().SheetNumber}-{plan.Levels.Last().SheetNumber}";
        return $"{playbook.PlaybookId} • {plan.Levels.Count} sheet(s) • {range} • TB={plan.TitleBlockName} • Template={plan.ViewTemplateName} • Scale={plan.ScaleText}";
    }

    private static string BuildExecutionSummary(SheetPackagePlan plan, IReadOnlyList<string> createdSheetNumbers, int createdViews, IReadOnlyList<string> residuals, ToolResponseEnvelope? qcResponse)
    {
        var qcSummary = string.Empty;
        if (qcResponse != null && qcResponse.Succeeded && !string.IsNullOrWhiteSpace(qcResponse.PayloadJson))
        {
            try
            {
                qcSummary = JsonUtil.DeserializeRequired<SmartQcResponse>(qcResponse.PayloadJson).Summary;
            }
            catch
            {
                qcSummary = string.Empty;
            }
        }

        var residualNote = residuals.Count > 0 ? $" Residuals: {residuals.Count}. {string.Join(" ", residuals.Take(3))}" : string.Empty;
        var filtersNote = plan.Filters.Count > 0 ? $" Filters target: {string.Join(", ", plan.Filters)} (template-managed)." : string.Empty;
        return $"Da execute {plan.PlaybookId}: tao {createdSheetNumbers.Count} sheet, {createdViews} view. Sheets: {string.Join(", ", createdSheetNumbers)}. {qcSummary}{filtersNote}{residualNote}".Trim();
    }

    private static string GetRequiredStandard(IReadOnlyDictionary<string, string> standards, string key, ICollection<string> missing)
    {
        if (standards.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        missing.Add(key);
        return string.Empty;
    }

    private static string GetOptionalStandard(IReadOnlyDictionary<string, string> standards, string key)
    {
        return standards.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
    }

    private static List<string> ParseJsonStringList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        try
        {
            return JsonUtil.DeserializeRequired<List<string>>(json)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static List<Level> ResolveLevels(Document doc, string message)
    {
        var allLevels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(x => x.Elevation)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (allLevels.Count == 0)
        {
            return new List<Level>();
        }

        var results = new List<Level>();
        var seen = new HashSet<long>();
        foreach (var level in allLevels)
        {
            if (!string.IsNullOrWhiteSpace(level.Name) && message.IndexOf(level.Name, StringComparison.OrdinalIgnoreCase) >= 0 && seen.Add(level.Id.Value))
            {
                results.Add(level);
            }
        }

        foreach (Match match in Regex.Matches(message ?? string.Empty, "(?:tang|tầng|level|lvl|lv)\\s*(?<start>-?\\d+)\\s*(?:-|to|den|đến)\\s*(?<end>-?\\d+)", RegexOptions.IgnoreCase))
        {
            if (!match.Success)
            {
                continue;
            }

            var start = int.Parse(match.Groups["start"].Value, CultureInfo.InvariantCulture);
            var end = int.Parse(match.Groups["end"].Value, CultureInfo.InvariantCulture);
            var min = Math.Min(start, end);
            var max = Math.Max(start, end);
            foreach (var level in allLevels)
            {
                var number = ResolveLevelNumber(level.Name ?? string.Empty, 0);
                if (number >= min && number <= max && seen.Add(level.Id.Value))
                {
                    results.Add(level);
                }
            }
        }

        foreach (Match match in Regex.Matches(message ?? string.Empty, "(?<!\\d)(-?\\d+)(?!\\d)"))
        {
            if (!match.Success)
            {
                continue;
            }

            var number = int.Parse(match.Value, CultureInfo.InvariantCulture);
            var level = allLevels.FirstOrDefault(x => ResolveLevelNumber(x.Name, 0) == number);
            if (level != null && seen.Add(level.Id.Value))
            {
                results.Add(level);
            }
        }

        return results.OrderBy(x => x.Elevation).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
    private static int ResolveLevelNumber(string levelName, int fallback)
    {
        var match = Regex.Match(levelName ?? string.Empty, "-?\\d+");
        if (match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback <= 0 ? 1 : fallback;
    }

    private static string FormatSheetNumber(string format, int floor, int seq)
    {
        var result = format ?? string.Empty;
        result = Regex.Replace(result, "\\{floor:(?<pad>0?\\d+)d\\}", m => floor.ToString(new string('0', Math.Max(1, int.Parse(m.Groups["pad"].Value, CultureInfo.InvariantCulture))), CultureInfo.InvariantCulture));
        result = Regex.Replace(result, "\\{seq:(?<pad>0?\\d+)d\\}", m => seq.ToString(new string('0', Math.Max(1, int.Parse(m.Groups["pad"].Value, CultureInfo.InvariantCulture))), CultureInfo.InvariantCulture));
        result = ReplaceOrdinalIgnoreCase(result, "{floor}", floor.ToString(CultureInfo.InvariantCulture));
        result = ReplaceOrdinalIgnoreCase(result, "{seq}", seq.ToString(CultureInfo.InvariantCulture));
        return result;
    }

    private static string FormatViewName(string format, string discipline, string levelName, string purpose)
    {
        var disciplineLabel = string.Equals(discipline, "mep", StringComparison.OrdinalIgnoreCase) ? "MEP" : "Architectural";
        var result = format ?? "{discipline} - Level {level} - {purpose}";
        result = ReplaceOrdinalIgnoreCase(result, "{discipline}", disciplineLabel);
        result = ReplaceOrdinalIgnoreCase(result, "{level}", levelName ?? string.Empty);
        result = ReplaceOrdinalIgnoreCase(result, "{purpose}", purpose ?? string.Empty);
        result = ReplaceOrdinalIgnoreCase(result, "{scope}", purpose ?? string.Empty);
        result = ReplaceOrdinalIgnoreCase(result, "{description}", purpose ?? string.Empty);
        result = ReplaceOrdinalIgnoreCase(result, "{seq}", "01");
        return result;
    }

    private static string ReplaceOrdinalIgnoreCase(string input, string oldValue, string newValue)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue))
        {
            return input ?? string.Empty;
        }

        var replacement = (newValue ?? string.Empty).Replace("$", "$$");
        return Regex.Replace(input, Regex.Escape(oldValue), replacement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string ExtractQuotedName(string text)
    {
        var match = Regex.Match(text ?? string.Empty, "\"(?<name>[^\"]+)\"");
        return match.Success ? match.Groups["name"].Value.Trim() : string.Empty;
    }

    private static string FirstDiagnostic(ToolResponseEnvelope response)
    {
        return response.Diagnostics?.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Message))?.Message ?? response.StatusCode ?? "Khong ro loi.";
    }

    private static IEnumerable<string> BuildDiagnosticHints(ToolResponseEnvelope response)
    {
        var hints = new List<string>();
        if (response.Diagnostics != null)
        {
            hints.AddRange(response.Diagnostics.Where(x => !string.IsNullOrWhiteSpace(x.Message)).Select(x => x.Message));
        }

        hints.Add("Neu standards drift, preview lai playbook de refresh naming/template/title block.");
        return hints.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToList();
    }

    private static WorkerToolCard CreateCard(
        string toolName,
        string statusCode,
        bool succeeded,
        string summary,
        object payload,
        string stage,
        double progress,
        string whyThisTool,
        double confidence,
        IEnumerable<string> recoveryHints,
        string executionTier,
        bool autoExecutionEligible,
        IEnumerable<string>? artifactRefs = null)
    {
        return new WorkerToolCard
        {
            ToolName = toolName,
            StatusCode = statusCode,
            Succeeded = succeeded,
            Summary = summary ?? string.Empty,
            PayloadJson = payload is string json ? json : JsonUtil.Serialize(payload),
            ArtifactRefs = artifactRefs?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
            Stage = stage,
            Progress = progress,
            WhyThisTool = whyThisTool ?? string.Empty,
            Confidence = confidence,
            RecoveryHints = recoveryHints?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>(),
            ExecutionTier = executionTier,
            AutoExecutionEligible = autoExecutionEligible
        };
    }

    internal sealed class WorkerPlaybookPreviewResult
    {
        internal bool Handled { get; set; }
        internal bool AwaitingApproval { get; set; }
        internal string ResponseText { get; set; } = string.Empty;
        internal WorkerPendingApprovalState PendingApproval { get; set; } = new WorkerPendingApprovalState();
        internal List<WorkerToolCard> ToolCards { get; set; } = new List<WorkerToolCard>();
        internal List<WorkerActionCard> ActionCards { get; set; } = new List<WorkerActionCard>();

        internal static WorkerPlaybookPreviewResult Blocked(string responseText, WorkerToolCard toolCard, WorkerActionCard actionCard)
        {
            return new WorkerPlaybookPreviewResult
            {
                Handled = true,
                ResponseText = responseText,
                ToolCards = new List<WorkerToolCard> { toolCard },
                ActionCards = new List<WorkerActionCard> { actionCard }
            };
        }
    }

    internal sealed class WorkerPlaybookExecutionResult
    {
        internal bool Handled { get; set; }
        internal bool Succeeded { get; set; }
        internal bool ConsumesPendingApproval { get; set; }
        internal string ResponseText { get; set; } = string.Empty;
        internal List<WorkerToolCard> ToolCards { get; set; } = new List<WorkerToolCard>();
        internal List<string> ArtifactRefs { get; set; } = new List<string>();
        internal List<WorkerActionCard> ActionCards { get; set; } = new List<WorkerActionCard>();

        internal static WorkerPlaybookExecutionResult Failed(string responseText, WorkerToolCard toolCard)
        {
            return new WorkerPlaybookExecutionResult
            {
                Handled = true,
                Succeeded = false,
                ConsumesPendingApproval = true,
                ResponseText = responseText,
                ToolCards = new List<WorkerToolCard> { toolCard }
            };
        }
    }

    private sealed class MutationToolOutcome
    {
        internal WorkerToolCard Card { get; set; } = new WorkerToolCard();
        internal bool Succeeded { get; set; }
        internal ExecutionResult? Result { get; set; }
        internal List<string> Artifacts { get; set; } = new List<string>();

        internal static MutationToolOutcome Ok(WorkerToolCard card, ExecutionResult result, IEnumerable<string>? artifacts)
        {
            return new MutationToolOutcome
            {
                Card = card,
                Succeeded = true,
                Result = result,
                Artifacts = artifacts?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>()
            };
        }

        internal static MutationToolOutcome Fail(WorkerToolCard card)
        {
            return new MutationToolOutcome
            {
                Card = card,
                Succeeded = false,
                Artifacts = card.ArtifactRefs.ToList()
            };
        }
    }

    private sealed class ReadToolOutcome
    {
        internal WorkerToolCard Card { get; set; } = new WorkerToolCard();
        internal ToolResponseEnvelope? Response { get; set; }
        internal List<string> Artifacts { get; set; } = new List<string>();

        internal static ReadToolOutcome Ok(WorkerToolCard card, ToolResponseEnvelope response, IEnumerable<string>? artifacts)
        {
            return new ReadToolOutcome
            {
                Card = card,
                Response = response,
                Artifacts = artifacts?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>()
            };
        }

        internal static ReadToolOutcome Fail(WorkerToolCard card)
        {
            return new ReadToolOutcome
            {
                Card = card,
                Artifacts = card.ArtifactRefs.ToList()
            };
        }
    }

    [DataContract]
    private sealed class SheetPackagePlan
    {
        [DataMember(Order = 1)] public string PlaybookId { get; set; } = string.Empty;
        [DataMember(Order = 2)] public string WorkspaceId { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string DocumentKey { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string UserMessage { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string Discipline { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string TitleBlockName { get; set; } = string.Empty;
        [DataMember(Order = 7)] public string ViewTemplateName { get; set; } = string.Empty;
        [DataMember(Order = 8)] public string ScaleText { get; set; } = string.Empty;
        [DataMember(Order = 9)] public List<string> Filters { get; set; } = new List<string>();
        [DataMember(Order = 10)] public List<string> RequiredSheetParameters { get; set; } = new List<string>();
        [DataMember(Order = 11)] public List<SheetLevelPlan> Levels { get; set; } = new List<SheetLevelPlan>();
        [DataMember(Order = 12)] public string StandardsSummary { get; set; } = string.Empty;
        [DataMember(Order = 13)] public bool IsExecutable { get; set; }
        [DataMember(Order = 14)] public string PreviewSummary { get; set; } = string.Empty;
        [DataMember(Order = 15)] public List<string> RecoveryHints { get; set; } = new List<string>();
    }

    [DataContract]
    private sealed class SheetLevelPlan
    {
        [DataMember(Order = 1)] public int LevelId { get; set; }
        [DataMember(Order = 2)] public string LevelName { get; set; } = string.Empty;
        [DataMember(Order = 3)] public string SheetNumber { get; set; } = string.Empty;
        [DataMember(Order = 4)] public string SheetName { get; set; } = string.Empty;
        [DataMember(Order = 5)] public string ViewName { get; set; } = string.Empty;
        [DataMember(Order = 6)] public string ViewKind { get; set; } = string.Empty;
    }
}
