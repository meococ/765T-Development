using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core;

public sealed class ToolGuidanceService
{
    private readonly ToolCapabilitySearchService _search;
    private readonly ToolGraphOverlayService _overlay;
    private readonly PlaybookLoaderService _playbooks;
    private readonly PolicyResolutionService _policies;
    private readonly SpecialistRegistryService _specialists;
    private readonly CapabilityTaskCompilerService _compiler;

    public ToolGuidanceService()
        : this(
            new ToolCapabilitySearchService(),
            ToolGraphOverlayService.LoadDefault(),
            new PlaybookLoaderService(),
            new PolicyResolutionService(new PackCatalogService(), new WorkspaceCatalogService()),
            new SpecialistRegistryService(new PackCatalogService(), new WorkspaceCatalogService()),
            null)
    {
    }

    public ToolGuidanceService(ToolCapabilitySearchService search, ToolGraphOverlayService overlay)
        : this(
            search,
            overlay,
            new PlaybookLoaderService(),
            new PolicyResolutionService(new PackCatalogService(), new WorkspaceCatalogService()),
            new SpecialistRegistryService(new PackCatalogService(), new WorkspaceCatalogService()),
            null)
    {
    }

    public ToolGuidanceService(ToolCapabilitySearchService search, ToolGraphOverlayService overlay, PlaybookLoaderService playbooks)
        : this(
            search,
            overlay,
            playbooks,
            new PolicyResolutionService(new PackCatalogService(), new WorkspaceCatalogService()),
            new SpecialistRegistryService(new PackCatalogService(), new WorkspaceCatalogService()),
            null)
    {
    }

