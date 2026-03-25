using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure.Failures;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

/// <summary>
/// QC & Model Audit — kiểm tra naming, unused views/families, duplicates, warnings, model standards.
/// Flow đặc biệt: AI sẽ chạy audit → xem report → đề xuất fix → dry-run fix → execute.
/// Đây là competitive advantage lớn: pyRevit và Dynamo KHÔNG có AI-driven audit loop.
/// </summary>
internal sealed class AuditService
{
    // ── Naming Convention Audit ──
    internal NamingAuditResponse AuditNaming(PlatformServices services, Document doc, NamingAuditRequest request)
    {
        request ??= new NamingAuditRequest();
        var result = new NamingAuditResponse { DocumentKey = services.GetDocumentKey(doc) };
        Regex? pattern = null;
        if (!string.IsNullOrWhiteSpace(request.ExpectedPattern))
        {
            try { pattern = new Regex(request.ExpectedPattern, RegexOptions.IgnoreCase); }
            catch { /* invalid regex — skip pattern check */ }
        }

        var scope = (request.Scope ?? "views").ToLowerInvariant();
        IEnumerable<Element> elements;
        if (scope == "sheets")
            elements = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<Element>();
        else if (scope == "families")
            elements = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Element>();
        else
            elements = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate).Cast<Element>();

