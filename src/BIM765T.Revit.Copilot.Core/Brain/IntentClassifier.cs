using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BIM765T.Revit.Copilot.Core.Brain;

public sealed class IntentClassifier
{
    public WorkerIntentClassification Classify(string message, bool hasPendingApproval)
    {
        var normalized = Normalize(message);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Build("help", normalized);
        }

        if (MatchesExactOrPrefix(normalized, "chao", "xin chao", "hello", "hey", "hi", "alo"))
        {
            return Build("greeting", normalized);
        }

        if (MatchesAny(normalized, "em la ai", "em la gi", "ban la ai", "ban la gi", "who are you"))
        {
            return Build("identity_query", normalized);
        }

        if (hasPendingApproval && MatchesAny(normalized, "dong y", "approve", "chap nhan", "ok chay", "tien hanh", "xac nhan"))
        {
            return Build("approval", normalized);
        }

        if (hasPendingApproval && MatchesAny(normalized, "tu choi", "reject", "khong dong y", "huy approval", "khong chay"))
        {
            return Build("reject", normalized);
        }

        if (MatchesAny(normalized, "cancel", "huy task", "dung lai", "stop task", "bo mission"))
        {
            return Build("cancel", normalized);
        }

        if (MatchesAny(normalized, "resume", "tiep tuc", "lam tiep", "restore mission"))
        {
            return Build("resume", normalized);
        }

        if ((MatchesAny(normalized, "tong quan", "overview", "tom tat", "summarize", "summary", "giai thich", "explain", "tai sao", "why", "so sanh", "compare", "review")
                && MatchesAny(normalized, "project", "du an", "workspace", "context", "manifest", "project brain", "deep scan", "project context", "context bundle"))
            || MatchesAny(normalized, "project brain", "project context", "workspace context", "context bundle", "deep scan report", "manifest project"))
        {
            return Build("project_research_request", normalized);
        }

        if (MatchesAny(normalized, "context", "delta", "selection", "active view", "active document", "trang thai", "dang mo", "dang chon", "queue", "progress"))
        {
            return Build("context_query", normalized);
        }

        if (MatchesAny(normalized, "family", "nested", "connector", "formula", "reference plane", "family manager"))
        {
            return Build("family_analysis_request", normalized, ExtractFamilyHint(message));
        }

        if ((MatchesAny(normalized, "sheet", "ban ve", "to ve") && MatchesAny(normalized, "tao", "create", "lap", "bo tri", "generate"))
            || MatchesAny(normalized, "tao sheet", "create sheet", "tao sheet a", "create sheet a", "tao to ve"))
        {
            return Build("sheet_authoring_request", normalized, ExtractSheetHint(message));
        }

        if ((MatchesAny(normalized, "view", "3d", "mat bang", "section", "elevation", "template", "duplicate")
             && MatchesAny(normalized, "tao", "create", "duplicate", "apply", "set"))
            || MatchesAny(normalized, "create 3d view", "duplicate active view", "apply view template"))
        {
            return Build("view_authoring_request", normalized);
        }

        if (MatchesAny(normalized, "/sheet", "/view", "/purge", "/command", "command palette", "slash command", "quick command"))
        {
            return Build("command_palette_request", normalized);
        }

        if (MatchesAny(normalized, "place view", "viewport", "renumber sheet", "align viewport", "documentation", "ho so", "package"))
        {
            return Build("documentation_request", normalized, ExtractSheetHint(message));
        }

        if (MatchesAny(normalized, "sheet", "ban ve", "viewport", "sheet number")
            || MatchesAll(normalized, "to", "ve"))
        {
            return Build("sheet_analysis_request", normalized, ExtractSheetHint(message));
        }

        // QC/health: precise match only — 'qc', 'health', 'warning', 'audit' are strong signals.
        // 'kiem tra' and 'review' are too broad alone (match systems/coordination), so require pairing.
        if (MatchesAny(normalized, "qc", "health", "warning", "audit")
            || (MatchesAny(normalized, "kiem tra", "review") && MatchesAny(normalized, "model", "health", "qc", "warning", "audit")))
        {
            return Build("qc_request", normalized);
        }

