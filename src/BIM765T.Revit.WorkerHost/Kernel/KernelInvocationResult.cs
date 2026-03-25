using System.Collections.Generic;

namespace BIM765T.Revit.WorkerHost.Kernel;

internal sealed class KernelInvocationResult
{
    public bool Succeeded { get; set; }

    public string StatusCode { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public string ApprovalToken { get; set; } = string.Empty;

    public string PreviewRunId { get; set; } = string.Empty;

    public string DiffSummaryJson { get; set; } = string.Empty;

    public string ReviewSummaryJson { get; set; } = string.Empty;

    public bool ConfirmationRequired { get; set; }

    public List<string> Diagnostics { get; set; } = new List<string>();

    public List<int> ChangedIds { get; set; } = new List<int>();

    public List<string> Artifacts { get; set; } = new List<string>();

    public string ProtocolVersion { get; set; } = string.Empty;
}