        foreach (var elem in elements.Take(Math.Max(1, request.MaxResults)))
        {
            result.TotalChecked++;
            var name = elem.Name ?? string.Empty;
            var violations = new List<string>();

            // Kiểm tra ký tự đặc biệt không hợp lệ
            if (name.IndexOfAny(new[] { '{', '}', '[', ']', '|', '\\' }) >= 0)
                violations.Add("Contains invalid characters ({, }, [, ], |, \\)");

            // Kiểm tra tên trùng mặc định (Copy, Copy 1...)
            if (Regex.IsMatch(name, @"Copy\s*\d*$", RegexOptions.IgnoreCase))
                violations.Add("Appears to be an uncleaned copy (contains 'Copy')");

            // Kiểm tra pattern nếu có
            if (pattern != null && !pattern.IsMatch(name))
                violations.Add($"Does not match expected pattern: {request.ExpectedPattern}");

            // Kiểm tra tên quá ngắn hoặc chỉ số
            if (name.Length < 3)
                violations.Add("Name too short (< 3 characters)");

            if (violations.Count > 0)
            {
                result.Violations.Add(new NamingViolationItem
                {
                    ElementId = checked((int)elem.Id.Value),
                    ElementName = name,
                    Category = elem.Category?.Name ?? scope,
                    Violation = string.Join("; ", violations),
                    SuggestedName = SuggestCleanName(name)
                });
            }
        }
        result.ViolationCount = result.Violations.Count;
        return result;
    }

    // ── Unused Views Audit ──
    internal UnusedViewsResponse AuditUnusedViews(PlatformServices services, Document doc, UnusedViewsRequest request)
    {
        request ??= new UnusedViewsRequest();
        var result = new UnusedViewsResponse { DocumentKey = services.GetDocumentKey(doc) };

        // Lấy tất cả view IDs đã đặt trên sheets
        var viewIdsOnSheets = new HashSet<long>();
        foreach (var sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
        {
            foreach (var vpId in sheet.GetAllViewports())
            {
                if (doc.GetElement(vpId) is Viewport vp)
                    viewIdsOnSheets.Add(vp.ViewId.Value);
            }
        }

        var allViews = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().ToList();
        result.TotalViews = allViews.Count(v => !v.IsTemplate);

        foreach (var view in allViews)
        {
            if (view.IsTemplate && request.ExcludeTemplates) continue;
            if (view is ViewSheet) continue; // sheets tự nó
            if (!request.IncludeSchedules && view is ViewSchedule) continue;
            if (!request.IncludeLegends && view.ViewType == ViewType.Legend) continue;
            // Browser Organization View, Project Browser views → skip
            if (view.ViewType == ViewType.ProjectBrowser || view.ViewType == ViewType.SystemBrowser) continue;
            if (view.ViewType == ViewType.Internal || view.ViewType == ViewType.Undefined) continue;
            if (view.ViewType == ViewType.DrawingSheet) continue;

            if (!viewIdsOnSheets.Contains(view.Id.Value))
            {
                result.UnusedViews.Add(new UnusedViewItem
                {
                    ViewId = checked((int)view.Id.Value),
                    ViewName = view.Name ?? string.Empty,
                    ViewType = view.ViewType.ToString(),
                    Reason = "Not placed on any sheet"
                });
            }

            if (result.UnusedViews.Count >= request.MaxResults) break;
        }
        result.UnusedCount = result.UnusedViews.Count;
        return result;
    }

    // ── Unused Families Audit ──
    internal UnusedFamiliesResponse AuditUnusedFamilies(PlatformServices services, Document doc, UnusedFamiliesRequest request)
    {
        request ??= new UnusedFamiliesRequest();
        var result = new UnusedFamiliesResponse { DocumentKey = services.GetDocumentKey(doc) };

        var families = new FilteredElementCollector(doc).OfClass(typeof(Family)).Cast<Family>().ToList();
        result.TotalFamilies = families.Count;

        // Đếm instance usage cho mỗi family
        var instanceCountByFamilyId = new Dictionary<long, int>();
        foreach (var instance in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
        {
            try
            {
                var sym = instance.Symbol;
                if (sym == null) continue;
                var fam = sym.Family;
                if (fam == null || !fam.IsValidObject) continue;
                var famId = fam.Id.Value;
                if (famId > 0)
                {
                    if (!instanceCountByFamilyId.ContainsKey(famId)) instanceCountByFamilyId[famId] = 0;
                    instanceCountByFamilyId[famId]++;
                }
            }
            catch { /* Skip corrupt/deleted instances */ }
        }

        foreach (var fam in families.OrderBy(f => f.Name ?? string.Empty))
        {
            try
            {
                if (!fam.IsValidObject) continue;
                if (!request.IncludeSystemFamilies && fam.IsInPlace) continue;

                instanceCountByFamilyId.TryGetValue(fam.Id.Value, out int count);
                if (count == 0)
                {
                    var catName = fam.FamilyCategory?.Name ?? string.Empty;
                    if (request.CategoryNames.Count > 0 && !request.CategoryNames.Any(c => string.Equals(c, catName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var typeCount = 0;
                    try { foreach (var symId in fam.GetFamilySymbolIds()) typeCount++; }
                    catch { typeCount = 0; }

                    result.UnusedFamilies.Add(new UnusedFamilyItem
                    {
                        FamilyId = checked((int)fam.Id.Value),
                        FamilyName = fam.Name ?? string.Empty,
                        Category = catName,
                        TypeCount = typeCount,
                        EstimatedSizeKb = typeCount * 50 // rough estimate
                    });

                    if (result.UnusedFamilies.Count >= request.MaxResults) break;
                }
            }
            catch { /* Skip families that throw during property access */ }
        }
        result.UnusedCount = result.UnusedFamilies.Count;
        result.TotalEstimatedSizeKb = result.UnusedFamilies.Sum(f => f.EstimatedSizeKb);
        return result;
    }

    // ── Duplicate Elements Detection ──
    internal DuplicateElementsResponse AuditDuplicates(PlatformServices services, Document doc, DuplicateElementsRequest request)
    {
        request ??= new DuplicateElementsRequest();
        var result = new DuplicateElementsResponse { DocumentKey = services.GetDocumentKey(doc) };
        var toleranceFeet = request.ToleranceMm / 304.8; // mm → feet

        var categories = request.CategoryNames.Count > 0 ? request.CategoryNames : new List<string> { "Walls", "Columns", "Structural Columns" };
        var elements = new List<Element>();

        foreach (var catName in categories)
        {
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            foreach (var elem in collector)
            {
                if (elem.Category != null && string.Equals(elem.Category.Name, catName, StringComparison.OrdinalIgnoreCase))
                    elements.Add(elem);
                if (elements.Count > 10000) break; // safety cap
            }
        }

        result.TotalChecked = elements.Count;

        // Group by location proximity
        var processed = new HashSet<long>();
        foreach (var elem in elements)
        {
            if (processed.Contains(elem.Id.Value)) continue;
            var loc = elem.Location as LocationPoint;
            if (loc == null) continue;

            var group = new List<int>();
            foreach (var other in elements)
            {
                if (other.Id.Value == elem.Id.Value || processed.Contains(other.Id.Value)) continue;
                var otherLoc = other.Location as LocationPoint;
                if (otherLoc == null) continue;

                if (loc.Point.DistanceTo(otherLoc.Point) < toleranceFeet
                    && elem.GetTypeId().Value == other.GetTypeId().Value)
                {
                    group.Add(checked((int)other.Id.Value));
                    processed.Add(other.Id.Value);
                }
            }

            if (group.Count > 0)
            {
                group.Insert(0, checked((int)elem.Id.Value));
                processed.Add(elem.Id.Value);
                result.DuplicateGroups.Add(new DuplicateGroup
                {
                    ElementIds = group,
                    Category = elem.Category?.Name ?? string.Empty,
                    TypeName = doc.GetElement(elem.GetTypeId())?.Name ?? string.Empty,
                    Location = $"({Math.Round(loc.Point.X * 304.8)}, {Math.Round(loc.Point.Y * 304.8)}, {Math.Round(loc.Point.Z * 304.8)}) mm"
                });

                if (result.DuplicateGroups.Count >= request.MaxResults) break;
            }
        }
        result.DuplicateGroupCount = result.DuplicateGroups.Count;
        return result;
    }

    // ── Warnings Cleanup Plan ──
    internal WarningsCleanupResponse AuditWarnings(PlatformServices services, Document doc, WarningsCleanupRequest request)
    {
        request ??= new WarningsCleanupRequest();
        var result = new WarningsCleanupResponse { DocumentKey = services.GetDocumentKey(doc) };
        var warnings = doc.GetWarnings();
        result.TotalWarnings = warnings.Count;

        foreach (var warning in warnings.Take(Math.Max(1, request.MaxResults)))
        {
            var desc = warning.GetDescriptionText() ?? string.Empty;
            var elementIds = warning.GetFailingElements().Select(id => checked((int)id.Value)).ToList();
            var additionalIds = warning.GetAdditionalElements().Select(id => checked((int)id.Value)).ToList();
            elementIds.AddRange(additionalIds);

            var category = CategorizeWarning(desc);
            if (!string.IsNullOrWhiteSpace(request.CategoryFilter) && !string.Equals(category, request.CategoryFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!result.ByCategory.ContainsKey(category)) result.ByCategory[category] = 0;
            result.ByCategory[category]++;

            var item = new WarningCleanupItem
            {
                WarningType = category,
                Description = desc,
                AffectedElementIds = elementIds,
                SuggestedAction = SuggestWarningFix(category),
                AutoFixAvailable = IsAutoFixable(category)
            };
            result.Warnings.Add(item);
        }
        result.AutoFixableCount = result.Warnings.Count(w => w.AutoFixAvailable);
        return result;
    }

    // ── Model Standards Check ──
    internal ModelStandardsResponse AuditModelStandards(PlatformServices services, Document doc, ModelStandardsRequest request)
    {
        request ??= new ModelStandardsRequest();
        var result = new ModelStandardsResponse { DocumentKey = services.GetDocumentKey(doc) };
        var checks = new List<StandardsCheckItem>();

        // Rule 1: Project Information completeness
        checks.Add(CheckProjectInfo(doc));
        // Rule 2: Workset naming
        checks.Add(CheckWorksetNaming(doc));
        // Rule 3: Warning count threshold
        checks.Add(CheckWarningThreshold(doc));
        // Rule 4: Unused view percentage
        checks.Add(CheckUnusedViewPercentage(doc));
        // Rule 5: Grid/Level naming
        checks.Add(CheckGridLevelNaming(doc));

        result.Results = checks;
        result.TotalRules = checks.Count;
        result.PassedCount = checks.Count(c => c.Status == "Pass");
        result.FailedCount = checks.Count(c => c.Status == "Fail");

        var ratio = result.TotalRules > 0 ? (double)result.PassedCount / result.TotalRules : 0;
        result.OverallGrade = ratio >= 0.9 ? "A" : ratio >= 0.7 ? "B" : ratio >= 0.5 ? "C" : "D";
        return result;
    }

    // ── Compliance Report (tổng hợp tất cả audit) ──
    internal ComplianceReportResponse GenerateComplianceReport(PlatformServices services, Document doc, ComplianceReportRequest request)
    {
        request ??= new ComplianceReportRequest();
        var result = new ComplianceReportResponse
        {
            DocumentKey = services.GetDocumentKey(doc),
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
        };

        int totalIssues = 0;

        if (request.IncludeNaming)
        {
            var naming = AuditNaming(services, doc, new NamingAuditRequest { MaxResults = 100 });
            result.Sections.Add(new ComplianceSectionResult
            {
                Section = "Naming Convention", Status = naming.ViolationCount == 0 ? "Pass" : "Fail",
                IssueCount = naming.ViolationCount, Summary = $"{naming.ViolationCount} violations in {naming.TotalChecked} checked"
            });
            totalIssues += naming.ViolationCount;
        }
        if (request.IncludeUnused)
        {
            var unused = AuditUnusedViews(services, doc, new UnusedViewsRequest { MaxResults = 100 });
            result.Sections.Add(new ComplianceSectionResult
            {
                Section = "Unused Views", Status = unused.UnusedCount < 10 ? "Pass" : "Warning",
                IssueCount = unused.UnusedCount, Summary = $"{unused.UnusedCount} unused views out of {unused.TotalViews}"
            });
            totalIssues += unused.UnusedCount;
        }
        if (request.IncludeWarnings)
        {
            var warnings = doc.GetWarnings();
            var status = warnings.Count < 20 ? "Pass" : warnings.Count < 100 ? "Warning" : "Fail";
            result.Sections.Add(new ComplianceSectionResult
            {
                Section = "Model Warnings", Status = status,
                IssueCount = warnings.Count, Summary = $"{warnings.Count} active warnings"
            });
            totalIssues += warnings.Count;
        }
        if (request.IncludeStandards)
        {
            var standards = AuditModelStandards(services, doc, new ModelStandardsRequest());
            result.Sections.Add(new ComplianceSectionResult
            {
                Section = "Model Standards", Status = standards.OverallGrade,
                IssueCount = standards.FailedCount, Summary = $"Grade {standards.OverallGrade}: {standards.PassedCount}/{standards.TotalRules} rules passed"
            });
            totalIssues += standards.FailedCount;
        }
        if (request.IncludeDuplicates)
        {
            var dupes = AuditDuplicates(services, doc, new DuplicateElementsRequest { MaxResults = 50 });
            result.Sections.Add(new ComplianceSectionResult
            {
                Section = "Duplicate Elements", Status = dupes.DuplicateGroupCount == 0 ? "Pass" : "Warning",
                IssueCount = dupes.DuplicateGroupCount, Summary = $"{dupes.DuplicateGroupCount} duplicate groups found"
            });
            totalIssues += dupes.DuplicateGroupCount;
        }

        result.TotalIssues = totalIssues;
        result.OverallScore = totalIssues == 0 ? "Excellent" : totalIssues < 10 ? "Good" : totalIssues < 50 ? "Fair" : "Needs Attention";
        return result;
    }

    // ── MUTATION: Purge unused (high-risk) ──
    internal ExecutionResult PreviewPurgeUnused(UIApplication uiapp, PlatformServices services, Document doc, PurgeUnusedRequest payload, ToolRequestEnvelope request)
    {
        var diags = new List<DiagnosticRecord>();
        var idsToDelete = new List<int>();

        if (payload.PurgeViews)
        {
            var unused = AuditUnusedViews(services, doc, new UnusedViewsRequest { MaxResults = 1000 });
            idsToDelete.AddRange(unused.UnusedViews.Select(v => v.ViewId));
            diags.Add(DiagnosticRecord.Create("PURGE_VIEWS", DiagnosticSeverity.Info, $"Will purge {unused.UnusedCount} unused views."));
        }
        if (payload.PurgeFamilies)
        {
            var unused = AuditUnusedFamilies(services, doc, new UnusedFamiliesRequest { MaxResults = 1000 });
            idsToDelete.AddRange(unused.UnusedFamilies.Select(f => f.FamilyId));
            diags.Add(DiagnosticRecord.Create("PURGE_FAMILIES", DiagnosticSeverity.Info, $"Will purge {unused.UnusedCount} unused families (~{unused.TotalEstimatedSizeKb}KB)."));
        }

        // Filter excludes
        if (payload.ExcludeNames.Count > 0)
        {
            // We can't resolve names here efficiently, but log a warning
            diags.Add(DiagnosticRecord.Create("EXCLUDE_FILTER", DiagnosticSeverity.Info, $"Excluding {payload.ExcludeNames.Count} name patterns."));
        }

        var token = services.Approval.IssueToken(request.ToolName, services.Approval.BuildFingerprint(request), services.GetDocumentKey(doc), request.Caller, request.SessionId);
        return new ExecutionResult
        {
            OperationName = request.ToolName, DryRun = true, ConfirmationRequired = true, ApprovalToken = token,
            Diagnostics = diags, ChangedIds = idsToDelete,
            Artifacts = new List<string> { $"TotalToPurge={idsToDelete.Count}" }
        };
    }

    internal ExecutionResult ExecutePurgeUnused(UIApplication uiapp, PlatformServices services, Document doc, PurgeUnusedRequest payload)
    {
        var diags = new List<DiagnosticRecord>();
        var deletedIds = new List<int>();

        using var group = new TransactionGroup(doc, "765TAgent::audit.purge_unused_safe");
        group.Start();
        using var tx = new Transaction(doc, "Purge unused elements");
        tx.Start();
        AgentFailureHandling.Configure(tx, diags);

        if (payload.PurgeViews)
        {
            var unused = AuditUnusedViews(services, doc, new UnusedViewsRequest { MaxResults = 1000 });
            foreach (var view in unused.UnusedViews)
            {
                if (payload.ExcludeNames.Any(n => view.ViewName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                try
                {
                    doc.Delete(new ElementId((long)view.ViewId));
                    deletedIds.Add(view.ViewId);
                }
                catch (Exception ex)
                {
                    diags.Add(DiagnosticRecord.Create("DELETE_FAILED", DiagnosticSeverity.Warning, $"Cannot delete view '{view.ViewName}': {ex.Message}"));
                }
            }
        }
        if (payload.PurgeFamilies)
        {
            var unused = AuditUnusedFamilies(services, doc, new UnusedFamiliesRequest { MaxResults = 1000 });
            foreach (var fam in unused.UnusedFamilies)
            {
                if (payload.ExcludeNames.Any(n => fam.FamilyName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                try
                {
                    doc.Delete(new ElementId((long)fam.FamilyId));
                    deletedIds.Add(fam.FamilyId);
                }
                catch (Exception ex)
                {
                    diags.Add(DiagnosticRecord.Create("DELETE_FAILED", DiagnosticSeverity.Warning, $"Cannot delete family '{fam.FamilyName}': {ex.Message}"));
                }
            }
        }
        tx.Commit();

        var diff = new DiffSummary { DeletedIds = deletedIds };
        if (diags.Any(d => d.Severity == DiagnosticSeverity.Error)) { group.RollBack(); diff = new DiffSummary(); }
        else group.Assimilate();

        return new ExecutionResult
        {
            OperationName = "audit.purge_unused_safe", DryRun = false, ChangedIds = deletedIds, DiffSummary = diff, Diagnostics = diags,
            Artifacts = new List<string> { $"DeletedCount={deletedIds.Count}" }
        };
    }

    // ── Private helpers ──
    private static string SuggestCleanName(string name)
    {
        var cleaned = Regex.Replace(name, @"\s*Copy\s*\d*$", string.Empty, RegexOptions.IgnoreCase).Trim();
        return cleaned.Length < 3 ? name + "_renamed" : cleaned;
    }

    private static string CategorizeWarning(string description)
    {
        if (description.IndexOf("overlap", StringComparison.OrdinalIgnoreCase) >= 0) return "Overlap";
        if (description.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0) return "Duplicate";
        if (description.IndexOf("join", StringComparison.OrdinalIgnoreCase) >= 0) return "Join";
        if (description.IndexOf("room", StringComparison.OrdinalIgnoreCase) >= 0) return "Room";
        if (description.IndexOf("level", StringComparison.OrdinalIgnoreCase) >= 0) return "Level";
        if (description.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0) return "Stair";
        if (description.IndexOf("analytical", StringComparison.OrdinalIgnoreCase) >= 0) return "Analytical";
        return "Other";
    }

    private static string SuggestWarningFix(string category)
    {
        return category switch
        {
            "Overlap" => "Review overlapping elements and delete/adjust duplicates",
            "Duplicate" => "Remove duplicate elements keeping the primary instance",
            "Join" => "Unjoin and rejoin elements with correct order",
            "Room" => "Check room boundaries and placement",
            "Level" => "Verify level elevations and element level assignments",
            _ => "Manual review recommended"
        };
    }

    private static bool IsAutoFixable(string category)
    {
        return category is "Duplicate" or "Overlap";
    }

    private static StandardsCheckItem CheckProjectInfo(Document doc)
    {
        var pi = doc.ProjectInformation;
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(pi.Name)) missing.Add("Project Name");
        if (string.IsNullOrWhiteSpace(pi.Number)) missing.Add("Project Number");
        if (string.IsNullOrWhiteSpace(pi.Address)) missing.Add("Address");
        return new StandardsCheckItem
        {
            RuleName = "Project Information Completeness",
            Status = missing.Count == 0 ? "Pass" : "Fail",
            Description = missing.Count == 0 ? "All project info fields populated" : $"Missing: {string.Join(", ", missing)}",
            AffectedCount = missing.Count
        };
    }

    private static StandardsCheckItem CheckWorksetNaming(Document doc)
    {
        if (!doc.IsWorkshared) return new StandardsCheckItem { RuleName = "Workset Naming", Status = "Pass", Description = "Non-workshared document — skipped" };
        var worksets = new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset).ToWorksets();
        var bad = worksets.Where(w => w.Name.StartsWith("Workset", StringComparison.OrdinalIgnoreCase)).ToList();
        return new StandardsCheckItem
        {
            RuleName = "Workset Naming",
            Status = bad.Count == 0 ? "Pass" : "Fail",
            Description = bad.Count == 0 ? "All worksets have meaningful names" : $"{bad.Count} worksets with default names",
            AffectedCount = bad.Count
        };
    }

    private static StandardsCheckItem CheckWarningThreshold(Document doc)
    {
        var count = doc.GetWarnings().Count;
        return new StandardsCheckItem
        {
            RuleName = "Warning Count",
            Status = count < 50 ? "Pass" : count < 200 ? "Warning" : "Fail",
            Description = $"{count} active warnings (threshold: <50=pass, <200=warning)",
            AffectedCount = count
        };
    }

    private static StandardsCheckItem CheckUnusedViewPercentage(Document doc)
    {
        var allViews = new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Where(v => !v.IsTemplate && !(v is ViewSheet)).ToList();
        var viewIdsOnSheets = new HashSet<long>();
        foreach (var sheet in new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>())
            foreach (var vpId in sheet.GetAllViewports())
                if (doc.GetElement(vpId) is Viewport vp) viewIdsOnSheets.Add(vp.ViewId.Value);

        var unusedCount = allViews.Count(v => !viewIdsOnSheets.Contains(v.Id.Value));
        var percentage = allViews.Count > 0 ? (double)unusedCount / allViews.Count * 100 : 0;
        return new StandardsCheckItem
        {
            RuleName = "Unused View Percentage",
            Status = percentage < 20 ? "Pass" : percentage < 50 ? "Warning" : "Fail",
            Description = $"{unusedCount}/{allViews.Count} views unused ({percentage:F0}%)",
            AffectedCount = unusedCount
        };
    }

    private static StandardsCheckItem CheckGridLevelNaming(Document doc)
    {
        var grids = new FilteredElementCollector(doc).OfClass(typeof(Grid)).Cast<Grid>().ToList();
        var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
        var badNames = new List<string>();

        foreach (var grid in grids)
            if (string.IsNullOrWhiteSpace(grid.Name) || grid.Name.Length < 1) badNames.Add($"Grid: '{grid.Name}'");
        foreach (var level in levels)
            if (level.Name.StartsWith("Level", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(level.Name, @"^Level\s+\d+$"))
                badNames.Add($"Level: '{level.Name}'");

        return new StandardsCheckItem
        {
            RuleName = "Grid/Level Naming",
            Status = badNames.Count == 0 ? "Pass" : "Warning",
            Description = badNames.Count == 0 ? "All grids/levels have proper names" : $"{badNames.Count} items with generic names",
            AffectedCount = badNames.Count
        };
    }
}