    public ToolGuidanceService(
        ToolCapabilitySearchService search,
        ToolGraphOverlayService overlay,
        PlaybookLoaderService playbooks,
        PolicyResolutionService policies,
        SpecialistRegistryService specialists,
        CapabilityTaskCompilerService? compiler)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _playbooks = playbooks ?? throw new ArgumentNullException(nameof(playbooks));
        var packs = new PackCatalogService();
        var workspaces = new WorkspaceCatalogService();
        _policies = policies ?? new PolicyResolutionService(packs, workspaces);
        _specialists = specialists ?? new SpecialistRegistryService(packs, workspaces);
        _compiler = compiler ?? new CapabilityTaskCompilerService(
            _search,
            new PlaybookOrchestrationService(_playbooks, packs, workspaces, new StandardsCatalogService(packs, workspaces)),
            _policies,
            _specialists);
    }

    public ToolGuidanceResponse Build(IEnumerable<ToolManifest> manifests, ToolGuidanceRequest request)
    {
        request ??= new ToolGuidanceRequest();
        var catalog = (manifests ?? Array.Empty<ToolManifest>()).ToList();
        var selected = SelectManifests(catalog, request).Take(Math.Max(1, request.MaxResults)).ToList();

        var response = new ToolGuidanceResponse
        {
            Query = request.Query,
            Guidance = selected.Select(x => BuildRecord(catalog, x)).ToList()
        };

        CompiledTaskPlan? compiledPlan = null;
        if (!string.IsNullOrWhiteSpace(request.Query) && request.Query.Split(' ').Length >= 3)
        {
            compiledPlan = _compiler.Compile(catalog, new IntentCompileRequest
            {
                PreferredCapabilityDomain = request.PreferredCapabilityDomain,
                Discipline = request.Discipline,
                Task = new IntentTask
                {
                    Query = request.Query,
                    WorkspaceId = request.WorkspaceId,
                    DocumentContext = request.DocumentContext,
                    CapabilityDomain = request.PreferredCapabilityDomain,
                    Discipline = request.Discipline,
                    IssueKinds = request.IssueKinds?.ToList() ?? new List<string>()
                }
            });

            response.CompiledPlan = compiledPlan;
            response.ResolvedCapabilityDomain = string.IsNullOrWhiteSpace(compiledPlan.CapabilityDomain)
                ? CapabilityDomains.General
                : compiledPlan.CapabilityDomain;
            response.PolicyResolution = compiledPlan.PolicyResolution ?? new PolicyResolution();
            response.RecommendedSpecialists = compiledPlan.RecommendedSpecialists?.ToList() ?? new List<CapabilitySpecialistDescriptor>();
            if (!string.IsNullOrWhiteSpace(compiledPlan.RecommendedPlaybook?.PlaybookId))
            {
                response.RecommendedPlaybook = compiledPlan.RecommendedPlaybook!;
            }
        }
        else
        {
            response.ResolvedCapabilityDomain = string.IsNullOrWhiteSpace(request.PreferredCapabilityDomain)
                ? CapabilityDomains.General
                : request.PreferredCapabilityDomain;
            response.PolicyResolution = _policies.Resolve(new PolicyResolutionRequest
            {
                WorkspaceId = request.WorkspaceId,
                CapabilityDomain = response.ResolvedCapabilityDomain,
                Discipline = request.Discipline,
                IssueKinds = request.IssueKinds?.ToList() ?? new List<string>()
            });
            response.RecommendedSpecialists = _specialists.Resolve(new CapabilitySpecialistRequest
            {
                WorkspaceId = request.WorkspaceId,
                CapabilityDomain = response.ResolvedCapabilityDomain,
                Discipline = request.Discipline,
                IssueKinds = request.IssueKinds?.ToList() ?? new List<string>()
            }).Specialists;
        }

        if (response.RecommendedPlaybook == null || string.IsNullOrWhiteSpace(response.RecommendedPlaybook.PlaybookId))
        {
            var toolNames = catalog.Select(t => t.ToolName).ToList();
            var recommendation = _playbooks.Recommend(
                request.Query,
                request.DocumentContext,
                toolNames);

            if (!string.IsNullOrWhiteSpace(recommendation.PlaybookId))
            {
                response.RecommendedPlaybook = recommendation;
            }
        }

        foreach (var packId in selected
                     .Select(x => x.PackId)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            response.RecommendedPackIds.Add(packId);
        }

        if (!string.IsNullOrWhiteSpace(response.RecommendedPlaybook?.PackId))
        {
            response.RecommendedPackIds.Add(response.RecommendedPlaybook!.PackId);
        }

        foreach (var packId in response.PolicyResolution?.ResolvedPackIds ?? new List<string>())
        {
            response.RecommendedPackIds.Add(packId);
        }

        foreach (var packId in (response.RecommendedSpecialists ?? new List<CapabilitySpecialistDescriptor>())
                     .Select(x => x.PackId)
                     .Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            response.RecommendedPackIds.Add(packId);
        }

        response.RecommendedPackIds = response.RecommendedPackIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return response;
    }

    /// <summary>
    /// Recommend a playbook for the given task description.
    /// Standalone method — useful when caller needs playbook without full guidance.
    /// </summary>
    public PlaybookRecommendation RecommendPlaybook(
        string taskDescription,
        string documentContext,
        IEnumerable<ToolManifest> manifests)
    {
        var catalog = (manifests ?? Array.Empty<ToolManifest>()).ToList();
        var toolNames = catalog.Select(t => t.ToolName).ToList();
        return _playbooks.Recommend(taskDescription, documentContext, toolNames);
    }

    public IReadOnlyList<PlaybookDefinition> ListPlaybooks()
    {
        return _playbooks.GetAll();
    }

    private IEnumerable<ToolManifest> SelectManifests(IReadOnlyList<ToolManifest> manifests, ToolGuidanceRequest request)
    {
        if (request.ToolNames != null && request.ToolNames.Count > 0)
        {
            var requested = new HashSet<string>(request.ToolNames.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
            return manifests.Where(x => requested.Contains(x.ToolName)).OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            return _search.Search(manifests, new ToolCapabilityLookupRequest
            {
                Query = request.Query,
                MaxResults = Math.Max(1, request.MaxResults),
                CapabilityDomain = request.PreferredCapabilityDomain,
                Discipline = request.Discipline,
                IssueKinds = request.IssueKinds?.ToList() ?? new List<string>()
            }).Matches.Select(x => x.Manifest);
        }

        return manifests
            .OrderByDescending(ComputeRiskScore)
            .ThenBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase);
    }

    private ToolGuidanceRecord BuildRecord(IReadOnlyCollection<ToolManifest> catalog, ToolManifest manifest)
    {
        _overlay.TryGet(manifest.ToolName, out var overlay);

        var prerequisites = BuildPrerequisites(manifest);
        MergeDistinct(prerequisites, overlay?.Prerequisites);

        var followUps = BuildFollowUps(catalog, manifest);
        MergeDistinct(followUps, overlay?.FollowUps);

        var failureCodes = BuildCommonFailureCodes(manifest);
        var recoveryTools = BuildRecoveryTools(catalog, failureCodes);
        var recoveryHints = BuildRecoveryHints(failureCodes, overlay);
        var riskScore = ComputeRiskScore(manifest);
        var costScore = ComputeCostScore(manifest);

        return new ToolGuidanceRecord
        {
            ToolName = manifest.ToolName,
            GuidanceSummary = BuildSummary(manifest, riskScore, costScore, prerequisites, followUps, overlay),
            RiskScore = riskScore,
            CostScore = costScore,
            Prerequisites = prerequisites,
            FollowUps = followUps,
            CommonFailureCodes = failureCodes,
            RecommendedRecoveryTools = recoveryTools,
            AntiPatterns = overlay?.AntiPatterns ?? new List<string>(),
            TypicalChains = overlay?.TypicalChains ?? new List<string>(),
            RecoveryHints = recoveryHints,
            RecommendedTemplates = overlay?.RecommendedTemplates ?? new List<string>(),
            CapabilityDomain = string.IsNullOrWhiteSpace(manifest.CapabilityDomain) ? CapabilityDomains.General : manifest.CapabilityDomain,
            DeterminismLevel = string.IsNullOrWhiteSpace(manifest.DeterminismLevel) ? ToolDeterminismLevels.Deterministic : manifest.DeterminismLevel,
            RequiresPolicyPack = manifest.RequiresPolicyPack,
            VerificationMode = string.IsNullOrWhiteSpace(manifest.VerificationMode) ? ToolVerificationModes.ReportOnly : manifest.VerificationMode,
            SupportedDisciplines = manifest.SupportedDisciplines?.ToList() ?? new List<string>(),
            IssueKinds = manifest.IssueKinds?.ToList() ?? new List<string>()
        };
    }

    private static int ComputeRiskScore(ToolManifest manifest)
    {
        var score = manifest.PermissionLevel switch
        {
            PermissionLevel.Read => 1,
            PermissionLevel.Review => 3,
            PermissionLevel.Mutate => 7,
            PermissionLevel.FileLifecycle => 8,
            PermissionLevel.Admin => 9,
            _ => 5
        };

        if (manifest.MutatesModel)
        {
            score += 1;
        }

        if (manifest.ApprovalRequirement == ApprovalRequirement.HighRiskToken)
        {
            score += 2;
        }
        else if (manifest.ApprovalRequirement == ApprovalRequirement.ConfirmToken)
        {
            score += 1;
        }

        if (manifest.RiskTags.Any(x => string.Equals(x, "high_risk", StringComparison.OrdinalIgnoreCase)))
        {
            score += 2;
        }

        if (manifest.RiskTags.Any(x => string.Equals(x, "mutation", StringComparison.OrdinalIgnoreCase)))
        {
            score += 1;
        }

        if (manifest.RiskTags.Any(x => string.Equals(x, "destructive", StringComparison.OrdinalIgnoreCase)))
        {
            score += 2;
        }

        return Math.Min(10, Math.Max(0, score));
    }

    private static int ComputeCostScore(ToolManifest manifest)
    {
        var score = manifest.BatchMode switch
        {
            "none" => 1,
            "single" => 2,
            "chunked" => 6,
            "checkpointed" => 7,
            _ => 4
        };

        score += Math.Min(2, manifest.RequiredContext.Count);

        if (manifest.TouchesActiveView)
        {
            score += 1;
        }

        if (manifest.ExecutionTimeoutMs >= 30000)
        {
            score += 2;
        }
        else if (manifest.ExecutionTimeoutMs >= 10000)
        {
            score += 1;
        }

        if (manifest.PermissionLevel == PermissionLevel.FileLifecycle)
        {
            score += 1;
        }

        return Math.Min(10, Math.Max(0, score));
    }

    private static List<string> BuildPrerequisites(ToolManifest manifest)
    {
        var results = new List<string>();
        foreach (var item in manifest.RequiredContext ?? new List<string>())
        {
            AddDistinct(results, item);
        }

        if (manifest.TouchesActiveView)
        {
            AddDistinct(results, "active view stable");
        }

        if (manifest.SupportsDryRun || manifest.RequiresExpectedContext || manifest.ApprovalRequirement != ApprovalRequirement.None)
        {
            AddDistinct(results, "dry_run preview");
        }

        if (manifest.RequiresExpectedContext || manifest.ApprovalRequirement == ApprovalRequirement.HighRiskToken)
        {
            AddDistinct(results, "expected_context");
        }

        if (manifest.ApprovalRequirement != ApprovalRequirement.None)
        {
            AddDistinct(results, "approval_token");
        }

        if (manifest.ApprovalRequirement == ApprovalRequirement.HighRiskToken)
        {
            AddDistinct(results, "preview_run_id");
        }

        return results;
    }

    private static List<string> BuildFollowUps(IReadOnlyCollection<ToolManifest> catalog, ToolManifest manifest)
    {
        var results = new List<string>();

        if (manifest.MutatesModel || manifest.PermissionLevel == PermissionLevel.FileLifecycle)
        {
            AddIfExists(catalog, results, ToolNames.ContextGetHotState);
            AddIfExists(catalog, results, ToolNames.SessionGetRecentOperations);
        }

        if (manifest.PermissionLevel == PermissionLevel.FileLifecycle
            || manifest.ToolName.StartsWith("export.", StringComparison.OrdinalIgnoreCase)
            || manifest.ToolName.StartsWith("file.", StringComparison.OrdinalIgnoreCase))
        {
            AddIfExists(catalog, results, ToolNames.ArtifactSummarize);
        }

        if (!string.Equals(manifest.BatchMode, "none", StringComparison.OrdinalIgnoreCase))
        {
            AddIfExists(catalog, results, ToolNames.TaskGetResiduals);
            AddIfExists(catalog, results, ToolNames.TaskResume);
        }

        if (manifest.TouchesActiveView)
        {
            AddIfExists(catalog, results, ToolNames.ReviewCaptureSnapshot);
        }

        return results;
    }

    private static List<string> BuildCommonFailureCodes(ToolManifest manifest)
    {
        var results = new List<string> { StatusCodes.InvalidRequest };

        if (!manifest.Enabled)
        {
            AddDistinct(results, StatusCodes.PolicyBlocked);
        }

        if (manifest.RequiresExpectedContext || manifest.ApprovalRequirement == ApprovalRequirement.HighRiskToken)
        {
            AddDistinct(results, StatusCodes.ContextMismatch);
        }

        if (manifest.ApprovalRequirement != ApprovalRequirement.None)
        {
            AddDistinct(results, StatusCodes.ApprovalInvalid);
        }

        if (manifest.ApprovalRequirement == ApprovalRequirement.HighRiskToken)
        {
            AddDistinct(results, StatusCodes.ApprovalExpired);
            AddDistinct(results, StatusCodes.ApprovalMismatch);
            AddDistinct(results, StatusCodes.PreviewRunRequired);
        }

        if (manifest.PermissionLevel == PermissionLevel.Mutate || manifest.PermissionLevel == PermissionLevel.FileLifecycle)
        {
            AddDistinct(results, StatusCodes.WriteDisabled);
        }

        if (manifest.PermissionLevel == PermissionLevel.FileLifecycle)
        {
            AddDistinct(results, StatusCodes.SaveDisabled);
        }

        return results;
    }

    private static List<string> BuildRecoveryTools(IReadOnlyCollection<ToolManifest> catalog, IEnumerable<string> failureCodes)
    {
        var results = new List<string>();
        foreach (var code in failureCodes)
        {
            switch (code)
            {
                case StatusCodes.ContextMismatch:
                    AddIfExists(catalog, results, ToolNames.DocumentGetContextFingerprint);
                    AddIfExists(catalog, results, ToolNames.ContextGetHotState);
                    break;

                case StatusCodes.ApprovalInvalid:
                case StatusCodes.ApprovalExpired:
                case StatusCodes.ApprovalMismatch:
                case StatusCodes.PreviewRunRequired:
                    AddIfExists(catalog, results, ToolNames.SessionGetTaskContext);
                    AddIfExists(catalog, results, ToolNames.ToolGetGuidance);
                    break;

                case StatusCodes.WriteDisabled:
                case StatusCodes.SaveDisabled:
                    AddIfExists(catalog, results, ToolNames.SessionGetCapabilities);
                    break;
            }
        }

        return results;
    }

    private static List<string> BuildRecoveryHints(IEnumerable<string> failureCodes, ToolGraphOverlayEntry? overlay)
    {
        var results = new List<string>();
        foreach (var code in failureCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            switch (code)
            {
                case StatusCodes.ContextMismatch:
                    AddDistinct(results, "Làm mới fingerprint/context rồi chạy lại dry-run trước khi execute.");
                    break;
                case StatusCodes.ApprovalInvalid:
                case StatusCodes.ApprovalExpired:
                case StatusCodes.ApprovalMismatch:
                case StatusCodes.PreviewRunRequired:
                    AddDistinct(results, "Tạo lại preview hoặc task.preview để lấy approval token mới và preview_run_id hợp lệ.");
                    break;
                case StatusCodes.WriteDisabled:
                case StatusCodes.SaveDisabled:
                    AddDistinct(results, "Kiểm tra session.get_capabilities và policy hiện hành trước khi thử lại.");
                    break;
            }
        }

        MergeDistinct(results, overlay?.RecoveryHints);
        return results;
    }

    private static string BuildSummary(ToolManifest manifest, int riskScore, int costScore, IReadOnlyList<string> prerequisites, IReadOnlyList<string> followUps, ToolGraphOverlayEntry? overlay)
    {
        var prereqSummary = prerequisites.Count == 0 ? "không cần bước chuẩn bị đặc biệt" : "cần " + string.Join(", ", prerequisites.Take(3));
        var followUpSummary = followUps.Count == 0 ? "không có follow-up bắt buộc" : "nên theo sau bằng " + string.Join(", ", followUps.Take(2));
        var antiPatternSummary = overlay?.AntiPatterns?.Count > 0 ? " Tránh: " + overlay.AntiPatterns[0] + "." : string.Empty;
        var surfaceSummary = string.IsNullOrWhiteSpace(manifest.CapabilityPack)
            ? string.Empty
            : $" [{manifest.CapabilityPack}/{manifest.SkillGroup}]";
        return manifest.ToolName + surfaceSummary + ": risk " + riskScore + "/10, cost " + costScore + "/10, " + prereqSummary + "; " + followUpSummary + "." + antiPatternSummary;
    }

    private static void MergeDistinct(ICollection<string> target, IEnumerable<string>? values)
    {
        foreach (var value in values ?? Array.Empty<string>())
        {
            AddDistinct(target, value);
        }
    }

    private static void AddIfExists(IReadOnlyCollection<ToolManifest> catalog, ICollection<string> values, string toolName)
    {
        if (catalog.Any(x => string.Equals(x.ToolName, toolName, StringComparison.OrdinalIgnoreCase)))
        {
            AddDistinct(values, toolName);
        }
    }

    private static void AddDistinct(ICollection<string> values, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }
}
