using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

// ── Parameter & Data Management DTOs ──

[DataContract]
public sealed class SharedParameterListRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string GroupNameContains { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string NameContains { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class SharedParameterItem
{
    [DataMember(Order = 1)]
    public int Id { get; set; }

    [DataMember(Order = 2)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string GroupName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ParameterType { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string BindingType { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public List<string> BoundCategories { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public bool IsInstance { get; set; }
}

[DataContract]
public sealed class SharedParameterListResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int Count { get; set; }

    [DataMember(Order = 3)]
    public List<SharedParameterItem> Parameters { get; set; } = new List<SharedParameterItem>();
}

[DataContract]
public sealed class CopyParametersBetweenRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int SourceElementId { get; set; }

    [DataMember(Order = 3)]
    public List<int> TargetElementIds { get; set; } = new List<int>();

    [DataMember(Order = 4)]
    public List<string> ParameterNames { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public bool SkipReadOnly { get; set; } = true;
}

[DataContract]
public sealed class AddSharedParameterRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string GroupName { get; set; } = "Data";

    [DataMember(Order = 4)]
    public string ParameterType { get; set; } = "Text";

    [DataMember(Order = 5)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public bool IsInstance { get; set; } = true;

    [DataMember(Order = 7)]
    public string SharedParameterFilePath { get; set; } = string.Empty;
}

[DataContract]
public sealed class BatchFillParameterRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string FillValue { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string FillMode { get; set; } = "OnlyEmpty";

    [DataMember(Order = 5)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 6)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public string FormatPattern { get; set; } = string.Empty;
}

[DataContract]
public sealed class DataExportRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> CategoryNames { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public List<string> ParameterNames { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public string Format { get; set; } = "json";

    [DataMember(Order = 5)]
    public string OutputPath { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public int MaxResults { get; set; } = 5000;

    [DataMember(Order = 7)]
    public string FilterParameterName { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string FilterValue { get; set; } = string.Empty;
}

[DataContract]
public sealed class DataExportItem
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string Category { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();
}

[DataContract]
public sealed class DataExportResult
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int Count { get; set; }

    [DataMember(Order = 3)]
    public string Format { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string OutputPath { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<DataExportItem> Items { get; set; } = new List<DataExportItem>();
}

[DataContract]
public sealed class ExportScheduleRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int ScheduleId { get; set; }

    [DataMember(Order = 3)]
    public string ScheduleName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Format { get; set; } = "json";
}

[DataContract]
public sealed class ScheduleExportResult
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ScheduleName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public List<string> ColumnHeaders { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public List<List<string>> Rows { get; set; } = new List<List<string>>();

    [DataMember(Order = 5)]
    public int RowCount { get; set; }
}

[DataContract]
public sealed class DataImportPreviewRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string InputPath { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Format { get; set; } = "json";

    [DataMember(Order = 4)]
    public string MatchParameterName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int MaxPreviewRows { get; set; } = 20;
}

[DataContract]
public sealed class DataImportPreviewResult
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalRows { get; set; }

    [DataMember(Order = 3)]
    public int MatchedElements { get; set; }

    [DataMember(Order = 4)]
    public int UnmatchedRows { get; set; }

    [DataMember(Order = 5)]
    public List<string> ParameterNames { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> Warnings { get; set; } = new List<string>();
}

[DataContract]
public sealed class DataImportRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string InputPath { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Format { get; set; } = "json";

    [DataMember(Order = 4)]
    public string MatchParameterName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool SkipReadOnly { get; set; } = true;
}
