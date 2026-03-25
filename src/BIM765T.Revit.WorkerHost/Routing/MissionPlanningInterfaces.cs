using BIM765T.Revit.WorkerHost.Memory;
using BIM765T.Revit.Copilot.Core.Brain;
using BIM765T.Revit.WorkerHost.Eventing;

namespace BIM765T.Revit.WorkerHost.Routing;

internal interface IToolCandidateBuilder
{
    MissionCandidateSet Build(MissionPlanningContext context);
}

internal interface IBoundedMissionPlanner
{
    MissionPlan BuildPlan(MissionPlanningContext context, MissionCandidateSet candidates);
}

internal interface IExecutionPolicyEvaluator
{
    SafetyAssessment EvaluateSubmission(MissionPlan plan);

    SafetyAssessment EvaluateCommand(MissionCommandInput input, MissionSnapshot snapshot);
}

internal interface IReadOnlyResearchOrchestrator
{
    MissionPlan Decorate(MissionPlan plan, RetrievalContext retrieval, MissionPlanningContext context);
}
