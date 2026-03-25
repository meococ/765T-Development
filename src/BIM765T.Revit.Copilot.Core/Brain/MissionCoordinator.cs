using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core.Brain;

public sealed class MissionCoordinator
{
    public WorkerMission EnsureMission(WorkerConversationSessionState session, string intent, string goal, bool continueMission)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        var shouldCreateNew = !continueMission
                              || string.IsNullOrWhiteSpace(session.Mission.MissionId)
                              || string.Equals(session.Mission.Status, WorkerMissionStates.Completed, StringComparison.OrdinalIgnoreCase)
                              || string.Equals(session.Mission.Status, WorkerMissionStates.Failed, StringComparison.OrdinalIgnoreCase);

        if (shouldCreateNew)
        {
            session.Mission = new WorkerMission
            {
                MissionId = Guid.NewGuid().ToString("N"),
                Intent = intent ?? string.Empty,
                Goal = goal ?? string.Empty,
                Status = WorkerMissionStates.Understanding,
                Stage = WorkerFlowStages.Thinking,
                LastUpdatedUtc = DateTime.UtcNow
            };
            return session.Mission;
        }

        session.Mission.Intent = intent ?? session.Mission.Intent;
        session.Mission.Goal = string.IsNullOrWhiteSpace(goal) ? session.Mission.Goal : goal;
        session.Mission.LastUpdatedUtc = DateTime.UtcNow;
        if (string.Equals(session.Mission.Status, WorkerMissionStates.Idle, StringComparison.OrdinalIgnoreCase))
        {
            session.Mission.Status = WorkerMissionStates.Understanding;
        }

        session.Mission.Stage = WorkerFlowStages.Thinking;

        return session.Mission;
    }

    public void SetPlan(WorkerConversationSessionState session, WorkerDecision decision)
    {
        if (session == null || decision == null)
        {
            return;
        }

        session.Mission.Intent = decision.Intent;
        session.Mission.Goal = decision.Goal;
        session.Mission.ReasoningSummary = decision.ReasoningSummary;
        session.Mission.PlanSummary = decision.PlanSummary;
        session.Mission.DecisionRationale = decision.DecisionRationale;
        session.Mission.PlannedTools = decision.PlannedTools.ToList();
        session.Mission.ConfiguredProvider = decision.ConfiguredProvider;
        session.Mission.PlannerModel = decision.PlannerModel;
        session.Mission.ResponseModel = decision.ResponseModel;
        session.Mission.ReasoningMode = string.IsNullOrWhiteSpace(decision.ReasoningMode)
            ? WorkerReasoningModes.RuleFirst
            : decision.ReasoningMode;
        session.Mission.Status = WorkerMissionStates.Planned;
        session.Mission.Stage = WorkerFlowStages.Plan;
        session.Mission.PendingStep = decision.SuggestedActions.FirstOrDefault(x => x.IsPrimary)?.Title ?? string.Empty;
        session.Mission.LastUpdatedUtc = DateTime.UtcNow;
        session.LastUpdatedUtc = DateTime.UtcNow;
    }

    public void AwaitApproval(WorkerConversationSessionState session, string summary, IEnumerable<string>? plannedTools = null)
    {
        if (session == null)
        {
            return;
        }

        session.Mission.Status = WorkerMissionStates.AwaitingApproval;
        session.Mission.Stage = WorkerFlowStages.Approval;
        session.Mission.PlanSummary = string.IsNullOrWhiteSpace(summary) ? session.Mission.PlanSummary : summary;
        session.Mission.PendingStep = "approval";
        if (plannedTools != null)
        {
            session.Mission.PlannedTools = plannedTools.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        session.Mission.LastUpdatedUtc = DateTime.UtcNow;
        session.LastUpdatedUtc = DateTime.UtcNow;
    }

    public void MarkRunning(WorkerConversationSessionState session, string pendingStep)
    {
        if (session == null)
        {
            return;
        }

        session.Mission.Status = WorkerMissionStates.Running;
        session.Mission.Stage = WorkerFlowStages.Run;
        session.Mission.PendingStep = pendingStep ?? string.Empty;
        session.Mission.LastUpdatedUtc = DateTime.UtcNow;
        session.LastUpdatedUtc = DateTime.UtcNow;
    }

    public void MarkVerifying(WorkerConversationSessionState session, string summary = "")
    {
        if (session == null)
        {
            return;
        }

        session.Mission.Status = WorkerMissionStates.Verifying;
        session.Mission.Stage = WorkerFlowStages.Verify;
        if (!string.IsNullOrWhiteSpace(summary))
        {
            session.Mission.PlanSummary = summary;
        }

        session.Mission.LastUpdatedUtc = DateTime.UtcNow;
        session.LastUpdatedUtc = DateTime.UtcNow;
    }

    public void Complete(WorkerConversationSessionState session, string summary = "")
    {
        if (session == null)
        {
            return;
        }

        session.Mission.Status = WorkerMissionStates.Completed;
        session.Mission.Stage = WorkerFlowStages.Done;
        session.Mission.PendingStep = string.Empty;
        if (!string.IsNullOrWhiteSpace(summary))
        {
            session.Mission.PlanSummary = summary;
        }

        session.Mission.LastUpdatedUtc = DateTime.UtcNow;
        session.LastUpdatedUtc = DateTime.UtcNow;
    }

    public void Block(WorkerConversationSessionState session, string summary, string rationale = "")
    {
        if (session == null)
        {
            return;
        }

        session.Mission.Status = WorkerMissionStates.Blocked;
        session.Mission.Stage = WorkerFlowStages.Error;
        session.Mission.PlanSummary = string.IsNullOrWhiteSpace(summary) ? session.Mission.PlanSummary : summary;
        session.Mission.DecisionRationale = string.IsNullOrWhiteSpace(rationale) ? session.Mission.DecisionRationale : rationale;
        session.Mission.PendingStep = string.Empty;
        session.Mission.LastUpdatedUtc = DateTime.UtcNow;
        session.LastUpdatedUtc = DateTime.UtcNow;
    }

    public void Fail(WorkerConversationSessionState session, string summary, string rationale = "")
    {
        if (session == null)
        {
            return;
        }

        session.Mission.Status = WorkerMissionStates.Failed;
        session.Mission.Stage = WorkerFlowStages.Error;
        session.Mission.PlanSummary = string.IsNullOrWhiteSpace(summary) ? session.Mission.PlanSummary : summary;
        session.Mission.DecisionRationale = string.IsNullOrWhiteSpace(rationale) ? session.Mission.DecisionRationale : rationale;
        session.Mission.PendingStep = string.Empty;
        session.Mission.LastUpdatedUtc = DateTime.UtcNow;
        session.LastUpdatedUtc = DateTime.UtcNow;
    }
}
