using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Context;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class OperationJournalEntry
{
    [DataMember(Order = 1)]
    public string JournalId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string RequestId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ToolName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Caller { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string ViewKey { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 8)]
    public DateTime EndedUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 9)]
    public bool Succeeded { get; set; }

    [DataMember(Order = 10)]
    public string StatusCode { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public List<int> ChangedIds { get; set; } = new List<int>();

    /// <summary>
    /// Tóm tắt kết quả ngắn gọn để hiện trong Activity Tab.
    /// Không lưu full JSON — chỉ 1-2 dòng context.
    /// VD: "12 warnings found", "Sheet A101: 4 views placed"
    /// </summary>
    [DataMember(Order = 12)]
    public string ResultSummary { get; set; } = string.Empty;

    /// <summary>
    /// Diagnostics summary dạng text — gom từ DiagnosticRecord[].
    /// VD: "WARNING: ACTIVE_WORKSET_MISMATCH | INFO: 3 elements checked"
    /// </summary>
    [DataMember(Order = 13)]
    public string DiagnosticsSummary { get; set; } = string.Empty;

    /// <summary>
    /// Số lượng diagnostics theo severity — để UI hiện badge.
    /// </summary>
    [DataMember(Order = 14)]
    public int DiagnosticsErrorCount { get; set; }

    [DataMember(Order = 15)]
    public int DiagnosticsWarningCount { get; set; }

    [DataMember(Order = 16)]
    public int DiagnosticsInfoCount { get; set; }

    [DataMember(Order = 17)]
    public string CorrelationId { get; set; } = string.Empty;

    [DataMember(Order = 18)]
    public string SessionId { get; set; } = string.Empty;

    [DataMember(Order = 19)]
    public string PreviewRunId { get; set; } = string.Empty;
}

[DataContract]
public sealed class EventRecord
{
    [DataMember(Order = 1)]
    public string EventKind { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ViewKey { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    [DataMember(Order = 5)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 6)]
    public string Message { get; set; } = string.Empty;
}

[DataContract]
public sealed class RecentEventsResponse
{
    [DataMember(Order = 1)]
    public List<EventRecord> Events { get; set; } = new List<EventRecord>();
}

[DataContract]
public sealed class RecentOperationsResponse
{
    [DataMember(Order = 1)]
    public List<OperationJournalEntry> Operations { get; set; } = new List<OperationJournalEntry>();
}

[DataContract]
public sealed class TaskContextRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int MaxRecentOperations { get; set; } = 10;

    [DataMember(Order = 3)]
    public int MaxRecentEvents { get; set; } = 10;

    [DataMember(Order = 4)]
    public bool IncludeCapabilities { get; set; } = true;

    [DataMember(Order = 5)]
    public bool IncludeToolCatalog { get; set; } = true;
}

[DataContract]
public sealed class TaskContextResponse
{
    [DataMember(Order = 1)]
    public DocumentSummaryDto Document { get; set; } = new DocumentSummaryDto();

    [DataMember(Order = 2)]
    public CurrentContextDto ActiveContext { get; set; } = new CurrentContextDto();

    [DataMember(Order = 3)]
    public SelectionSummaryDto Selection { get; set; } = new SelectionSummaryDto();

    [DataMember(Order = 4)]
    public ContextFingerprint Fingerprint { get; set; } = new ContextFingerprint();

    [DataMember(Order = 5)]
    public List<EventRecord> RecentEvents { get; set; } = new List<EventRecord>();

    [DataMember(Order = 6)]
    public List<OperationJournalEntry> RecentOperations { get; set; } = new List<OperationJournalEntry>();

    [DataMember(Order = 7, EmitDefaultValue = false)]
    public BridgeCapabilities? Capabilities { get; set; }

    [DataMember(Order = 8)]
    public List<ToolManifest> Tools { get; set; } = new List<ToolManifest>();

    /// <summary>
    /// Shortcut: AllowWriteTools — agent không cần parse sâu vào Capabilities.
    /// Dùng cho cold-start check ngay sau session.get_task_context.
    /// </summary>
    [DataMember(Order = 9)]
    public bool WriteEnabled { get; set; }

    /// <summary>
    /// Luôn true nếu response trả về được — agent dùng để confirm bridge alive.
    /// </summary>
    [DataMember(Order = 10)]
    public bool BridgeAlive { get; set; } = true;
}
