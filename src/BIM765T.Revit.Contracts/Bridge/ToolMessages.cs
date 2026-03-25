using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Bridge;

[DataContract]
public sealed class ToolRequestEnvelope
{
    [DataMember(Order = 1)]
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

    [DataMember(Order = 2)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string PayloadJson { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Caller { get; set; } = "unknown";

    [DataMember(Order = 5)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public bool DryRun { get; set; } = true;

    [DataMember(Order = 7)]
    public string TargetDocument { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string TargetView { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string ExpectedContextJson { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string ApprovalToken { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public string ScopeDescriptorJson { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 13)]
    public string PreviewRunId { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public string CorrelationId { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public string ProtocolVersion { get; set; } = BIM765T.Revit.Contracts.Common.BridgeProtocol.PipeV1;

    [DataMember(Order = 16)]
    public string RequestedPriority { get; set; } = BIM765T.Revit.Contracts.Platform.ToolQueuePriorities.Normal;
}

[DataContract]
public sealed class ToolResponseEnvelope
{
    [DataMember(Order = 1)]
    public string RequestId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool Succeeded { get; set; }

    [DataMember(Order = 4)]
    public string StatusCode { get; set; } = string.Empty;

    [DataMember(Order = 5, EmitDefaultValue = false)]
    public string PayloadJson { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();

    [DataMember(Order = 7)]
    public bool ConfirmationRequired { get; set; }

    [DataMember(Order = 8, EmitDefaultValue = false)]
    public string ApprovalToken { get; set; } = string.Empty;

    [DataMember(Order = 9, EmitDefaultValue = false)]
    public string DiffSummaryJson { get; set; } = string.Empty;

    [DataMember(Order = 10, EmitDefaultValue = false)]
    public string ReviewSummaryJson { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public List<int> ChangedIds { get; set; } = new List<int>();

    [DataMember(Order = 12)]
    public List<string> Artifacts { get; set; } = new List<string>();

    [DataMember(Order = 13)]
    public DateTime ExecutedAtUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 14)]
    public long DurationMs { get; set; }

    [DataMember(Order = 15)]
    public string PreviewRunId { get; set; } = string.Empty;

    [DataMember(Order = 16)]
    public string CorrelationId { get; set; } = string.Empty;

    [DataMember(Order = 17)]
    public string ProtocolVersion { get; set; } = BIM765T.Revit.Contracts.Common.BridgeProtocol.PipeV1;

    [DataMember(Order = 18)]
    public string Stage { get; set; } = string.Empty;

    [DataMember(Order = 19)]
    public double Progress { get; set; }

    [DataMember(Order = 20)]
    public DateTime? HeartbeatUtc { get; set; }

    [DataMember(Order = 21)]
    public int ResidualIssueCount { get; set; }

    [DataMember(Order = 22)]
    public string ExecutionTier { get; set; } = BIM765T.Revit.Contracts.Platform.WorkerExecutionTiers.Tier0;
}
