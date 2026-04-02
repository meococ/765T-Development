using BIM765T.Revit.Contracts.Proto;

namespace BIM765T.Revit.WorkerHost.Routing;

internal sealed class MissionCommandInput
{
    public EnvelopeMetadata Meta { get; set; } = new EnvelopeMetadata();

    public string MissionId { get; set; } = string.Empty;

    public string CommandName { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public string ApprovalToken { get; set; } = string.Empty;

    public string PreviewRunId { get; set; } = string.Empty;

    public string ExpectedContextJson { get; set; } = string.Empty;

    public bool AllowMutations { get; set; }

    public string RecoveryBranchId { get; set; } = string.Empty;
}
