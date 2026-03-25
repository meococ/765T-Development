using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BIM765T.Revit.Contracts.Platform;

[DataContract]
public sealed class RoundPenetrationCutPlanRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TargetFamilyName { get; set; } = "Mii_Pen-Round_Project";

    [DataMember(Order = 3)]
    public List<string> SourceElementClasses { get; set; } = new List<string> { "PIP", "PPF", "PPG" };

    [DataMember(Order = 4)]
    public List<string> HostElementClasses { get; set; } = new List<string> { "GYB", "WFR" };

    [DataMember(Order = 5)]
    public List<string> SourceFamilyNameContains { get; set; } = new List<string> { "PIP", "PPF", "PPG" };

    [DataMember(Order = 6)]
    public List<int> SourceElementIds { get; set; } = new List<int>();

    [DataMember(Order = 7)]
    public double GybClearancePerSideInches { get; set; } = 0.25;

    [DataMember(Order = 8)]
    public double WfrClearancePerSideInches { get; set; } = 0.125;

    [DataMember(Order = 9)]
    public double AxisToleranceDegrees { get; set; } = 5.0;

    [DataMember(Order = 10)]
    public string TraceCommentPrefix { get; set; } = "BIM765T_PEN_ROUND";

    [DataMember(Order = 11)]
    public int MaxResults { get; set; } = 5000;

    [DataMember(Order = 12)]
    public bool IncludeExisting { get; set; } = true;
}

[DataContract]
public sealed class RoundPenetrationCutPlanItemDto
{
    [DataMember(Order = 1)]
    public int SourceElementId { get; set; }

    [DataMember(Order = 2)]
    public int HostElementId { get; set; }

    [DataMember(Order = 3)]
    public string SourceFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string SourceTypeName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string HostFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string HostTypeName { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string HostClass { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string CassetteId { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public AxisVectorDto Origin { get; set; } = new AxisVectorDto();

    [DataMember(Order = 10)]
    public AxisVectorDto BasisX { get; set; } = new AxisVectorDto();

    [DataMember(Order = 11)]
    public string NominalOD { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public double NominalODFeet { get; set; }

    [DataMember(Order = 13)]
    public double OpeningDiameterFeet { get; set; }

    [DataMember(Order = 14)]
    public double CutLengthFeet { get; set; }

    [DataMember(Order = 15)]
    public double ClearancePerSideFeet { get; set; }

    [DataMember(Order = 16)]
    public AxisVectorDto PlacementPoint { get; set; } = new AxisVectorDto();

    [DataMember(Order = 17)]
    public string TargetFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 18)]
    public string TypeName { get; set; } = string.Empty;

    [DataMember(Order = 19)]
    public int? ExistingPenetrationElementId { get; set; }

    [DataMember(Order = 20)]
    public bool CanPlace { get; set; }

    [DataMember(Order = 21)]
    public bool CanCut { get; set; }

    [DataMember(Order = 22)]
    public string TraceComment { get; set; } = string.Empty;

    [DataMember(Order = 23)]
    public string ResidualNote { get; set; } = string.Empty;
}

[DataContract]
public sealed class RoundPenetrationCutPlanResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TargetFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int Count { get; set; }

    [DataMember(Order = 4)]
    public int CreatableCount { get; set; }

    [DataMember(Order = 5)]
    public int ExistingCount { get; set; }

    [DataMember(Order = 6)]
    public int ResidualCount { get; set; }

    [DataMember(Order = 7)]
    public List<RoundPenetrationCutPlanItemDto> Items { get; set; } = new List<RoundPenetrationCutPlanItemDto>();

    [DataMember(Order = 8)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}

[DataContract]
public sealed class CreateRoundPenetrationCutBatchRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TargetFamilyName { get; set; } = "Mii_Pen-Round_Project";

    [DataMember(Order = 3)]
    public List<string> SourceElementClasses { get; set; } = new List<string> { "PIP", "PPF", "PPG" };

    [DataMember(Order = 4)]
    public List<string> HostElementClasses { get; set; } = new List<string> { "GYB", "WFR" };

    [DataMember(Order = 5)]
    public List<string> SourceFamilyNameContains { get; set; } = new List<string> { "PIP", "PPF", "PPG" };

    [DataMember(Order = 6)]
    public List<int> SourceElementIds { get; set; } = new List<int>();

    [DataMember(Order = 7)]
    public double GybClearancePerSideInches { get; set; } = 0.25;

    [DataMember(Order = 8)]
    public double WfrClearancePerSideInches { get; set; } = 0.125;

    [DataMember(Order = 9)]
    public double AxisToleranceDegrees { get; set; } = 5.0;

    [DataMember(Order = 10)]
    public string TraceCommentPrefix { get; set; } = "BIM765T_PEN_ROUND";

    [DataMember(Order = 11)]
    public int MaxResults { get; set; } = 5000;

    [DataMember(Order = 12)]
    public string OutputDirectory { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public bool OverwriteFamilyFiles { get; set; } = true;

    [DataMember(Order = 14)]
    public bool OverwriteExistingProjectFamilies { get; set; } = true;

    [DataMember(Order = 15)]
    public bool ForceRebuildFamilies { get; set; }

    [DataMember(Order = 16)]
    public bool SetCommentsTrace { get; set; } = true;

    [DataMember(Order = 17)]
    public bool RequireAxisAlignedResult { get; set; } = true;

    [DataMember(Order = 18)]
    public int MaxCutRetries { get; set; } = 2;

    [DataMember(Order = 19)]
    public int RetryBackoffMs { get; set; } = 150;

    [DataMember(Order = 20)]
    public bool ShowReviewBodyByDefault { get; set; }
}

[DataContract]
public sealed class RoundPenetrationCutQcRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TargetFamilyName { get; set; } = "Mii_Pen-Round_Project";

    [DataMember(Order = 3)]
    public List<string> SourceElementClasses { get; set; } = new List<string> { "PIP", "PPF", "PPG" };

    [DataMember(Order = 4)]
    public List<string> HostElementClasses { get; set; } = new List<string> { "GYB", "WFR" };

    [DataMember(Order = 5)]
    public List<string> SourceFamilyNameContains { get; set; } = new List<string> { "PIP", "PPF", "PPG" };

    [DataMember(Order = 6)]
    public List<int> SourceElementIds { get; set; } = new List<int>();

    [DataMember(Order = 7)]
    public double GybClearancePerSideInches { get; set; } = 0.25;

    [DataMember(Order = 8)]
    public double WfrClearancePerSideInches { get; set; } = 0.125;

    [DataMember(Order = 9)]
    public double AxisToleranceDegrees { get; set; } = 5.0;

    [DataMember(Order = 10)]
    public string TraceCommentPrefix { get; set; } = "BIM765T_PEN_ROUND";

    [DataMember(Order = 11)]
    public int MaxResults { get; set; } = 5000;
}

[DataContract]
public sealed class RoundPenetrationCutQcItemDto
{
    [DataMember(Order = 1)]
    public int SourceElementId { get; set; }

