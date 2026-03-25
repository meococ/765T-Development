using System.Collections.Generic;
using StatusCodes = BIM765T.Revit.Contracts.Common.StatusCodes;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class SafetyAssessment
{
    public bool Allowed { get; set; } = true;

    public string StatusCode { get; set; } = StatusCodes.Ok;

    public string Summary { get; set; } = string.Empty;

    public string ResolvedCommandText { get; set; } = string.Empty;

    public List<string> Diagnostics { get; } = new List<string>();
}
