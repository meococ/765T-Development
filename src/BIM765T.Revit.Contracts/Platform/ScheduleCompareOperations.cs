using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

// ── Schedule Compare ──────────────────────────────────────────────────────────

/// <summary>Request to compare a live schedule view against a previously captured JSON baseline snapshot.</summary>
[DataContract]
public sealed class ScheduleCompareRequest
{
    /// <summary>Document key identifying the document that owns the schedule view.</summary>
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>ElementId of the schedule view to compare against the baseline.</summary>
    [DataMember(Order = 2)]
    public int ScheduleViewId { get; set; }

    /// <summary>
    /// JSON string of the baseline snapshot previously produced by <c>data.extract_schedule_structured</c>
    /// or an equivalent export. The diff engine compares the live rows against this snapshot.
    /// </summary>
    [DataMember(Order = 3)]
    public string BaselineSnapshotJson { get; set; } = string.Empty;

    /// <summary>When true, unchanged rows are included in <see cref="ScheduleCompareResponse.Diffs"/>. Defaults to false.</summary>
    [DataMember(Order = 4)]
    public bool IncludeUnchanged { get; set; } = false;
}

/// <summary>Result of a schedule comparison containing aggregate counts and per-row diffs.</summary>
[DataContract]
public sealed class ScheduleCompareResponse
{
    /// <summary>Number of rows present in the live schedule but absent from the baseline.</summary>
    [DataMember(Order = 1)]
    public int AddedRows { get; set; }

    /// <summary>Number of rows present in the baseline but absent from the live schedule.</summary>
    [DataMember(Order = 2)]
    public int RemovedRows { get; set; }

    /// <summary>Number of rows whose field values differ between baseline and live schedule.</summary>
    [DataMember(Order = 3)]
    public int ModifiedRows { get; set; }

    /// <summary>Number of rows that are identical in baseline and live schedule.</summary>
    [DataMember(Order = 4)]
    public int UnchangedRows { get; set; }

    /// <summary>Per-field diff records. Includes unchanged rows only when <see cref="ScheduleCompareRequest.IncludeUnchanged"/> is true.</summary>
    [DataMember(Order = 5)]
    public List<ScheduleDiffItem> Diffs { get; set; } = new List<ScheduleDiffItem>();
}

/// <summary>A single field-level difference between the baseline and live schedule.</summary>
[DataContract]
public sealed class ScheduleDiffItem
{
    /// <summary>Zero-based index of the row within the live schedule grid.</summary>
    [DataMember(Order = 1)]
    public int RowIndex { get; set; }

    /// <summary>
    /// Classification of the change.
    /// One of: <c>added</c>, <c>removed</c>, <c>modified</c>, <c>unchanged</c>.
    /// </summary>
    [DataMember(Order = 2)]
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>Name of the schedule field (column heading) that changed.</summary>
    [DataMember(Order = 3)]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>Value of the field in the baseline snapshot. Empty string for added rows.</summary>
    [DataMember(Order = 4)]
    public string OldValue { get; set; } = string.Empty;

    /// <summary>Value of the field in the live schedule. Empty string for removed rows.</summary>
    [DataMember(Order = 5)]
    public string NewValue { get; set; } = string.Empty;
}
