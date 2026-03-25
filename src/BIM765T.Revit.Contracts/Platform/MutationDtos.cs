using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class ParameterUpdateItem
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string NewValue { get; set; } = string.Empty;
}

[DataContract]
public sealed class SetParametersRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<ParameterUpdateItem> Changes { get; set; } = new List<ParameterUpdateItem>();
}

[DataContract]
public sealed class DeleteElementsRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 3)]
    public bool AllowDependentDeletes { get; set; }
}

[DataContract]
public sealed class PlaceFamilyInstanceRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int FamilySymbolId { get; set; }

    [DataMember(Order = 3)]
    public int? LevelId { get; set; }

    [DataMember(Order = 4)]
    public int? ViewId { get; set; }

    [DataMember(Order = 5)]
    public int? HostElementId { get; set; }

    [DataMember(Order = 6)]
    public string PlacementMode { get; set; } = "auto";

    [DataMember(Order = 7)]
    public string StructuralTypeName { get; set; } = "NonStructural";

    [DataMember(Order = 8)]
    public double X { get; set; }

    [DataMember(Order = 9)]
    public double Y { get; set; }

    [DataMember(Order = 10)]
    public double Z { get; set; }

    [DataMember(Order = 11, EmitDefaultValue = false)]
    public double? StartX { get; set; }

    [DataMember(Order = 12, EmitDefaultValue = false)]
    public double? StartY { get; set; }

    [DataMember(Order = 13, EmitDefaultValue = false)]
    public double? StartZ { get; set; }

    [DataMember(Order = 14, EmitDefaultValue = false)]
    public double? EndX { get; set; }

    [DataMember(Order = 15, EmitDefaultValue = false)]
    public double? EndY { get; set; }

    [DataMember(Order = 16, EmitDefaultValue = false)]
    public double? EndZ { get; set; }

    [DataMember(Order = 17, EmitDefaultValue = false)]
    public double? FaceNormalX { get; set; }

    [DataMember(Order = 18, EmitDefaultValue = false)]
    public double? FaceNormalY { get; set; }

    [DataMember(Order = 19, EmitDefaultValue = false)]
    public double? FaceNormalZ { get; set; }

    [DataMember(Order = 20, EmitDefaultValue = false)]
    public double? ReferenceDirectionX { get; set; }

    [DataMember(Order = 21, EmitDefaultValue = false)]
    public double? ReferenceDirectionY { get; set; }

    [DataMember(Order = 22, EmitDefaultValue = false)]
    public double? ReferenceDirectionZ { get; set; }

    [DataMember(Order = 23)]
    public double RotateRadians { get; set; }

    [DataMember(Order = 24)]
    public string Notes { get; set; } = string.Empty;
}

[DataContract]
public sealed class MoveElementsRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 3)]
    public double DeltaX { get; set; }

    [DataMember(Order = 4)]
    public double DeltaY { get; set; }

    [DataMember(Order = 5)]
    public double DeltaZ { get; set; }
}

[DataContract]
public sealed class RotateElementsRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 3)]
    public double AngleDegrees { get; set; }

    [DataMember(Order = 4)]
    public string AxisMode { get; set; } = "element_basis_z";

    [DataMember(Order = 5, EmitDefaultValue = false)]
    public double? AxisOriginX { get; set; }

    [DataMember(Order = 6, EmitDefaultValue = false)]
    public double? AxisOriginY { get; set; }

    [DataMember(Order = 7, EmitDefaultValue = false)]
    public double? AxisOriginZ { get; set; }

    [DataMember(Order = 8, EmitDefaultValue = false)]
    public double? AxisDirectionX { get; set; }

    [DataMember(Order = 9, EmitDefaultValue = false)]
    public double? AxisDirectionY { get; set; }

    [DataMember(Order = 10, EmitDefaultValue = false)]
    public double? AxisDirectionZ { get; set; }
}

[DataContract]
public sealed class ParameterChangeRecord
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string ParameterName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string BeforeValue { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string AfterValue { get; set; } = string.Empty;
}

[DataContract]
public sealed class DiffSummary
{
    [DataMember(Order = 1)]
    public List<int> CreatedIds { get; set; } = new List<int>();

    [DataMember(Order = 2)]
    public List<int> ModifiedIds { get; set; } = new List<int>();

    [DataMember(Order = 3)]
    public List<int> DeletedIds { get; set; } = new List<int>();

    [DataMember(Order = 4)]
    public List<ParameterChangeRecord> ParameterChanges { get; set; } = new List<ParameterChangeRecord>();

    [DataMember(Order = 5)]
    public int WarningDelta { get; set; }
}

[DataContract]
public sealed class ExecutionResult
{
    [DataMember(Order = 1)]
    public string OperationName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool DryRun { get; set; }

    [DataMember(Order = 3)]
    public bool ConfirmationRequired { get; set; }

    [DataMember(Order = 4)]
    public string ApprovalToken { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<int> ChangedIds { get; set; } = new List<int>();

    [DataMember(Order = 6)]
    public DiffSummary DiffSummary { get; set; } = new DiffSummary();

    [DataMember(Order = 7)]
    public ReviewReport? ReviewSummary { get; set; }

    [DataMember(Order = 8)]
    public List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();

    [DataMember(Order = 9)]
    public List<string> Artifacts { get; set; } = new List<string>();

    [DataMember(Order = 10)]
    public string PreviewRunId { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public ContextFingerprint? ResolvedContext { get; set; }
}
