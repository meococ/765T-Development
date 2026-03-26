using System.Collections.Generic;
using Autodesk.Revit.DB;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Copilot.Core.Brain;

namespace BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows;

/// <summary>
/// Mutable context bag for the worker.message workflow.
/// Populated progressively across GatherContext → Plan → ExecuteIntent → Enhance → BuildResponse.
/// </summary>
internal sealed class MessageWorkflowContext
{
    // ── Input ───────────────────────────────────────────────────
    internal ToolRequestEnvelope Envelope { get; set; } = new ToolRequestEnvelope();
    internal WorkerMessageRequest Request { get; set; } = new WorkerMessageRequest();

    // ── GatherContext phase (UI thread) ─────────────────────────
    internal Document? Doc { get; set; }
    internal string DocumentKey { get; set; } = string.Empty;
    internal WorkerConversationSessionState Session { get; set; } = new WorkerConversationSessionState();
    internal string WorkspaceId { get; set; } = string.Empty;
    internal WorkerContextSummary ContextSummary { get; set; } = new WorkerContextSummary();

    // ── Plan phase (off-thread) ─────────────────────────────────
    internal WorkerDecision Decision { get; set; } = new WorkerDecision();

    // ── ExecuteIntent phase (UI thread) ─────────────────────────
    internal string ResponseText { get; set; } = string.Empty;
    internal List<WorkerToolCard> ToolCards { get; set; } = new List<WorkerToolCard>();
    internal List<WorkerActionCard> ActionCards { get; set; } = new List<WorkerActionCard>();
    internal List<string> ArtifactRefs { get; set; } = new List<string>();
    internal PlaybookRecommendation SelectedPlaybook { get; set; } = new PlaybookRecommendation();
    internal PlaybookPreviewResponse PlaybookPreview { get; set; } = new PlaybookPreviewResponse();
    internal CompiledTaskPlan CompiledPlan { get; set; } = new CompiledTaskPlan();
    internal string CapabilityDomain { get; set; } = CapabilityDomains.General;

    // ── Enhance phase (off-thread) ──────────────────────────────
    internal string NarrationMode { get; set; } = WorkerNarrationModes.RuleOnly;
    internal string NarrationDiagnostics { get; set; } = "Dang dung response rule-based trong native worker lane.";

    // ── BuildResponse phase (UI thread) ─────────────────────────
    internal WorkerResponse? FinalResponse { get; set; }
}
