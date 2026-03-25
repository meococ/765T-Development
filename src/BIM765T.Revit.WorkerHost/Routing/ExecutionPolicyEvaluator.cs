using System;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.WorkerHost.Eventing;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class ExecutionPolicyEvaluator : IExecutionPolicyEvaluator
{
    private readonly SafetyAgent _safety;

    public ExecutionPolicyEvaluator()
        : this(new SafetyAgent())
    {
    }

    public ExecutionPolicyEvaluator(SafetyAgent safety)
    {
        _safety = safety ?? throw new ArgumentNullException(nameof(safety));
    }

    public SafetyAssessment EvaluateSubmission(MissionPlan plan)
    {
        return _safety.EvaluateSubmission(plan);
    }

    public SafetyAssessment EvaluateCommand(MissionCommandInput input, MissionSnapshot snapshot)
    {
        return _safety.EvaluateCommand(input, snapshot);
    }
}
