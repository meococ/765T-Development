using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Platform;

// ══════════════════════════════════════════════════════════════
// Phase 2: Smart View Template & Sheet Analysis DTOs
// 6 tools — tất cả read-only, phân tích thông minh cho BIM Manager
// ══════════════════════════════════════════════════════════════

// ── 1. audit.template_health ─────────────────────────────────

[DataContract]
public sealed class TemplateHealthRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>Filter templates by name substring.</summary>
    [DataMember(Order = 2)]
    public string NameContains { get; set; } = string.Empty;

    /// <summary>Filter by ViewType (e.g., "FloorPlan", "Section").</summary>
    [DataMember(Order = 3)]
    public string ViewType { get; set; } = string.Empty;

    /// <summary>Regex for naming convention check. Empty = skip naming audit.</summary>
    [DataMember(Order = 4)]
    public string NamingPattern { get; set; } = string.Empty;

    /// <summary>0.0–1.0 Levenshtein similarity threshold for duplicate detection. Default 0.85.</summary>
    [DataMember(Order = 5)]
    public double DuplicateSimilarityThreshold { get; set; } = 0.85;

    [DataMember(Order = 6)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class TemplateHealthResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    // ── Summary metrics ──

    [DataMember(Order = 2)]
    public int TotalTemplates { get; set; }

    [DataMember(Order = 3)]
    public int UsedTemplates { get; set; }

    [DataMember(Order = 4)]
    public int UnusedTemplates { get; set; }

    /// <summary>0–100 percentage of unused templates.</summary>
    [DataMember(Order = 5)]
    public double UnusedPercentage { get; set; }

    [DataMember(Order = 6)]
    public int SuspectDuplicateGroups { get; set; }

    [DataMember(Order = 7)]
    public int NamingViolationCount { get; set; }

    /// <summary>A/B/C/D/F grade based on scoring rules.</summary>
    [DataMember(Order = 8)]
    public string OverallGrade { get; set; } = string.Empty;

    // ── Breakdown ──

    [DataMember(Order = 9)]
    public List<TemplateTypeBreakdown> ByViewType { get; set; } = new List<TemplateTypeBreakdown>();

    [DataMember(Order = 10)]
    public List<UnusedTemplateItem> UnusedTemplatesList { get; set; } = new List<UnusedTemplateItem>();

    [DataMember(Order = 11)]
    public List<TemplateDuplicateGroup> DuplicateGroups { get; set; } = new List<TemplateDuplicateGroup>();

    [DataMember(Order = 12)]
    public List<TemplateNamingViolation> NamingViolations { get; set; } = new List<TemplateNamingViolation>();

    [DataMember(Order = 13)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}

[DataContract]
public sealed class TemplateTypeBreakdown
{
    [DataMember(Order = 1)]
    public string ViewType { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalCount { get; set; }

    [DataMember(Order = 3)]
    public int UsedCount { get; set; }

    [DataMember(Order = 4)]
    public int UnusedCount { get; set; }
}

[DataContract]
public sealed class UnusedTemplateItem
{
    [DataMember(Order = 1)]
    public int TemplateId { get; set; }

    [DataMember(Order = 2)]
    public string TemplateName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ViewType { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int FilterCount { get; set; }

    /// <summary>"safe_to_delete" | "review_first" | "keep_as_standard"</summary>
    [DataMember(Order = 5)]
    public string SuggestedAction { get; set; } = string.Empty;
}

[DataContract]
public sealed class TemplateDuplicateGroup
{
    [DataMember(Order = 1)]
    public List<TemplateDuplicateCandidate> Templates { get; set; } = new List<TemplateDuplicateCandidate>();

    [DataMember(Order = 2)]
    public double SimilarityScore { get; set; }

    [DataMember(Order = 3)]
    public string DifferenceDescription { get; set; } = string.Empty;
}

[DataContract]
public sealed class TemplateDuplicateCandidate
{
    [DataMember(Order = 1)]
    public int TemplateId { get; set; }

    [DataMember(Order = 2)]
    public string TemplateName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int UsageCount { get; set; }
}

[DataContract]
public sealed class TemplateNamingViolation
{
    [DataMember(Order = 1)]
    public int TemplateId { get; set; }

    [DataMember(Order = 2)]
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>"copy_suffix" | "invalid_characters" | "too_short" | "pattern_mismatch"</summary>
    [DataMember(Order = 3)]
    public string Violation { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string SuggestedName { get; set; } = string.Empty;
}

// ── 2. audit.sheet_organization ──────────────────────────────

[DataContract]
public sealed class SheetOrganizationRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>Regex to group sheets. Empty = auto-detect by common prefix.</summary>
    [DataMember(Order = 2)]
    public string GroupByPattern { get; set; } = string.Empty;

    /// <summary>Sheets with ≥ this many viewports flagged as heavy. Default 10.</summary>
    [DataMember(Order = 3)]
    public int HeavySheetThreshold { get; set; } = 10;

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 500;
}

[DataContract]
public sealed class SheetOrganizationResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalSheets { get; set; }

    [DataMember(Order = 3)]
    public int EmptySheetCount { get; set; }

    [DataMember(Order = 4)]
    public int HeavySheetCount { get; set; }

    [DataMember(Order = 5)]
    public int MissingTitleBlockCount { get; set; }

    [DataMember(Order = 6)]
    public double AverageViewportsPerSheet { get; set; }

    [DataMember(Order = 7)]
    public int MaxViewportsOnSingleSheet { get; set; }

    [DataMember(Order = 8)]
    public string OverallGrade { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public List<SheetGroupSummaryItem> Groups { get; set; } = new List<SheetGroupSummaryItem>();

    [DataMember(Order = 10)]
    public List<SheetOrganizationIssue> Issues { get; set; } = new List<SheetOrganizationIssue>();

    [DataMember(Order = 11)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}

[DataContract]
public sealed class SheetGroupSummaryItem
{
    [DataMember(Order = 1)]
    public string GroupPrefix { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int SheetCount { get; set; }

    [DataMember(Order = 3)]
    public int TotalViewports { get; set; }

    [DataMember(Order = 4)]
    public int EmptySheetCount { get; set; }

    [DataMember(Order = 5)]
    public List<string> SampleSheetNumbers { get; set; } = new List<string>();
}

[DataContract]
public sealed class SheetOrganizationIssue
{
    [DataMember(Order = 1)]
    public int SheetId { get; set; }

    [DataMember(Order = 2)]
    public string SheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SheetName { get; set; } = string.Empty;

    /// <summary>"EMPTY_SHEET" | "HEAVY_SHEET" | "NO_TITLEBLOCK" | "UNNAMED"</summary>
    [DataMember(Order = 4)]
    public string IssueCode { get; set; } = string.Empty;

    /// <summary>"Error" | "Warning" | "Info"</summary>
    [DataMember(Order = 5)]
    public string Severity { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 7)]
    public int ViewportCount { get; set; }
}

// ── 3. audit.template_sheet_map ──────────────────────────────

[DataContract]
public sealed class TemplateSheetMapRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string TemplateNameContains { get; set; } = string.Empty;

    /// <summary>If true, only return chains with issues (broken/orphan).</summary>
    [DataMember(Order = 3)]
    public bool OnlyBrokenChains { get; set; }

    [DataMember(Order = 4)]
    public int MaxResults { get; set; } = 200;
}

[DataContract]
public sealed class TemplateSheetMapResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int TotalTemplatesAnalyzed { get; set; }

    /// <summary>Templates whose views are all placed on sheets.</summary>
    [DataMember(Order = 3)]
    public int TemplatesWithCompleteChain { get; set; }

    /// <summary>Templates with views not placed on any sheet.</summary>
    [DataMember(Order = 4)]
    public int TemplatesWithBrokenChain { get; set; }

    /// <summary>Templates not used by any view.</summary>
    [DataMember(Order = 5)]
    public int OrphanTemplates { get; set; }

    /// <summary>Views on sheets that have no template assigned.</summary>
    [DataMember(Order = 6)]
    public int ViewsWithNoTemplate { get; set; }

    [DataMember(Order = 7)]
    public List<TemplateChainItem> Chains { get; set; } = new List<TemplateChainItem>();

    [DataMember(Order = 8)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}

[DataContract]
public sealed class TemplateChainItem
{
    [DataMember(Order = 1)]
    public int TemplateId { get; set; }

    [DataMember(Order = 2)]
    public string TemplateName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ViewType { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int ViewCount { get; set; }

    [DataMember(Order = 5)]
    public int ViewsOnSheet { get; set; }

    [DataMember(Order = 6)]
    public int ViewsNotOnSheet { get; set; }

    /// <summary>"Complete" | "Partial" | "Orphan"</summary>
    [DataMember(Order = 7)]
    public string ChainStatus { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public List<string> SheetNumbers { get; set; } = new List<string>();
}

// ── 4. view.template_inspect ─────────────────────────────────

[DataContract]
public sealed class TemplateInspectRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public int? TemplateId { get; set; }

    [DataMember(Order = 3)]
    public string TemplateName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IncludeFilterDetails { get; set; } = true;

    [DataMember(Order = 5)]
    public bool IncludeControlledParameters { get; set; } = true;

    [DataMember(Order = 6)]
    public int MaxViewSamples { get; set; } = 20;
}

[DataContract]
public sealed class TemplateInspectResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    // ── Template identity ──

    [DataMember(Order = 2)]
    public int TemplateId { get; set; }

    [DataMember(Order = 3)]
    public string TemplateName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ViewType { get; set; } = string.Empty;

    /// <summary>Architectural, Structural, Mechanical, Electrical, Coordination, or empty.</summary>
    [DataMember(Order = 5)]
    public string Discipline { get; set; } = string.Empty;

    // ── Template settings ──

    [DataMember(Order = 6)]
    public int ControlledParameterCount { get; set; }

    [DataMember(Order = 7)]
    public List<string> ControlledParameterNames { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public string DetailLevel { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public int? Scale { get; set; }

    // ── Filters ──

    [DataMember(Order = 10)]
    public int FilterCount { get; set; }

    [DataMember(Order = 11)]
    public List<TemplateFilterDetail> Filters { get; set; } = new List<TemplateFilterDetail>();

    // ── Usage ──

    [DataMember(Order = 12)]
    public int UsageCount { get; set; }

    [DataMember(Order = 13)]
    public List<TemplateViewUsageItem> ViewUsages { get; set; } = new List<TemplateViewUsageItem>();

    // ── AI Explanations ──

    [DataMember(Order = 14)]
    public List<string> Explanations { get; set; } = new List<string>();
}

[DataContract]
public sealed class TemplateFilterDetail
{
    [DataMember(Order = 1)]
    public int FilterId { get; set; }

    [DataMember(Order = 2)]
    public string FilterName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public bool IsEnabled { get; set; }

    [DataMember(Order = 4)]
    public bool IsVisible { get; set; }

    /// <summary>Human-readable override summary, e.g., "Red projection lines, 50% transparency".</summary>
    [DataMember(Order = 5)]
    public string OverrideSummary { get; set; } = string.Empty;
}

[DataContract]
public sealed class TemplateViewUsageItem
{
    [DataMember(Order = 1)]
    public int ViewId { get; set; }

    [DataMember(Order = 2)]
    public string ViewName { get; set; } = string.Empty;

    /// <summary>Sheet number if placed, empty if not on any sheet.</summary>
    [DataMember(Order = 3)]
    public string PlacedOnSheet { get; set; } = string.Empty;
}

// ── 5. sheet.group_summary ───────────────────────────────────

[DataContract]
public sealed class SheetGroupDetailRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>Prefix to match, e.g., "SR-QQ-T".</summary>
    [DataMember(Order = 2)]
    public string SheetNumberPrefix { get; set; } = string.Empty;

    /// <summary>Regex alternative to prefix matching.</summary>
    [DataMember(Order = 3)]
    public string SheetNumberPattern { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool IncludeTemplateUsage { get; set; } = true;

    [DataMember(Order = 5)]
    public int MaxResults { get; set; } = 100;
}

[DataContract]
public sealed class SheetGroupDetailResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string GroupIdentifier { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public int SheetCount { get; set; }

    [DataMember(Order = 4)]
    public int TotalViewports { get; set; }

    [DataMember(Order = 5)]
    public int UniqueTemplatesUsed { get; set; }

    [DataMember(Order = 6)]
    public int ViewsWithoutTemplate { get; set; }

    [DataMember(Order = 7)]
    public double AverageViewportsPerSheet { get; set; }

    [DataMember(Order = 8)]
    public List<SheetGroupMemberItem> Sheets { get; set; } = new List<SheetGroupMemberItem>();

    [DataMember(Order = 9)]
    public List<TemplateUsageInGroup> TemplateUsages { get; set; } = new List<TemplateUsageInGroup>();

    [DataMember(Order = 10)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}

[DataContract]
public sealed class SheetGroupMemberItem
{
    [DataMember(Order = 1)]
    public int SheetId { get; set; }

    [DataMember(Order = 2)]
    public string SheetNumber { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SheetName { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public int ViewportCount { get; set; }

    [DataMember(Order = 5)]
    public string TitleBlockName { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public List<string> IssueFlags { get; set; } = new List<string>();
}

[DataContract]
public sealed class TemplateUsageInGroup
{
    [DataMember(Order = 1)]
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>Views using this template that are placed on sheets in this group.</summary>
    [DataMember(Order = 2)]
    public int ViewCount { get; set; }

    /// <summary>Sheets in this group where this template appears via viewports.</summary>
    [DataMember(Order = 3)]
    public int SheetCount { get; set; }
}

// ── 6. audit.view_template_compliance ────────────────────────

[DataContract]
public sealed class ViewTemplateComplianceRequest
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public bool IncludeTemplateHealth { get; set; } = true;

    [DataMember(Order = 3)]
    public bool IncludeSheetOrganization { get; set; } = true;

    [DataMember(Order = 4)]
    public bool IncludeChainAnalysis { get; set; } = true;

    /// <summary>Regex for naming convention. Empty = use default copy/invalid-char checks only.</summary>
    [DataMember(Order = 5)]
    public string NamingPattern { get; set; } = string.Empty;
}

[DataContract]
public sealed class ViewTemplateComplianceResponse
{
    [DataMember(Order = 1)]
    public string DocumentKey { get; set; } = string.Empty;

    /// <summary>A/B/C/D/F.</summary>
    [DataMember(Order = 2)]
    public string OverallGrade { get; set; } = string.Empty;

    /// <summary>0–100 composite score.</summary>
    [DataMember(Order = 3)]
    public int OverallScore { get; set; }

    [DataMember(Order = 4)]
    public int TotalIssues { get; set; }

    [DataMember(Order = 5)]
    public int CriticalIssues { get; set; }

    [DataMember(Order = 6)]
    public List<ComplianceSectionResult> Sections { get; set; } = new List<ComplianceSectionResult>();

    /// <summary>Top 5 actionable recommendations ranked by impact.</summary>
    [DataMember(Order = 7)]
    public List<string> TopRecommendations { get; set; } = new List<string>();

    [DataMember(Order = 8)]
    public string Timestamp { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public ReviewReport Review { get; set; } = new ReviewReport();
}

// ComplianceSectionResult is defined in AuditDtos.cs — reused here.
// Extended with Score + MaxPoints fields (added to original DTO).
