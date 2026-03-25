using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class NamedRootDto
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Path { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Kind { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool Exists { get; set; }
}

[DataContract]
public sealed class DeliveryPresetDescriptor
{
    [DataMember(Order = 1)]
    public string PresetName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Kind { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Source { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string NativeSetupName { get; set; } = string.Empty;
}

[DataContract]
public sealed class FamilyLibraryRootsResponse
{
    [DataMember(Order = 1)]
    public List<NamedRootDto> Roots { get; set; } = new List<NamedRootDto>();
}

[DataContract]
public sealed class DeliveryPresetCatalogResponse
{
    [DataMember(Order = 1)]
    public List<NamedRootDto> FamilyRoots { get; set; } = new List<NamedRootDto>();

    [DataMember(Order = 2)]
    public List<NamedRootDto> OutputRoots { get; set; } = new List<NamedRootDto>();

    [DataMember(Order = 3)]
    public List<DeliveryPresetDescriptor> Presets { get; set; } = new List<DeliveryPresetDescriptor>();
}

[DataContract]
public sealed class OutputTargetValidationRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string OperationKind { get; set; } = "export";

    [DataMember(Order = 3)]
    public string OutputRootName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string RelativePath { get; set; } = string.Empty;
}

[DataContract]
public sealed class OutputTargetValidationResponse
{
    [DataMember(Order = 1)]
    public string OperationKind { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string OutputRootName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ResolvedRootPath { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string RelativePath { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ResolvedPath { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public bool Allowed { get; set; }

    [DataMember(Order = 7)]
    public string Reason { get; set; } = string.Empty;
}

[DataContract]
public sealed class FamilyLoadRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string LibraryRootName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string RelativeFamilyPath { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<string> TypeNames { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public bool LoadAllSymbols { get; set; }

    [DataMember(Order = 6)]
    public bool OverwriteExisting { get; set; }
}

[DataContract]
public sealed class ScheduleFieldSpec
{
    [DataMember(Order = 1)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ColumnHeading { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool Hidden { get; set; }
}

[DataContract]
public sealed class ScheduleFilterSpec
{
    [DataMember(Order = 1)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Operator { get; set; } = "Equal";

    [DataMember(Order = 3)]
    public string Value { get; set; } = string.Empty;
}

[DataContract]
public sealed class ScheduleSortSpec
{
    [DataMember(Order = 1)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool Ascending { get; set; } = true;
}

[DataContract]
public sealed class ScheduleCreateRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ScheduleName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<ScheduleFieldSpec> Fields { get; set; } = new List<ScheduleFieldSpec>();

    [DataMember(Order = 5)]
    public List<ScheduleFilterSpec> Filters { get; set; } = new List<ScheduleFilterSpec>();

    [DataMember(Order = 6)]
    public List<ScheduleSortSpec> Sorts { get; set; } = new List<ScheduleSortSpec>();

    [DataMember(Order = 7)]
    public bool IsItemized { get; set; } = true;

    [DataMember(Order = 8)]
    public bool IncludeLinkedFiles { get; set; }

    [DataMember(Order = 9)]
    public bool ShowGrandTotal { get; set; }

    [DataMember(Order = 10)]
    public int MaxFieldCount { get; set; } = 25;
}

[DataContract]
public sealed class ScheduleCreatePreviewResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ScheduleName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string CategoryName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int? ResolvedCategoryId { get; set; }

    [DataMember(Order = 5)]
    public int? ExistingScheduleId { get; set; }

    [DataMember(Order = 6)]
    public List<string> FieldNames { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public int FilterCount { get; set; }

    [DataMember(Order = 8)]
    public int SortCount { get; set; }

    [DataMember(Order = 9)]
    public List<string> Warnings { get; set; } = new List<string>();
}

[DataContract]
public sealed class IfcExportRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PresetName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string OutputRootName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string RelativeOutputPath { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string FileName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public int? ViewId { get; set; }

    [DataMember(Order = 7)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public bool OverwriteExisting { get; set; }
}

[DataContract]
public sealed class DwgExportRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PresetName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string OutputRootName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string RelativeOutputPath { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string FileName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public List<int> ViewIds { get; set; } = new List<int>();

    [DataMember(Order = 7)]
    public List<int> SheetIds { get; set; } = new List<int>();

    [DataMember(Order = 8)]
    public bool UseActiveViewWhenEmpty { get; set; } = true;

    [DataMember(Order = 9)]
    public bool OverwriteExisting { get; set; }
}

[DataContract]
public sealed class PdfPrintRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string PresetName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string OutputRootName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string RelativeOutputPath { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string FileName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public List<int> SheetIds { get; set; } = new List<int>();

    [DataMember(Order = 7)]
    public List<string> SheetNumbers { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public bool Combine { get; set; } = true;

    [DataMember(Order = 9)]
    public bool OverwriteExisting { get; set; }
}
