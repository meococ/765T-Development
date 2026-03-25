using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class ScheduleExtractionRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int ScheduleId { get; set; }

    [DataMember(Order = 3)]
    public string ScheduleName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int MaxRows { get; set; } = 200;

    [DataMember(Order = 5)]
    public bool IncludeEmptyRows { get; set; }

    [DataMember(Order = 6)]
    public bool IncludeColumnMetadata { get; set; } = true;
}

[DataContract]
public sealed class ScheduleColumnInfo
{
    [DataMember(Order = 1)]
    public int Index { get; set; }

    [DataMember(Order = 2)]
    public string Key { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Heading { get; set; } = string.Empty;
}

[DataContract]
public sealed class ScheduleExtractionRow
{
    [DataMember(Order = 1)]
    public int RowIndex { get; set; }

    [DataMember(Order = 2)]
    public Dictionary<string, string> Cells { get; set; } = new Dictionary<string, string>();
}

[DataContract]
public sealed class ScheduleExtractionResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int ScheduleId { get; set; }

    [DataMember(Order = 3)]
    public string ScheduleName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool IsItemized { get; set; } = true;

    [DataMember(Order = 6)]
    public int ColumnCount { get; set; }

    [DataMember(Order = 7)]
    public int TotalRowCount { get; set; }

    [DataMember(Order = 8)]
    public int ReturnedRowCount { get; set; }

    [DataMember(Order = 9)]
    public List<ScheduleColumnInfo> Columns { get; set; } = new List<ScheduleColumnInfo>();

    [DataMember(Order = 10)]
    public List<ScheduleExtractionRow> Rows { get; set; } = new List<ScheduleExtractionRow>();

    [DataMember(Order = 11)]
    public List<string> FiltersApplied { get; set; } = new List<string>();

    [DataMember(Order = 12)]
    public string GroupingSummary { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public Dictionary<string, string> Totals { get; set; } = new Dictionary<string, string>();

    [DataMember(Order = 14)]
    public string Summary { get; set; } = string.Empty;
}