    [DataMember(Order = 2)]
    public int HostElementId { get; set; }

    [DataMember(Order = 3)]
    public int? PenetrationElementId { get; set; }

    [DataMember(Order = 4)]
    public string PenetrationFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string PenetrationTypeName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string HostClass { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public string CassetteId { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string Status { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string AxisStatus { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string CutStatus { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public string PlacementStatus { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public double PlacementDriftFeet { get; set; }

    [DataMember(Order = 13)]
    public string TraceComment { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public string ResidualNote { get; set; } = string.Empty;
}

[DataContract]
public sealed class RoundPenetrationCutQcResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TargetFamilyName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int Count { get; set; }

    [DataMember(Order = 4)]
    public int PlacedCount { get; set; }

    [DataMember(Order = 5)]
    public int CutSuccessCount { get; set; }

    [DataMember(Order = 6)]
    public int ResidualCount { get; set; }

    [DataMember(Order = 7)]
    public int OrphanCount { get; set; }

    [DataMember(Order = 8)]
    public List<RoundPenetrationCutQcItemDto> Items { get; set; } = new List<RoundPenetrationCutQcItemDto>();

    [DataMember(Order = 9)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}

[DataContract]
public sealed class RoundPenetrationReviewPacketRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TargetFamilyName { get; set; } = "Mii_Pen-Round_Project";

    [DataMember(Order = 3)]
    public List<string> SourceElementClasses { get; set; } = new List<string> { "PIP", "PPF", "PPG" };

    [DataMember(Order = 4)]
    public List<string> HostElementClasses { get; set; } = new List<string> { "GYB", "WFR" };

    [DataMember(Order = 5)]
    public List<string> SourceFamilyNameContains { get; set; } = new List<string> { "PIP", "PPF", "PPG" };

    [DataMember(Order = 6)]
    public List<int> SourceElementIds { get; set; } = new List<int>();

    [DataMember(Order = 7)]
    public List<int> PenetrationElementIds { get; set; } = new List<int>();

    [DataMember(Order = 8)]
    public double GybClearancePerSideInches { get; set; } = 0.25;

    [DataMember(Order = 9)]
    public double WfrClearancePerSideInches { get; set; } = 0.125;

    [DataMember(Order = 10)]
    public double AxisToleranceDegrees { get; set; } = 5.0;

    [DataMember(Order = 11)]
    public string TraceCommentPrefix { get; set; } = "BIM765T_PEN_ROUND";

    [DataMember(Order = 12)]
    public int MaxResults { get; set; } = 5000;

    [DataMember(Order = 13)]
    public int MaxItems { get; set; } = 6;

    [DataMember(Order = 14)]
    public string ViewNamePrefix { get; set; } = "BIM765T_RoundPen_Review";

    [DataMember(Order = 15)]
    public string SheetNumber { get; set; } = "BIM765T-RP-01";

    [DataMember(Order = 16)]
    public string SheetName { get; set; } = "Round Penetration Review";

    [DataMember(Order = 17)]
    public string TitleBlockTypeName { get; set; } = string.Empty;

    [DataMember(Order = 18)]
    public double SectionBoxPaddingFeet { get; set; } = 0.5;

    [DataMember(Order = 19)]
    public bool CopyActive3DOrientation { get; set; } = true;

    [DataMember(Order = 20)]
    public bool ReuseExistingViews { get; set; } = true;

    [DataMember(Order = 21)]
    public bool ReuseExistingSheet { get; set; } = true;

    [DataMember(Order = 22)]
    public bool ExportSheetImage { get; set; } = true;

    [DataMember(Order = 23)]
    public string ImageOutputPath { get; set; } = string.Empty;

    [DataMember(Order = 24)]
    public bool ActivateSheetAfterCreate { get; set; }

    [DataMember(Order = 25)]
    public bool IncludeOnlyNonOkQcItems { get; set; } = true;
}
