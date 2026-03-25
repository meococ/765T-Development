using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core;

public sealed class PolicyResolutionService
{
    private readonly PackCatalogService _packs;
    private readonly WorkspaceCatalogService _workspaces;

    public PolicyResolutionService(PackCatalogService packs, WorkspaceCatalogService workspaces)
    {
        _packs = packs ?? throw new ArgumentNullException(nameof(packs));
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
    }

    public PolicyResolution Resolve(PolicyResolutionRequest request)
    {
        request ??= new PolicyResolutionRequest();
        var workspace = _workspaces.GetManifest(request.WorkspaceId).Workspace;
        var candidates = ResolveCandidatePacks(workspace, request).ToList();
        var resolved = candidates
            .Where(x => MatchesPolicyPack(x.Manifest, request))
            .ToList();
        if (resolved.Count == 0)
        {
            resolved = candidates;
        }

        var files = new List<StandardsResolvedFile>();
        foreach (var pack in resolved)
        {
            foreach (var export in (pack.Manifest.Exports ?? new List<PackExport>())
                         .Where(x => string.Equals(x.ExportKind, "standard", StringComparison.OrdinalIgnoreCase)))
            {
                var fullPath = Path.Combine(pack.RootPath, export.RelativePath ?? string.Empty);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                files.Add(new StandardsResolvedFile
                {
                    FileName = Path.GetFileName(fullPath),
                    SourcePackId = pack.Manifest.PackId,
                    RelativePath = export.RelativePath ?? string.Empty,
                    ContentJson = File.ReadAllText(fullPath)
                });
            }
        }

        return new PolicyResolution
        {
            WorkspaceId = workspace.WorkspaceId,
            CapabilityDomain = NormalizeOrDefault(request.CapabilityDomain, CapabilityDomains.General),
            Discipline = NormalizeOrDefault(request.Discipline, CapabilityDisciplines.Common),
            IssueKinds = NormalizeDistinct(request.IssueKinds),
            CandidatePackIds = candidates.Select(x => x.Manifest.PackId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ResolvedPackIds = resolved.Select(x => x.Manifest.PackId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ResolvedPacks = resolved.Select(ToPolicyPackManifest).ToList(),
            Files = files
                .GroupBy(x => x.SourcePackId + "::" + x.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .OrderBy(x => x.SourcePackId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Summary = BuildSummary(workspace.WorkspaceId, request, resolved, files)
        };
    }

    private IEnumerable<PackCatalogEntry> ResolveCandidatePacks(WorkspaceManifest workspace, PolicyResolutionRequest request)
    {
        var ids = new List<string>();
        ids.AddRange(request.PreferredPackIds ?? new List<string>());
        ids.AddRange(workspace.PreferredStandardsPacks ?? new List<string>());
        ids.AddRange(workspace.EnabledPacks ?? new List<string>());

        var explicitCandidates = _packs.GetByIds(ids)
            .Where(x => string.Equals(x.Manifest.PackType, "standards-pack", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (explicitCandidates.Count > 0)
        {
            return explicitCandidates;
        }

        return _packs.GetAll()
            .Where(x => string.Equals(x.Manifest.PackType, "standards-pack", StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesPolicyPack(PackManifest manifest, PolicyResolutionRequest request)
    {
        var capabilityDomains = NormalizeDistinct(manifest.CapabilityDomains);
        var disciplines = NormalizeDistinct(manifest.SupportedDisciplines);
        var issueKinds = NormalizeDistinct(manifest.IssueKinds);
        var requestedIssueKinds = NormalizeDistinct(request.IssueKinds);
        var domain = NormalizeOrDefault(request.CapabilityDomain, CapabilityDomains.General);
        var discipline = NormalizeOrDefault(request.Discipline, CapabilityDisciplines.Common);

        var domainMatch = capabilityDomains.Count == 0 || capabilityDomains.Contains(domain, StringComparer.OrdinalIgnoreCase);
        var disciplineMatch = disciplines.Count == 0 || disciplines.Contains(discipline, StringComparer.OrdinalIgnoreCase) || disciplines.Contains(CapabilityDisciplines.Common, StringComparer.OrdinalIgnoreCase);
        var issueMatch = requestedIssueKinds.Count == 0 || issueKinds.Count == 0 || requestedIssueKinds.Any(x => issueKinds.Contains(x, StringComparer.OrdinalIgnoreCase));
        return domainMatch && disciplineMatch && issueMatch;
    }

    private static PolicyPackManifest ToPolicyPackManifest(PackCatalogEntry pack)
    {
        return new PolicyPackManifest
        {
            PackId = pack.Manifest.PackId,
            DisplayName = pack.Manifest.DisplayName,
            Description = pack.Manifest.Description,
            CapabilityDomains = NormalizeDistinct(pack.Manifest.CapabilityDomains),
            SupportedDisciplines = NormalizeDistinct(pack.Manifest.SupportedDisciplines),
            IssueKinds = NormalizeDistinct(pack.Manifest.IssueKinds),
            VerificationModes = NormalizeDistinct(pack.Manifest.VerificationModes)
        };
    }

    private static string BuildSummary(string workspaceId, PolicyResolutionRequest request, IReadOnlyCollection<PackCatalogEntry> resolved, IReadOnlyCollection<StandardsResolvedFile> files)
    {
        if (resolved.Count == 0)
        {
            return $"Workspace {workspaceId}: khong resolve duoc policy pack cho domain {NormalizeOrDefault(request.CapabilityDomain, CapabilityDomains.General)}.";
        }

        return $"Workspace {workspaceId}: resolved {resolved.Count} policy pack(s), {files.Count} policy file(s) cho domain {NormalizeOrDefault(request.CapabilityDomain, CapabilityDomains.General)}.";
    }

    internal static List<string> NormalizeDistinct(IEnumerable<string>? values)
    {
        return (values ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static string NormalizeOrDefault(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();
    }
}

public sealed class SpecialistRegistryService
{
    private readonly PackCatalogService _packs;
    private readonly WorkspaceCatalogService _workspaces;

    public SpecialistRegistryService(PackCatalogService packs, WorkspaceCatalogService workspaces)
    {
        _packs = packs ?? throw new ArgumentNullException(nameof(packs));
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
    }

    public CapabilitySpecialistResponse Resolve(CapabilitySpecialistRequest request)
    {
        request ??= new CapabilitySpecialistRequest();
        var workspace = _workspaces.GetManifest(request.WorkspaceId).Workspace;
        var requestedDomain = PolicyResolutionService.NormalizeOrDefault(request.CapabilityDomain, CapabilityDomains.General);
        var discipline = PolicyResolutionService.NormalizeOrDefault(request.Discipline, CapabilityDisciplines.Common);
        var issueKinds = PolicyResolutionService.NormalizeDistinct(request.IssueKinds);

        var allowedSpecialists = (workspace.AllowedSpecialists ?? new List<string>())
            .Concat(workspace.AllowedAgents ?? new List<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var descriptors = _packs.GetByIds(allowedSpecialists)
            .Where(x => string.Equals(x.Manifest.PackType, "agent-pack", StringComparison.OrdinalIgnoreCase))
            .Select(x => BuildDescriptor(x, requestedDomain, discipline, issueKinds))
            .Where(x => x.Score > 0 || string.Equals(requestedDomain, CapabilityDomains.General, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (descriptors.Count == 0)
        {
            descriptors = _packs.GetAll()
                .Where(x => string.Equals(x.Manifest.PackType, "agent-pack", StringComparison.OrdinalIgnoreCase))
                .Select(x => BuildDescriptor(x, requestedDomain, discipline, issueKinds))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
        }

        return new CapabilitySpecialistResponse
        {
            WorkspaceId = workspace.WorkspaceId,
            CapabilityDomain = requestedDomain,
            Discipline = discipline,
            Specialists = descriptors,
            Summary = descriptors.Count == 0
                ? $"Workspace {workspace.WorkspaceId}: khong tim thay specialist phu hop cho domain {requestedDomain}."
                : $"Workspace {workspace.WorkspaceId}: recommend {descriptors.Count} specialist(s) cho domain {requestedDomain}."
        };
    }

    private static CapabilitySpecialistDescriptor BuildDescriptor(PackCatalogEntry pack, string requestedDomain, string discipline, IReadOnlyCollection<string> issueKinds)
    {
        var packDomains = PolicyResolutionService.NormalizeDistinct(pack.Manifest.CapabilityDomains);
        if (packDomains.Count == 0)
        {
            packDomains = InferDomains(pack.Manifest.PackId);
        }

        var supportedDisciplines = PolicyResolutionService.NormalizeDistinct(pack.Manifest.SupportedDisciplines);
        if (supportedDisciplines.Count == 0)
        {
            supportedDisciplines.Add(CapabilityDisciplines.Common);
        }

        var supportedIssueKinds = PolicyResolutionService.NormalizeDistinct(pack.Manifest.IssueKinds);
        var score = 0;
        if (packDomains.Contains(requestedDomain, StringComparer.OrdinalIgnoreCase))
        {
            score += 6;
        }

        if (supportedDisciplines.Contains(discipline, StringComparer.OrdinalIgnoreCase) || supportedDisciplines.Contains(CapabilityDisciplines.Common, StringComparer.OrdinalIgnoreCase))
        {
            score += 3;
        }

        if (issueKinds.Count > 0 && supportedIssueKinds.Any(x => issueKinds.Contains(x, StringComparer.OrdinalIgnoreCase)))
        {
            score += 2;
        }

        if (requestedDomain == CapabilityDomains.General)
        {
            score += 1;
        }

        return new CapabilitySpecialistDescriptor
        {
            SpecialistId = pack.Manifest.Exports?.FirstOrDefault(x => string.Equals(x.ExportKind, "agent", StringComparison.OrdinalIgnoreCase))?.ExportId
                ?? pack.Manifest.PackId,
            PackId = pack.Manifest.PackId,
            DisplayName = string.IsNullOrWhiteSpace(pack.Manifest.DisplayName) ? pack.Manifest.PackId : pack.Manifest.DisplayName,
            Summary = pack.Manifest.Description,
            CapabilityDomains = packDomains,
            SupportedDisciplines = supportedDisciplines,
            IssueKinds = supportedIssueKinds,
            Score = score
        };
    }

    private static List<string> InferDomains(string packId)
    {
        var normalized = (packId ?? string.Empty).ToLowerInvariant();
        if (normalized.Contains("annotation"))
        {
            return new List<string> { CapabilityDomains.Annotation };
        }

        if (normalized.Contains("family"))
        {
            return new List<string> { CapabilityDomains.FamilyQa };
        }

        if (normalized.Contains("coordination") || normalized.Contains("sheet"))
        {
            return new List<string> { CapabilityDomains.Coordination };
        }

        if (normalized.Contains("system"))
        {
            return new List<string> { CapabilityDomains.Systems };
        }

        if (normalized.Contains("integration") || normalized.Contains("delivery"))
        {
            return new List<string> { CapabilityDomains.Integration };
        }

        if (normalized.Contains("audit") || normalized.Contains("governance"))
        {
            return new List<string> { CapabilityDomains.Governance };
        }

        return new List<string> { CapabilityDomains.Intent };
    }
}

public sealed class CapabilityTaskCompilerService
{
    private readonly ToolCapabilitySearchService _toolSearch;
    private readonly PlaybookOrchestrationService _playbooks;
    private readonly PolicyResolutionService _policies;
    private readonly SpecialistRegistryService _specialists;

    public CapabilityTaskCompilerService(
        ToolCapabilitySearchService toolSearch,
        PlaybookOrchestrationService playbooks,
        PolicyResolutionService policies,
        SpecialistRegistryService specialists)
    {
        _toolSearch = toolSearch ?? throw new ArgumentNullException(nameof(toolSearch));
        _playbooks = playbooks ?? throw new ArgumentNullException(nameof(playbooks));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _specialists = specialists ?? throw new ArgumentNullException(nameof(specialists));
    }

    public CompiledTaskPlan Compile(IEnumerable<ToolManifest> manifests, IntentCompileRequest request)
    {
        request ??= new IntentCompileRequest();
        var task = request.Task ?? new IntentTask();
        var workspaceId = PolicyResolutionService.NormalizeOrDefault(task.WorkspaceId, "default");
        var capabilityDomain = ResolveCapabilityDomain(task, request);
        var discipline = ResolveDiscipline(task, request);
        var issueKinds = ResolveIssueKinds(task);
        var catalog = (manifests ?? Array.Empty<ToolManifest>()).ToList();

        var playbook = _playbooks.Match(catalog, new PlaybookMatchRequest
        {
            WorkspaceId = workspaceId,
            Query = task.Query,
            DocumentContext = task.DocumentContext,
            MaxResults = 3,
            PreferredCapabilityDomain = capabilityDomain,
            Discipline = discipline
        }).RecommendedPlaybook;

        var policyResolution = _policies.Resolve(new PolicyResolutionRequest
        {
            WorkspaceId = workspaceId,
            CapabilityDomain = capabilityDomain,
            Discipline = discipline,
            IssueKinds = issueKinds,
            PreferredPackIds = playbook.PolicyPackIds?.ToList() ?? new List<string>()
        });

        var specialists = _specialists.Resolve(new CapabilitySpecialistRequest
        {
            WorkspaceId = workspaceId,
            CapabilityDomain = capabilityDomain,
            Discipline = discipline,
            IssueKinds = issueKinds
        });

        var toolMatches = _toolSearch.Search(catalog, new ToolCapabilityLookupRequest
        {
            Query = task.Query,
            MaxResults = Math.Max(8, catalog.Count == 0 ? 8 : 12),
            CapabilityDomain = capabilityDomain,
            Discipline = discipline,
            IssueKinds = issueKinds
        }).Matches.Select(x => x.Manifest).ToList();

        var filtered = toolMatches
            .Where(x => MatchesDomain(x, capabilityDomain))
            .Where(x => MatchesDiscipline(x, discipline))
            .Where(x => MatchesIssueKinds(x, issueKinds))
            .Distinct(new ToolManifestNameComparer())
            .ToList();
        if (filtered.Count == 0)
        {
            filtered = toolMatches.Distinct(new ToolManifestNameComparer()).ToList();
        }

        var plan = new CompiledTaskPlan
        {
            Task = new IntentTask
            {
                Query = task.Query ?? string.Empty,
                WorkspaceId = workspaceId,
                DocumentContext = task.DocumentContext ?? string.Empty,
                CapabilityDomain = capabilityDomain,
                Discipline = discipline,
                RequestedOutcome = task.RequestedOutcome ?? string.Empty,
                Constraints = PolicyResolutionService.NormalizeDistinct(task.Constraints),
                IssueKinds = issueKinds
            },
            CapabilityDomain = capabilityDomain,
            DeterminismLevel = ResolveDeterminismLevel(filtered, playbook, request.RequireDeterministicPlan),
            VerificationMode = ResolveVerificationMode(filtered, playbook, capabilityDomain),
            PolicyResolution = policyResolution,
            RecommendedPlaybook = playbook ?? new PlaybookRecommendation(),
            RecommendedSpecialists = specialists.Specialists.ToList(),
            CandidateToolNames = filtered.Select(x => x.ToolName).ToList(),
            IssueScanTools = filtered.Where(IsIssueScanTool).Select(x => x.ToolName).ToList(),
            FixTools = filtered.Where(IsFixTool).Select(x => x.ToolName).ToList(),
            VerifyTools = filtered.Where(IsVerifyTool).Select(x => x.ToolName).ToList()
        };
        plan.Summary = BuildSummary(plan);
        return plan;
    }

    public IntentValidationResponse Validate(IEnumerable<ToolManifest> manifests, IntentValidateRequest request)
    {
        request ??= new IntentValidateRequest();
        var plan = request.Plan ?? new CompiledTaskPlan();
        var errors = new List<string>();
        var warnings = new List<string>();
        var available = new HashSet<string>((manifests ?? Array.Empty<ToolManifest>()).Select(x => x.ToolName), StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(plan.CapabilityDomain))
        {
            errors.Add("CapabilityDomain is required.");
        }

        if (plan.CandidateToolNames.Count == 0)
        {
            errors.Add("Compiled plan does not contain any candidate tools.");
        }

        foreach (var toolName in plan.CandidateToolNames)
        {
            if (!available.Contains(toolName))
            {
                warnings.Add($"Tool '{toolName}' is not available in the current catalog.");
            }
        }

        if (plan.PolicyResolution.ResolvedPackIds.Count == 0)
        {
            warnings.Add(StatusCodes.PolicyResolutionNotFound);
        }

        if (plan.RecommendedSpecialists.Count == 0)
        {
            warnings.Add(StatusCodes.SpecialistResolutionNotFound);
        }

        if (plan.VerifyTools.Count == 0)
        {
            warnings.Add("Compiled plan does not include explicit verify tools.");
        }

        return new IntentValidationResponse
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings,
            Summary = errors.Count == 0
                ? $"Compiled plan for domain {plan.CapabilityDomain} is valid with {warnings.Count} warning(s)."
                : $"Compiled plan for domain {plan.CapabilityDomain} is invalid with {errors.Count} error(s)."
        };
    }

    private static string ResolveCapabilityDomain(IntentTask task, IntentCompileRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.PreferredCapabilityDomain))
        {
            return request.PreferredCapabilityDomain.Trim();
        }

        if (!string.IsNullOrWhiteSpace(task.CapabilityDomain))
        {
            return task.CapabilityDomain.Trim();
        }

        var query = (task.Query ?? string.Empty).ToLowerInvariant();
        if (ContainsAny(query, "sheet", "view", "naming", "workset", "parameter", "excel", "lod", "loi", "warning"))
        {
            return CapabilityDomains.Governance;
        }

        if (ContainsAny(query, "tag", "dimension", "room", "ceiling", "floor finish"))
        {
            return CapabilityDomains.Annotation;
        }

        if (ContainsAny(query, "family", "nested", "lod family", "family qa"))
        {
            return CapabilityDomains.FamilyQa;
        }

        if (ContainsAny(query, "clash", "clearance", "penetration", "opening", "fire damper"))
        {
            return CapabilityDomains.Coordination;
        }

        if (ContainsAny(query, "route", "routing", "slope", "duct", "pipe", "electrical", "fixture", "disconnected", "system"))
        {
            return CapabilityDomains.Systems;
        }

        if (ContainsAny(query, "cad", "point cloud", "pdf", "boq", "4d", "5d", "cost", "time", "split model"))
        {
            return CapabilityDomains.Integration;
        }

        if (ContainsAny(query, "natural language", "highlight", "filter", "script"))
        {
            return CapabilityDomains.Intent;
        }

        return CapabilityDomains.General;
    }

    private static string ResolveDiscipline(IntentTask task, IntentCompileRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Discipline))
        {
            return request.Discipline.Trim();
        }

        if (!string.IsNullOrWhiteSpace(task.Discipline))
        {
            return task.Discipline.Trim();
        }

        var query = (task.Query ?? string.Empty).ToLowerInvariant();
        if (ContainsAny(query, "duct", "hvac", "ahu", "fcu", "mechanical"))
        {
            return CapabilityDisciplines.Mechanical;
        }

        if (ContainsAny(query, "pipe", "plumbing", "drainage", "sanitary"))
        {
            return CapabilityDisciplines.Plumbing;
        }

        if (ContainsAny(query, "electrical", "cable", "tray", "wire", "panel"))
        {
            return CapabilityDisciplines.Electrical;
        }

        if (ContainsAny(query, "beam", "column", "slab", "concrete", "struct"))
        {
            return CapabilityDisciplines.Structure;
        }

        if (ContainsAny(query, "door", "room", "ceiling", "floor", "sheet", "view"))
        {
            return CapabilityDisciplines.Architecture;
        }

        if (ContainsAny(query, "mep"))
        {
            return CapabilityDisciplines.Mep;
        }

        return CapabilityDisciplines.Common;
    }

    private static List<string> ResolveIssueKinds(IntentTask task)
    {
        if (task.IssueKinds != null && task.IssueKinds.Count > 0)
        {
            return PolicyResolutionService.NormalizeDistinct(task.IssueKinds);
        }

        var query = (task.Query ?? string.Empty).ToLowerInvariant();
        var results = new List<string>();
        if (ContainsAny(query, "sheet", "naming", "view"))
        {
            results.Add(CapabilityIssueKinds.SheetPackage);
            results.Add(CapabilityIssueKinds.NamingConvention);
        }

        if (ContainsAny(query, "tag", "dimension"))
        {
            results.Add(CapabilityIssueKinds.TagOverlap);
            results.Add(CapabilityIssueKinds.DimensionCollision);
        }

        if (ContainsAny(query, "cleanup", "purge", "dwg"))
        {
            results.Add(CapabilityIssueKinds.ModelCleanup);
        }

        if (ContainsAny(query, "parameter", "excel", "spec"))
        {
            results.Add(CapabilityIssueKinds.ParameterPopulation);
        }

        if (ContainsAny(query, "room", "ceiling", "floor"))
        {
            results.Add(CapabilityIssueKinds.RoomFinishGeneration);
        }

        if (ContainsAny(query, "warning"))
        {
            results.Add(CapabilityIssueKinds.WarningTriage);
        }

        if (ContainsAny(query, "family"))
        {
            results.Add(CapabilityIssueKinds.FamilyQa);
        }

        if (ContainsAny(query, "clash", "penetration", "opening"))
        {
            results.Add(CapabilityIssueKinds.HardClash);
        }

        if (ContainsAny(query, "clearance"))
        {
            results.Add(CapabilityIssueKinds.ClearanceSoftClash);
        }

        if (ContainsAny(query, "lod", "loi"))
        {
            results.Add(CapabilityIssueKinds.LodLoiCompliance);
        }

        if (ContainsAny(query, "disconnected", "open end"))
        {
            results.Add(CapabilityIssueKinds.DisconnectedSystem);
        }

        if (ContainsAny(query, "slope"))
        {
            results.Add(CapabilityIssueKinds.SlopeContinuity);
        }

        if (ContainsAny(query, "route", "routing"))
        {
            results.Add(CapabilityIssueKinds.BasicRouting);
        }

        if (ContainsAny(query, "natural language", "script", "highlight"))
        {
            results.Add(CapabilityIssueKinds.IntentCompile);
        }

        if (ContainsAny(query, "cad", "point cloud"))
        {
            results.Add(CapabilityIssueKinds.ScanToBim);
        }

        if (ContainsAny(query, "boq", "cost", "4d", "5d"))
        {
            results.Add(CapabilityIssueKinds.ExternalSync);
        }

        if (ContainsAny(query, "sizing"))
        {
            results.Add(CapabilityIssueKinds.SystemSizing);
        }

        if (ContainsAny(query, "split model"))
        {
            results.Add(CapabilityIssueKinds.LargeModelSplit);
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ResolveDeterminismLevel(IEnumerable<ToolManifest> manifests, PlaybookRecommendation playbook, bool requireDeterministicPlan)
    {
        if (!string.IsNullOrWhiteSpace(playbook?.DeterminismLevel))
        {
            return playbook!.DeterminismLevel;
        }

        var levels = (manifests ?? Array.Empty<ToolManifest>()).Select(x => x.DeterminismLevel).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (levels.Contains(ToolDeterminismLevels.Scaffold, StringComparer.OrdinalIgnoreCase))
        {
            return requireDeterministicPlan ? ToolDeterminismLevels.PolicyBacked : ToolDeterminismLevels.Scaffold;
        }

        if (levels.Contains(ToolDeterminismLevels.Experimental, StringComparer.OrdinalIgnoreCase))
        {
            return requireDeterministicPlan ? ToolDeterminismLevels.PolicyBacked : ToolDeterminismLevels.Experimental;
        }

        if (levels.Contains(ToolDeterminismLevels.PolicyBacked, StringComparer.OrdinalIgnoreCase))
        {
            return ToolDeterminismLevels.PolicyBacked;
        }

        return ToolDeterminismLevels.Deterministic;
    }

    private static string ResolveVerificationMode(IEnumerable<ToolManifest> manifests, PlaybookRecommendation playbook, string capabilityDomain)
    {
        if (!string.IsNullOrWhiteSpace(playbook?.VerificationMode))
        {
            return playbook!.VerificationMode;
        }

        var preferred = (manifests ?? Array.Empty<ToolManifest>())
            .Select(x => x.VerificationMode)
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && !string.Equals(x, ToolVerificationModes.None, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        return capabilityDomain switch
        {
            var x when string.Equals(x, CapabilityDomains.Coordination, StringComparison.OrdinalIgnoreCase) => ToolVerificationModes.GeometryCheck,
            var x when string.Equals(x, CapabilityDomains.Systems, StringComparison.OrdinalIgnoreCase) => ToolVerificationModes.SystemConsistency,
            var x when string.Equals(x, CapabilityDomains.Governance, StringComparison.OrdinalIgnoreCase) => ToolVerificationModes.PolicyCheck,
            _ => ToolVerificationModes.ReportOnly
        };
    }

    private static bool MatchesDomain(ToolManifest manifest, string capabilityDomain)
    {
        return string.IsNullOrWhiteSpace(manifest.CapabilityDomain)
               || string.Equals(manifest.CapabilityDomain, CapabilityDomains.General, StringComparison.OrdinalIgnoreCase)
               || string.Equals(manifest.CapabilityDomain, capabilityDomain, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDiscipline(ToolManifest manifest, string discipline)
    {
        return manifest.SupportedDisciplines == null
               || manifest.SupportedDisciplines.Count == 0
               || manifest.SupportedDisciplines.Contains(discipline, StringComparer.OrdinalIgnoreCase)
               || manifest.SupportedDisciplines.Contains(CapabilityDisciplines.Common, StringComparer.OrdinalIgnoreCase);
    }

    private static bool MatchesIssueKinds(ToolManifest manifest, IReadOnlyCollection<string> issueKinds)
    {
        return issueKinds.Count == 0
               || manifest.IssueKinds == null
               || manifest.IssueKinds.Count == 0
               || manifest.IssueKinds.Any(x => issueKinds.Contains(x, StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsIssueScanTool(ToolManifest manifest)
    {
        return manifest.PermissionLevel == PermissionLevel.Read
               || manifest.PermissionLevel == PermissionLevel.Review;
    }

    private static bool IsFixTool(ToolManifest manifest)
    {
        return manifest.PermissionLevel == PermissionLevel.Mutate
               || manifest.PermissionLevel == PermissionLevel.FileLifecycle
               || manifest.ToolName.IndexOf(".plan_", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsVerifyTool(ToolManifest manifest)
    {
        return string.Equals(manifest.VerificationMode, ToolVerificationModes.GeometryCheck, StringComparison.OrdinalIgnoreCase)
               || string.Equals(manifest.VerificationMode, ToolVerificationModes.SystemConsistency, StringComparison.OrdinalIgnoreCase)
               || string.Equals(manifest.VerificationMode, ToolVerificationModes.PolicyCheck, StringComparison.OrdinalIgnoreCase)
               || manifest.ToolName.IndexOf("verify", StringComparison.OrdinalIgnoreCase) >= 0
               || manifest.ToolName.IndexOf("review", StringComparison.OrdinalIgnoreCase) >= 0
               || manifest.ToolName.IndexOf("qc", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string BuildSummary(CompiledTaskPlan plan)
    {
        return $"Compiled domain={plan.CapabilityDomain}; playbook={plan.RecommendedPlaybook.PlaybookId}; policies={plan.PolicyResolution.ResolvedPackIds.Count}; specialists={plan.RecommendedSpecialists.Count}; tools={plan.CandidateToolNames.Count}.";
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        return tokens.Any(token => value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private sealed class ToolManifestNameComparer : IEqualityComparer<ToolManifest>
    {
        public bool Equals(ToolManifest? x, ToolManifest? y)
        {
            return string.Equals(x?.ToolName, y?.ToolName, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(ToolManifest obj)
        {
            return (obj.ToolName ?? string.Empty).ToLowerInvariant().GetHashCode();
        }
    }
}
