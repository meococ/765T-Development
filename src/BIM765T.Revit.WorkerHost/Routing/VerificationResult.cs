using System.Collections.Generic;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class VerificationResult
{
    public string State { get; set; } = WorkerMissionStates.Blocked;

    public bool Terminal { get; set; }

    public string ResponseText { get; set; } = string.Empty;

    public List<MissionEventDescriptor> Events { get; } = new List<MissionEventDescriptor>();
}
