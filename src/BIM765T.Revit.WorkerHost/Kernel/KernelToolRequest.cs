namespace BIM765T.Revit.WorkerHost.Kernel;

internal sealed class KernelToolRequest
{
    public string MissionId { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;

    public string CausationId { get; set; } = string.Empty;

    public string ActorId { get; set; } = string.Empty;

    public string DocumentKey { get; set; } = string.Empty;

    public string RequestedAtUtc { get; set; } = string.Empty;

    public int TimeoutMs { get; set; }

    public string CancellationTokenId { get; set; } = string.Empty;

    public string ToolName { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public string Caller { get; set; } = string.Empty;

    public string SessionId { get; set; } = string.Empty;

    public bool DryRun { get; set; } = true;

    public string TargetDocument { get; set; } = string.Empty;

    public string TargetView { get; set; } = string.Empty;

    public string ExpectedContextJson { get; set; } = string.Empty;

    public string ApprovalToken { get; set; } = string.Empty;

    public string ScopeDescriptorJson { get; set; } = string.Empty;

    public string PreviewRunId { get; set; } = string.Empty;
}
