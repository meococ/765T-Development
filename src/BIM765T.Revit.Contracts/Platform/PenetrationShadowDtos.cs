using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class PenetrationInventoryRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string FamilyName { get; set; } = "Penetration Alpha";

    [DataMember(Order = 3)]
    public int? ViewId { get; set; }

    [DataMember(Order = 4)]
    public string ViewName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 5000;

    [DataMember(Order = 6)]
    public bool IncludeAxisStatus { get; set; } = true;
}

[DataContract]
public sealed class PenetrationInventoryItemDto
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string UniqueId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string LevelName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public int? HostElementId { get; set; }

    [DataMember(Order = 7)]
    public string HostCategoryName { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string MiiDiameter { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string MiiDimLength { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string MiiElementClass { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public string MiiElementTier { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public string Mark { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public string AxisStatus { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public string AxisReason { get; set; } = string.Empty;
}

[DataContract]
public sealed class PenetrationInventoryGroupDto
{
    [DataMember(Order = 1)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string MiiDiameter { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string MiiDimLength { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string MiiElementClass { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string MiiElementTier { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public int Count { get; set; }
}

[DataContract]
public sealed class PenetrationInventoryResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int Count { get; set; }

    [DataMember(Order = 4)]
    public List<PenetrationInventoryItemDto> Items { get; set; } = new List<PenetrationInventoryItemDto>();

    [DataMember(Order = 5)]
    public List<PenetrationInventoryGroupDto> Groups { get; set; } = new List<PenetrationInventoryGroupDto>();

    [DataMember(Order = 6)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}

[DataContract]
public sealed class CreatePenetrationInventoryScheduleRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string FamilyName { get; set; } = "Penetration Alpha";

    [DataMember(Order = 3)]
    public string ScheduleName { get; set; } = "BIM765T_PenetrationAlpha_Inventory";

    [DataMember(Order = 4)]
    public bool OverwriteIfExists { get; set; } = true;

    [DataMember(Order = 5)]
    public bool Itemized { get; set; } = true;
}

/// <summary>
/// Request tạo schedule inventory cho tất cả Round family instances trong dự án.
/// Filter theo FamilyName (case-insensitive) trên category Generic Model.
/// </summary>
[DataContract]
public sealed class CreateRoundInventoryScheduleRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>
    /// Tên family Round cần lọc (case-insensitive). Mặc định "Round".
    /// </summary>
    [DataMember(Order = 2)]
    public string FamilyName { get; set; } = "Round";

    [DataMember(Order = 3)]
    public string ScheduleName { get; set; } = "BIM765T_Round_Inventory";

    [DataMember(Order = 4)]
    public bool OverwriteIfExists { get; set; } = true;

    [DataMember(Order = 5)]
    public bool Itemized { get; set; } = true;

    /// <summary>
    /// Khi true, schedule bao gồm cả Round instances trong linked files.
    /// </summary>
    [DataMember(Order = 6)]
    public bool IncludeLinkedFiles { get; set; } = false;
}

[DataContract]
public sealed class PenetrationRoundShadowPlanRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string SourceFamilyName { get; set; } = "Penetration Alpha";

    [DataMember(Order = 3)]
    public string RoundFamilyName { get; set; } = "Round";

    [DataMember(Order = 4)]
    public string PreferredReferenceMark { get; set; } = "test";

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 5000;
}

[DataContract]
public sealed class PenetrationRoundShadowPlanItemDto
{
    [DataMember(Order = 1)]
    public int SourceElementId { get; set; }

    [DataMember(Order = 2)]
    public string SourceTypeName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string MiiDiameter { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string MiiDimLength { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string MiiElementClass { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string MiiElementTier { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public int RoundSymbolId { get; set; }

    [DataMember(Order = 8)]
    public string RoundTypeName { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public int? ReferenceRoundElementId { get; set; }

    [DataMember(Order = 10)]
    public string ReferenceAxisStatus { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public bool CanCreateShadow { get; set; }

    [DataMember(Order = 12)]
    public string Notes { get; set; } = string.Empty;
}

[DataContract]
public sealed class PenetrationRoundShadowPlanResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string SourceFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string RoundFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int Count { get; set; }

    [DataMember(Order = 5)]
    public int CreatableCount { get; set; }

    [DataMember(Order = 6)]
    public List<PenetrationRoundShadowPlanItemDto> Items { get; set; } = new List<PenetrationRoundShadowPlanItemDto>();

    [DataMember(Order = 7)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}

[DataContract]
public sealed class RoundExternalizationPlanRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ParentFamilyName { get; set; } = "Penetration Alpha";

    [DataMember(Order = 3)]
    public string RoundFamilyName { get; set; } = "Round";

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 10000;

    [DataMember(Order = 5)]
    public double AngleToleranceDegrees { get; set; } = 5.0;

    [DataMember(Order = 6)]
    public bool RequireParentFamilyMatch { get; set; } = true;

    [DataMember(Order = 7)]
    public string TraceCommentPrefix { get; set; } = "BIM765T_EXTERNAL_ROUND";

    [DataMember(Order = 8)]
    public string PlanWrapperFamilyName { get; set; } = "Round_Project";

    [DataMember(Order = 9)]
    public string PlanWrapperTypeName { get; set; } = "AXIS_X";

    [DataMember(Order = 10)]
    public string ElevXWrapperFamilyName { get; set; } = "Round_Project";

    [DataMember(Order = 11)]
    public string ElevXWrapperTypeName { get; set; } = "AXIS_Z";

    [DataMember(Order = 12)]
    public string ElevYWrapperFamilyName { get; set; } = "Round_Project";

    [DataMember(Order = 13)]
    public string ElevYWrapperTypeName { get; set; } = "AXIS_Y";
}

[DataContract]
public sealed class RoundExternalizationPlanItemDto
{
    [DataMember(Order = 1)]
    public int RoundElementId { get; set; }

    [DataMember(Order = 2)]
    public string RoundUniqueId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string RoundFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string RoundTypeName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string RoundPlacementType { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public bool Mirrored { get; set; }

    [DataMember(Order = 7)]
    public string RoundStatus { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string RoundReason { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public bool CanExternalize { get; set; }

    [DataMember(Order = 10)]
    public string Notes { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public int? ParentElementId { get; set; }

    [DataMember(Order = 12)]
    public string ParentFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public string ParentTypeName { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public string ParentCategoryName { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public string ParentMiiDiameter { get; set; } = string.Empty;

    [DataMember(Order = 16)]
    public string ParentMiiDimLength { get; set; } = string.Empty;

    [DataMember(Order = 17)]
    public string ParentMiiElementClass { get; set; } = string.Empty;

    [DataMember(Order = 18)]
    public string ParentMiiElementTier { get; set; } = string.Empty;

    [DataMember(Order = 19)]
    public string ParentMark { get; set; } = string.Empty;

    [DataMember(Order = 20)]
    public string ProposedPlacementMode { get; set; } = string.Empty;

    [DataMember(Order = 21)]
    public string PlacementNote { get; set; } = string.Empty;

    [DataMember(Order = 22)]
    public string ProposedTargetFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 23)]
    public string ProposedTargetTypeName { get; set; } = string.Empty;

    [DataMember(Order = 24)]
    public string SuggestedTraceComment { get; set; } = string.Empty;

    [DataMember(Order = 25)]
    public AxisVectorDto Origin { get; set; } = new AxisVectorDto();

    [DataMember(Order = 26)]
    public AxisVectorDto BasisX { get; set; } = new AxisVectorDto();

    [DataMember(Order = 27)]
    public AxisVectorDto BasisY { get; set; } = new AxisVectorDto();

    [DataMember(Order = 28)]
    public AxisVectorDto BasisZ { get; set; } = new AxisVectorDto();

    [DataMember(Order = 29)]
    public double RotationAroundProjectZDegrees { get; set; }

    [DataMember(Order = 30)]
    public double AngleXDegrees { get; set; }

    [DataMember(Order = 31)]
    public double AngleYDegrees { get; set; }

    [DataMember(Order = 32)]
    public double AngleZDegrees { get; set; }
}

[DataContract]
public sealed class RoundExternalizationModeSummaryDto
{
    [DataMember(Order = 1)]
    public string ProposedPlacementMode { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ProposedTargetFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ProposedTargetTypeName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int Count { get; set; }
}

[DataContract]
public sealed class RoundExternalizationTypeSummaryDto
{
    [DataMember(Order = 1)]
    public string ParentFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ParentTypeName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ProposedPlacementMode { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ProposedTargetFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string ProposedTargetTypeName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public int Count { get; set; }
}

[DataContract]
public sealed class RoundExternalizationPlanResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DocumentTitle { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ParentFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string RoundFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int TotalRoundInstances { get; set; }

    [DataMember(Order = 6)]
    public int Count { get; set; }

    [DataMember(Order = 7)]
    public int EligibleCount { get; set; }

    [DataMember(Order = 8)]
    public int MissingParentCount { get; set; }

    [DataMember(Order = 9)]
    public int UnexpectedParentCount { get; set; }

    [DataMember(Order = 10)]
    public int MissingTransformCount { get; set; }

    [DataMember(Order = 11)]
    public int UniqueParentInstanceCount { get; set; }

    [DataMember(Order = 12)]
    public bool Truncated { get; set; }

    [DataMember(Order = 13)]
    public List<RoundExternalizationModeSummaryDto> ModeSummary { get; set; } = new List<RoundExternalizationModeSummaryDto>();

    [DataMember(Order = 14)]
    public List<RoundExternalizationTypeSummaryDto> TypeSummary { get; set; } = new List<RoundExternalizationTypeSummaryDto>();

    [DataMember(Order = 15)]
    public List<RoundExternalizationPlanItemDto> Items { get; set; } = new List<RoundExternalizationPlanItemDto>();

    [DataMember(Order = 16)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}

[DataContract]
public sealed class BuildRoundProjectWrappersRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string SourceFamilyName { get; set; } = "Round";

    [DataMember(Order = 3)]
    public string OutputDirectory { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool OverwriteFamilyFiles { get; set; } = true;

    [DataMember(Order = 5)]
    public bool LoadIntoProject { get; set; } = true;

    [DataMember(Order = 6)]
    public bool OverwriteExistingProjectFamilies { get; set; } = true;

    [DataMember(Order = 7)]
    public string PlanWrapperFamilyName { get; set; } = "Round_Project";

    [DataMember(Order = 8)]
    public string PlanWrapperTypeName { get; set; } = "AXIS_X";

    [DataMember(Order = 9)]
    public string ElevXWrapperFamilyName { get; set; } = "Round_Project";

    [DataMember(Order = 10)]
    public string ElevXWrapperTypeName { get; set; } = "AXIS_Z";

    [DataMember(Order = 11)]
    public string ElevYWrapperFamilyName { get; set; } = "Round_Project";

    [DataMember(Order = 12)]
    public string ElevYWrapperTypeName { get; set; } = "AXIS_Y";

    [DataMember(Order = 13)]
    public bool GenerateSizeSpecificVariants { get; set; }
}

[DataContract]
public sealed class CreateRoundShadowBatchRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string SourceFamilyName { get; set; } = "Penetration Alpha";

    [DataMember(Order = 3)]
    public string RoundFamilyName { get; set; } = "Round";

    [DataMember(Order = 4)]
    public string PreferredReferenceMark { get; set; } = "test";

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 5000;

    [DataMember(Order = 6)]
    public List<int> SourceElementIds { get; set; } = new List<int>();

    [DataMember(Order = 7)]
    public int? ReferenceRoundElementId { get; set; }

    [DataMember(Order = 8)]
    public int? RoundSymbolId { get; set; }

    [DataMember(Order = 9)]
    public string TraceCommentPrefix { get; set; } = "BIM765T_SHADOW_ROUND";

    [DataMember(Order = 10)]
    public bool SetCommentsTrace { get; set; } = true;

    [DataMember(Order = 11)]
    public bool CopyDiameter { get; set; } = true;

    [DataMember(Order = 12)]
    public bool CopyLength { get; set; } = true;

    [DataMember(Order = 13)]
    public bool CopyElementClass { get; set; } = true;

    [DataMember(Order = 14)]
    public bool CopyElementTier { get; set; } = true;

    [DataMember(Order = 15)]
    public bool SkipIfTraceExists { get; set; } = true;

    [DataMember(Order = 16)]
    public string PlacementMode { get; set; } = "host_face_project_aligned";

    [DataMember(Order = 17)]
    public bool RequireAxisAlignedResult { get; set; } = true;
}

[DataContract]
public sealed class SyncPenetrationAlphaNestedTypesRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ParentFamilyName { get; set; } = "Penetration Alpha M";

    [DataMember(Order = 3)]
    public string NestedFamilyName { get; set; } = "Penetration Alpha";

    [DataMember(Order = 4)]
    public string ProjectDocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public bool ReloadIntoProject { get; set; } = true;

    [DataMember(Order = 6)]
    public bool OverwriteExistingProjectFamily { get; set; } = true;

    [DataMember(Order = 7)]
    public bool RequireSingleNestedInstance { get; set; } = true;

    [DataMember(Order = 8)]
    public string PreferredSeedTypeName { get; set; } = string.Empty;
}

[DataContract]
public sealed class RoundShadowCleanupRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TraceCommentPrefix { get; set; } = "BIM765T_SHADOW_ROUND";

    [DataMember(Order = 3)]
    public string JournalId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public List<int> ElementIds { get; set; } = new List<int>();

    [DataMember(Order = 5)]
    public bool UseLatestSuccessfulBatchWhenEmpty { get; set; } = true;

    [DataMember(Order = 6)]
    public bool RequireTraceCommentMatch { get; set; } = true;

    [DataMember(Order = 7)]
    public int MaxResults { get; set; } = 5000;
}

[DataContract]
public sealed class RoundShadowCleanupItemDto
{
    [DataMember(Order = 1)]
    public int ElementId { get; set; }

    [DataMember(Order = 2)]
    public string FamilyName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Comments { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public int? SourceElementId { get; set; }

    [DataMember(Order = 6)]
    public bool TraceMatched { get; set; }

    [DataMember(Order = 7)]
    public bool CanDelete { get; set; }

    [DataMember(Order = 8)]
    public int EstimatedDependentCount { get; set; }

    [DataMember(Order = 9)]
    public string Notes { get; set; } = string.Empty;
}

[DataContract]
public sealed class RoundShadowCleanupPlanResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TraceCommentPrefix { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string JournalId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int Count { get; set; }

    [DataMember(Order = 5)]
    public int DeletableCount { get; set; }

    [DataMember(Order = 6)]
    public List<RoundShadowCleanupItemDto> Items { get; set; } = new List<RoundShadowCleanupItemDto>();

    [DataMember(Order = 7)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}