        if (MatchesAny(normalized, "naming", "iso 19650", "lod", "lodloi", "workset", "parameter", "excel", "spec", "bep"))
        {
            return Build("governance_request", normalized);
        }

        if (MatchesAny(normalized, "tag", "dimension", "dim ", "room", "ceiling", "floor finish", "tran", "san theo room"))
        {
            return Build("annotation_request", normalized);
        }

        if (MatchesAny(normalized, "clash", "clearance", "penetration", "opening", "xung dot", "va cham"))
        {
            return Build("coordination_request", normalized);
        }

        if (MatchesAny(normalized, "route", "routing", "slope", "duct", "pipe", "fixture", "open end", "disconnected", "mep system")
            || (MatchesAny(normalized, "system") && MatchesAny(normalized, "duct", "pipe", "fixture", "route", "slope", "mep")))
        {
            return Build("systems_request", normalized);
        }

        if (MatchesAny(normalized, "cad", "point cloud", "scan to bim", "boq", "4d", "5d", "cost", "schedule sync", "split model", "pdf"))
        {
            return Build("integration_request", normalized);
        }

        if (MatchesAny(normalized, "highlight", "filter", "script", "natural language", "ngon ngu tu nhien"))
        {
            return Build("intent_compile_request", normalized);
        }

        if (MatchesAny(normalized, "purge", "cleanup", "clean up", "manage", "rename", "delete unused", "unused views", "unused families"))
        {
            return Build("model_manage_request", normalized);
        }

        if ((MatchesAny(normalized, "wall", "door", "window", "column", "beam", "level", "grid", "family instance", "device")
            && MatchesAny(normalized, "tao", "create", "place", "insert")))
        {
            return Build("element_authoring_request", normalized);
        }

        if (MatchesAny(normalized, "xoa", "delete", "purge", "apply", "fix", "doi", "set ", "fill ", "batch fill", "cleanup", "create", "sync", "rename"))
        {
            return Build("mutation_request", normalized);
        }

        if (MatchesAny(normalized, "model", "review"))
        {
            return Build("qc_request", normalized);
        }

        if (MatchesAny(normalized, "giup", "help", "lam duoc gi", "huong dan"))
        {
            return Build("help", normalized);
        }

        return Build("help", normalized);
    }

    private static WorkerIntentClassification Build(string intent, string normalizedMessage, string targetHint = "")
    {
        return new WorkerIntentClassification
        {
            Intent = intent,
            NormalizedMessage = normalizedMessage,
            TargetHint = targetHint ?? string.Empty
        };
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = (value ?? string.Empty).Trim().Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => c == 'đ' || c == 'Đ' ? 'd' : char.ToLowerInvariant(c))
            .ToArray();

        return Regex.Replace(new string(chars), "\\s+", " ");
    }

    private static bool MatchesAny(string normalized, params string[] patterns)
    {
        return patterns.Any(pattern => normalized.IndexOf(pattern, StringComparison.Ordinal) >= 0);
    }

    private static bool MatchesExactOrPrefix(string normalized, params string[] patterns)
    {
        return patterns.Any(pattern =>
            string.Equals(normalized, pattern, StringComparison.Ordinal)
            || normalized.StartsWith(pattern + " ", StringComparison.Ordinal));
    }

    private static bool MatchesAll(string normalized, params string[] patterns)
    {
        return patterns.All(pattern => normalized.IndexOf(pattern, StringComparison.Ordinal) >= 0);
    }

    private static string ExtractSheetHint(string originalMessage)
    {
        var match = Regex.Match(originalMessage ?? string.Empty, "\\b([A-Za-z]{0,2}\\d{2,4})\\b");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private static string ExtractFamilyHint(string originalMessage)
    {
        var match = Regex.Match(originalMessage ?? string.Empty, "\"(?<name>[^\"]+)\"");
        return match.Success ? match.Groups["name"].Value : string.Empty;
    }
}
