using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Copilot.Core.Brain;
using BIM765T.Revit.WorkerHost.Configuration;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class MissionToolCandidateBuilder : IToolCandidateBuilder
{
    private static readonly string[] CoreGroundingTools =
    {
        ToolNames.SessionGetTaskContext,
        ToolNames.ContextGetDeltaSummary,
        ToolNames.ProjectGetContextBundle,
        ToolNames.CommandSearch,
        ToolNames.ToolGetGuidance
    };

    private readonly IntentClassifier _classifier;
    private readonly PersonaRegistry _personas;
    private readonly WorkerReasoningEngine _ruleEngine;
    private readonly WorkerHostSettings? _settings;

    public MissionToolCandidateBuilder()
        : this(new IntentClassifier(), new PersonaRegistry(), null)
    {
    }

    public MissionToolCandidateBuilder(IntentClassifier classifier, PersonaRegistry personas, WorkerHostSettings? settings = null)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _personas = personas ?? throw new ArgumentNullException(nameof(personas));
        _ruleEngine = new WorkerReasoningEngine(_classifier, _personas);
        _settings = settings;
    }

    public MissionCandidateSet Build(MissionPlanningContext context)
    {
        context ??= new MissionPlanningContext();
        var autonomyMode = WorkerAutonomyModes.Normalize(
            string.IsNullOrWhiteSpace(context.AutonomyMode)
                ? _settings?.ResolveAutonomyMode()
                : context.AutonomyMode);
        var workspaceId = string.IsNullOrWhiteSpace(context.WorkspaceId) ? "default" : context.WorkspaceId.Trim();
        var session = new WorkerConversationSessionState
        {
            SessionId = string.IsNullOrWhiteSpace(context.SessionId) ? Guid.NewGuid().ToString("N") : context.SessionId.Trim(),
            PersonaId = string.IsNullOrWhiteSpace(context.PersonaId) ? WorkerPersonas.RevitWorker : context.PersonaId.Trim(),
            ClientSurface = string.IsNullOrWhiteSpace(context.ClientSurface) ? WorkerClientSurfaces.Mcp : context.ClientSurface.Trim(),
            DocumentKey = context.DocumentKey ?? string.Empty,
            PendingApprovalState = context.PendingApprovalState ?? new WorkerPendingApprovalState()
        };

        var persona = _personas.Resolve(session.PersonaId);
        var classification = _classifier.Classify(context.UserMessage ?? string.Empty, session.PendingApprovalState?.HasPendingApproval ?? false);
        var contextSummary = BuildContextSummary(context, workspaceId);
        var decision = _ruleEngine.ProcessMessage(session, context.UserMessage ?? string.Empty, context.ContinueMission, contextSummary, workspaceId);

        contextSummary.SuggestedNextTools = decision.PlannedTools?.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        contextSummary.SimilarEpisodeHints = decision.SuggestedActions?
            .Where(action => !string.IsNullOrWhiteSpace(action.ToolName))
            .Select(action => action.ToolName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList() ?? new List<string>();

        var candidateTools = BuildCandidateTools(context, classification, decision, autonomyMode);
        var candidateCommands = BuildCandidateCommands(context.UserMessage, decision, classification, autonomyMode);
        contextSummary.SuggestedNextTools = candidateTools.Take(8).ToList();

        return new MissionCandidateSet
        {
            Session = session,
            Persona = persona,
            Classification = classification,
            RuleDecision = decision,
            ContextSummary = contextSummary,
            WorkspaceId = workspaceId,
            AutonomyMode = autonomyMode,
            CandidateTools = candidateTools,
            CandidateCommands = candidateCommands
        };
    }

    private List<string> BuildCandidateTools(
        MissionPlanningContext context,
        WorkerIntentClassification classification,
        WorkerDecision decision,
        string autonomyMode)
    {
        var tools = new List<string>();
        tools.AddRange(CoreGroundingTools);
        tools.AddRange(decision.PlannedTools ?? new List<string>());

        var message = (context.UserMessage ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(message))
        {
            var normalized = message.ToLowerInvariant();
            AddIfMatches(normalized, tools, new[] { "tong quan", "overview", "du an", "project", "context", "hieu model", "scan" },
                ToolNames.ProjectGetManifest, ToolNames.ProjectGetContextBundle, ToolNames.ProjectGetDeepScan, ToolNames.ProjectDeepScan);
            AddIfMatches(normalized, tools, new[] { "init", "workspace", "bat dau", "onboard" },
                ToolNames.ProjectInitPreview, ToolNames.ProjectInitApply, ToolNames.ProjectGetManifest, ToolNames.ProjectGetContextBundle);
            AddIfMatches(normalized, tools, new[] { "review", "audit", "qc", "health", "kiem tra", "clash", "warning" },
                ToolNames.ReviewSmartQc, ToolNames.ReviewModelHealth, ToolNames.ReviewSheetSummary, ToolNames.AuditNamingConvention, ToolNames.AuditComplianceReport);
            AddIfMatches(normalized, tools, new[] { "sheet", "ban ve", "titleblock", "viewport" },
                ToolNames.SheetCreateSafe, ToolNames.SheetPlaceViewsSafe, ToolNames.SheetRenumberSafe, ToolNames.ReviewSheetSummary);
            AddIfMatches(normalized, tools, new[] { "view", "3d", "template" },
                ToolNames.ViewCreate3dSafe, ToolNames.ViewDuplicateSafe, ToolNames.ViewCreateProjectViewSafe, ToolNames.ViewSetTemplateSafe);
            AddIfMatches(normalized, tools, new[] { "schedule", "bang thong ke", "export schedule" },
                ToolNames.SchedulePreviewCreate, ToolNames.ScheduleCreateSafe, ToolNames.DataExportSchedule, ToolNames.DataExtractScheduleStructured);
            AddIfMatches(normalized, tools, new[] { "family", "load family", "xray" },
                ToolNames.FamilyXray, ToolNames.FamilyLoadSafe, ToolNames.FamilyListLibraryRoots);
            AddIfMatches(normalized, tools, new[] { "script", "dynamo", "pyrevit", "automation", "tool moi", "tao tool" },
                ToolNames.ScriptList, ToolNames.ScriptValidate, ToolNames.ScriptComposeSafe, ToolNames.ScriptImportManifest, ToolNames.ScriptInstallPack, ToolNames.CommandSearch, ToolNames.IntentCompile);
            AddIfMatches(normalized, tools, new[] { "excel", "jira", "sync", "connect" },
                ToolNames.IntegrationPreviewSync, ToolNames.PolicyResolve, ToolNames.SpecialistResolve);
        }

        if (string.Equals(classification.Intent, "project_research_request", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(ToolNames.ProjectGetDeepScan);
            tools.Add(ToolNames.ArtifactSummarize);
            tools.Add(ToolNames.StandardsResolve);
            tools.Add(ToolNames.MemorySearchScoped);
        }

        if (string.Equals(classification.Intent, "mutation_request", StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(ToolNames.WorkflowQuickPlan);
            tools.Add(ToolNames.CommandDescribe);
            tools.Add(ToolNames.CommandExecuteSafe);
        }

        if (string.Equals(autonomyMode, WorkerAutonomyModes.Ship, StringComparison.OrdinalIgnoreCase))
        {
            tools.Add(ToolNames.MemoryFindSimilarRuns);
            tools.Add(ToolNames.PlaybookMatch);
            tools.Add(ToolNames.PlaybookPreview);
            tools.Add(ToolNames.ProjectGetManifest);
        }

        return tools
            .Where(tool => !string.IsNullOrWhiteSpace(tool))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> BuildCandidateCommands(
        string? userMessage,
        WorkerDecision decision,
        WorkerIntentClassification classification,
        string autonomyMode)
    {
        var commands = new List<string>();
        if (!string.IsNullOrWhiteSpace(decision.PreferredCommandId))
        {
            commands.Add(decision.PreferredCommandId);
        }

        var normalized = (userMessage ?? string.Empty).ToLowerInvariant();
        AddCommandIfMatches(normalized, commands, new[] { "qc", "review", "audit", "health" }, ToolNames.ReviewSmartQc, ToolNames.ReviewModelHealth);
        AddCommandIfMatches(normalized, commands, new[] { "sheet", "ban ve" }, ToolNames.SheetCreateSafe, ToolNames.SheetPlaceViewsSafe, ToolNames.SheetRenumberSafe);
        AddCommandIfMatches(normalized, commands, new[] { "view", "3d" }, ToolNames.ViewCreate3dSafe, ToolNames.ViewDuplicateSafe);
        AddCommandIfMatches(normalized, commands, new[] { "schedule", "export schedule" }, ToolNames.DataExportSchedule, ToolNames.ScheduleCreateSafe);
        AddCommandIfMatches(normalized, commands, new[] { "init", "workspace" }, ToolNames.ProjectInitPreview, ToolNames.ProjectDeepScan);
        AddCommandIfMatches(normalized, commands, new[] { "script", "dynamo", "tool moi" }, ToolNames.ScriptComposeSafe, ToolNames.ScriptValidate);

        if (string.Equals(classification.Intent, "mutation_request", StringComparison.OrdinalIgnoreCase))
        {
            commands.Add(ToolNames.CommandExecuteSafe);
        }

        if (string.Equals(autonomyMode, WorkerAutonomyModes.Ship, StringComparison.OrdinalIgnoreCase))
        {
            commands.Add(ToolNames.CommandSearch);
            commands.Add(ToolNames.WorkflowQuickPlan);
        }

        return commands
            .Where(command => !string.IsNullOrWhiteSpace(command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddIfMatches(string normalized, List<string> tools, IEnumerable<string> keywords, params string[] values)
    {
        if (keywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            tools.AddRange(values);
        }
    }

    private static void AddCommandIfMatches(string normalized, List<string> commands, IEnumerable<string> keywords, params string[] values)
    {
        if (keywords.Any(keyword => normalized.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            commands.AddRange(values);
        }
    }

    private static WorkerContextSummary BuildContextSummary(MissionPlanningContext context, string workspaceId)
    {
        var documentKey = context.DocumentKey ?? string.Empty;
        var documentTitle = FirstNonEmpty(context.TargetDocument, documentKey, "active_model");
        var activeViewName = FirstNonEmpty(context.TargetView, "active_view");
        return new WorkerContextSummary
        {
            DocumentKey = documentKey,
            DocumentTitle = documentTitle,
            ActiveViewName = activeViewName,
            Summary = $"Mission planning bootstrap for '{documentTitle}' / '{activeViewName}'.",
            WorkspaceId = workspaceId,
            ProjectSummary = string.Equals(workspaceId, "default", StringComparison.OrdinalIgnoreCase)
                ? "Workspace default chua duoc xac nhan deep scan."
                : $"Workspace '{workspaceId}' dang la nguon grounded chinh cho mission nay.",
            ProjectPrimaryModelStatus = string.IsNullOrWhiteSpace(documentKey) ? "no_document_key" : "document_attached",
            ProjectTopRefs = BuildGroundingRefs(context, workspaceId),
            GroundingLevel = WorkerGroundingLevels.LiveContextOnly,
            GroundingSummary = string.Equals(workspaceId, "default", StringComparison.OrdinalIgnoreCase)
                ? "Live Revit context available; workspace grounding se duoc nang cap neu context bundle/deep scan san sang."
                : $"Live Revit context available; workspace '{workspaceId}' co the duoc dung de ground mission.",
            GroundingRefs = BuildGroundingRefs(context, workspaceId)
        };
    }

    private static List<string> BuildGroundingRefs(MissionPlanningContext context, string workspaceId)
    {
        var refs = new List<string>();
        if (!string.IsNullOrWhiteSpace(context.DocumentKey))
        {
            refs.Add("doc:" + context.DocumentKey.Trim());
        }

        if (!string.IsNullOrWhiteSpace(context.TargetView))
        {
            refs.Add("view:" + context.TargetView.Trim());
        }

        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            refs.Add("workspace:" + workspaceId);
        }

        return refs;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
