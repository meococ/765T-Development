using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Copilot.Core;

public static class RepoLayoutService
{
    public static string GetAppDataRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            BIM765T.Revit.Contracts.Common.BridgeConstants.AppDataFolderName);
    }

    public static string FindRepoRoot(string? baseDirectory = null)
    {
        var current = new DirectoryInfo(baseDirectory ?? AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BIM765T.Revit.Agent.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return string.Empty;
    }

    public static string GetPacksRoot(string? baseDirectory = null)
    {
        return Path.Combine(FindRepoRoot(baseDirectory), "packs");
    }

    public static string GetRepoWorkspacesRoot(string? baseDirectory = null)
    {
        return Path.Combine(FindRepoRoot(baseDirectory), "workspaces");
    }

    public static string GetWorkspacesRoot(string? baseDirectory = null)
    {
        return GetRepoWorkspacesRoot(baseDirectory);
    }

    public static string GetDefaultWorkspacesRootPath()
    {
        return Path.Combine(GetAppDataRoot(), "workspaces");
    }

    public static string ResolveWorkspacesRoot(string? configuredRoot = null, string? baseDirectory = null)
    {
        var explicitRoot = NormalizeDirectoryPath(configuredRoot)
            ?? NormalizeDirectoryPath(Environment.GetEnvironmentVariable("BIM765T_PROJECT_WORKSPACE_ROOT"));
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return explicitRoot!;
        }

        var repoOverride = ResolveRepoWorkspacesRootOverride(baseDirectory);
        if (!string.IsNullOrWhiteSpace(repoOverride))
        {
            return repoOverride!;
        }

        return GetDefaultWorkspacesRootPath();
    }

    public static string? ResolveRepoWorkspacesRootOverride(string? baseDirectory = null)
    {
        var normalizedBaseDirectory = NormalizeDirectoryPath(baseDirectory);
        if (string.IsNullOrWhiteSpace(normalizedBaseDirectory))
        {
            return null;
        }

        if (!File.Exists(Path.Combine(normalizedBaseDirectory, "BIM765T.Revit.Agent.sln")))
        {
            return null;
        }

        var workspacesRoot = Path.Combine(normalizedBaseDirectory, "workspaces");
        return Directory.Exists(workspacesRoot) ? workspacesRoot : null;
    }

    public static string? NormalizeDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Path.GetFullPath(path!.Trim());
    }

    public static string GetCatalogRoot(string? baseDirectory = null)
    {
        return Path.Combine(FindRepoRoot(baseDirectory), "catalog");
    }

    public static string GetDistRoot(string? baseDirectory = null)
    {
        return Path.Combine(FindRepoRoot(baseDirectory), "dist");
    }
}

public sealed class PackCatalogService
{
    private readonly IReadOnlyDictionary<string, PackCatalogEntry> _packs;
    private readonly IReadOnlyDictionary<string, PackCatalogEntry> _packsByAnyId;

    public PackCatalogService(string? baseDirectory = null)
        : this(LoadAll(RepoLayoutService.FindRepoRoot(baseDirectory)))
    {
    }

    public PackCatalogService(IEnumerable<PackCatalogEntry> packs)
    {
        var materialized = (packs ?? Array.Empty<PackCatalogEntry>())
            .Where(x => x?.Manifest != null && !string.IsNullOrWhiteSpace(x.Manifest.PackId))
            .GroupBy(x => x.Manifest.PackId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

        _packs = materialized;
        _packsByAnyId = BuildAliasIndex(materialized.Values);
    }

    public IReadOnlyList<PackCatalogEntry> GetAll()
    {
        return _packs.Values
            .OrderBy(x => x.Manifest.PackType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Manifest.PackId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool TryGet(string packId, out PackCatalogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(packId))
        {
            entry = new PackCatalogEntry();
            return false;
        }

        return _packsByAnyId.TryGetValue(packId, out entry!);
    }

    public IReadOnlyList<PackCatalogEntry> GetByIds(IEnumerable<string>? packIds)
    {
        var ids = new HashSet<string>((packIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
        {
            return Array.Empty<PackCatalogEntry>();
        }

        return _packs.Values
            .Where(x => ids.Any(id => IsPackMatch(x.Manifest, id)))
            .OrderBy(x => x.Manifest.PackId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsPackMatch(PackManifest manifest, string packId)
    {
        if (string.Equals(manifest.PackId, packId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (manifest.LegacyPackIds ?? new List<string>())
            .Any(x => string.Equals(x, packId, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyDictionary<string, PackCatalogEntry> BuildAliasIndex(IEnumerable<PackCatalogEntry> packs)
    {
        var index = new Dictionary<string, PackCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in packs)
        {
            index[entry.Manifest.PackId] = entry;
            foreach (var alias in entry.Manifest.LegacyPackIds ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    index[alias] = entry;
                }
            }
        }

        return index;
    }

    public static IReadOnlyList<PackCatalogEntry> LoadAll(string? repoRoot)
    {
        var packsRoot = Path.Combine(repoRoot ?? string.Empty, "packs");
        if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(packsRoot))
        {
            return Array.Empty<PackCatalogEntry>();
        }

        var results = new List<PackCatalogEntry>();
        foreach (var file in Directory.GetFiles(packsRoot, "pack.json", SearchOption.AllDirectories))
        {
            try
            {
                var manifest = JsonUtil.DeserializeRequired<PackManifest>(File.ReadAllText(file));
                if (string.IsNullOrWhiteSpace(manifest.PackId))
                {
                    continue;
                }

                results.Add(new PackCatalogEntry
                {
                    Manifest = manifest,
                    RootPath = Path.GetDirectoryName(file) ?? string.Empty,
                    SourcePath = file
                });
            }
            catch
            {
            }
        }

        return results;
    }
}

public sealed class WorkspaceCatalogService
{
    private readonly IReadOnlyDictionary<string, WorkspaceManifestResponse> _workspaces;
    private readonly string _workspacesRoot;

    public WorkspaceCatalogService(string? baseDirectory = null, string? workspaceRootPath = null)
        : this(LoadAll(RepoLayoutService.ResolveWorkspacesRoot(workspaceRootPath, baseDirectory)))
    {
        _workspacesRoot = RepoLayoutService.ResolveWorkspacesRoot(workspaceRootPath, baseDirectory);
    }

    public WorkspaceCatalogService(IEnumerable<WorkspaceManifestResponse> workspaces)
    {
        _workspacesRoot = string.Empty;
        _workspaces = (workspaces ?? Array.Empty<WorkspaceManifestResponse>())
            .Where(x => x?.Workspace != null && !string.IsNullOrWhiteSpace(x.Workspace.WorkspaceId))
            .GroupBy(x => x.Workspace.WorkspaceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<WorkspaceManifestResponse> GetAll()
    {
        return GetSnapshot().Values
            .OrderBy(x => x.Workspace.WorkspaceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public WorkspaceManifestResponse GetManifest(string? workspaceId)
    {
        var requestedWorkspaceId = workspaceId == null ? string.Empty : workspaceId.Trim();
        var snapshot = GetSnapshot();
        if (requestedWorkspaceId.Length > 0 && snapshot.TryGetValue(requestedWorkspaceId, out var selected))
        {
            return selected;
        }

        if (snapshot.TryGetValue("default", out var defaultWorkspace))
        {
            return defaultWorkspace;
        }

        return new WorkspaceManifestResponse
        {
            Workspace = new WorkspaceManifest
            {
                WorkspaceId = requestedWorkspaceId.Length == 0 ? "default" : requestedWorkspaceId,
                DisplayName = "Default Workspace"
            },
            RootPath = string.Empty
        };
    }


    private IReadOnlyDictionary<string, WorkspaceManifestResponse> GetSnapshot()
    {
        if (string.IsNullOrWhiteSpace(_workspacesRoot))
        {
            return _workspaces;
        }

        return (LoadAll(_workspacesRoot) ?? Array.Empty<WorkspaceManifestResponse>())
            .Where(x => x?.Workspace != null && !string.IsNullOrWhiteSpace(x.Workspace.WorkspaceId))
            .GroupBy(x => x.Workspace.WorkspaceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<WorkspaceManifestResponse> LoadAll(string? rootPath)
    {
        var workspacesRoot = ResolveWorkspaceCatalogRoot(rootPath);
        if (string.IsNullOrWhiteSpace(workspacesRoot) || !Directory.Exists(workspacesRoot))
        {
            return Array.Empty<WorkspaceManifestResponse>();
        }

        var results = new List<WorkspaceManifestResponse>();
        foreach (var file in Directory.GetFiles(workspacesRoot, "workspace.json", SearchOption.AllDirectories))
        {
            try
            {
                var manifest = JsonUtil.DeserializeRequired<WorkspaceManifest>(File.ReadAllText(file));
                if (string.IsNullOrWhiteSpace(manifest.WorkspaceId))
                {
                    continue;
                }

                results.Add(new WorkspaceManifestResponse
                {
                    Workspace = manifest,
                    RootPath = Path.GetDirectoryName(file) ?? string.Empty
                });
            }
            catch
            {
            }
        }

        return results;
    }

    private static string ResolveWorkspaceCatalogRoot(string? rootPath)
    {
        var normalized = RepoLayoutService.NormalizeDirectoryPath(rootPath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var nestedRoot = Path.Combine(normalized, "workspaces");
        return Directory.Exists(nestedRoot) ? nestedRoot : normalized!;
    }
}

public sealed class StandardsCatalogService
{
    private readonly PackCatalogService _packs;
    private readonly WorkspaceCatalogService _workspaces;

    public StandardsCatalogService(PackCatalogService packs, WorkspaceCatalogService workspaces)
    {
        _packs = packs ?? throw new ArgumentNullException(nameof(packs));
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
    }

    public StandardsResolution Resolve(StandardsResolutionRequest request)
    {
        request ??= new StandardsResolutionRequest();
        var workspace = _workspaces.GetManifest(request.WorkspaceId).Workspace;
        var candidatePacks = ResolveCandidatePacks(request, workspace).ToList();
        var files = new List<StandardsResolvedFile>();
        var values = new List<StandardsResolvedValue>();

        foreach (var pack in candidatePacks)
        {
            foreach (var export in SelectStandardExports(pack, request.StandardKind))
            {
                var fullPath = Path.Combine(pack.RootPath, export.RelativePath ?? string.Empty);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                var relativePath = GetRelativePath(pack.RootPath, fullPath);
                var contentJson = File.ReadAllText(fullPath);
                var file = new StandardsResolvedFile
                {
                    FileName = Path.GetFileName(fullPath),
                    SourcePackId = pack.Manifest.PackId,
                    RelativePath = relativePath,
                    ContentJson = contentJson
                };
                files.Add(file);

                foreach (var requestedKey in request.RequestedKeys ?? new List<string>())
                {
                    if (!ShouldCheckKeyAgainstFile(requestedKey, file.FileName))
                    {
                        continue;
                    }

                    if (values.Any(x => string.Equals(x.RequestedKey, requestedKey, StringComparison.OrdinalIgnoreCase) && x.Matched))
                    {
                        continue;
                    }

                    var path = ExtractPathPart(requestedKey);
                    if (TryResolveJsonPath(contentJson, path, out var value))
                    {
                        values.Add(new StandardsResolvedValue
                        {
                            RequestedKey = requestedKey,
                            Value = value,
                            SourcePackId = pack.Manifest.PackId,
                            SourceFile = file.FileName,
                            Matched = true
                        });
                    }
                }
            }
        }

        foreach (var requestedKey in (request.RequestedKeys ?? new List<string>()).Where(x => !values.Any(v => string.Equals(v.RequestedKey, x, StringComparison.OrdinalIgnoreCase) && v.Matched)))
        {
            values.Add(new StandardsResolvedValue
            {
                RequestedKey = requestedKey,
                Value = string.Empty,
                SourcePackId = string.Empty,
                SourceFile = string.Empty,
                Matched = false
            });
        }

        return new StandardsResolution
        {
            WorkspaceId = workspace.WorkspaceId,
            StandardKind = request.StandardKind ?? string.Empty,
            Discipline = request.Discipline ?? string.Empty,
            CandidatePackIds = candidatePacks.Select(x => x.Manifest.PackId).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Files = files
                .GroupBy(x => x.SourcePackId + "::" + x.RelativePath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.SourcePackId, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Values = values.OrderBy(x => x.RequestedKey, StringComparer.OrdinalIgnoreCase).ToList(),
            Summary = BuildStandardsSummary(workspace.WorkspaceId, candidatePacks.Count, files.Count, values)
        };
    }

    private IEnumerable<PackCatalogEntry> ResolveCandidatePacks(StandardsResolutionRequest request, WorkspaceManifest workspace)
    {
        var preferred = (request.PreferredPackIds ?? new List<string>())
            .Concat(workspace.PreferredStandardsPacks ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (preferred.Count > 0)
        {
            var preferredMatches = _packs.GetByIds(preferred)
                .Where(x => string.Equals(x.Manifest.PackType, "standards-pack", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (preferredMatches.Count > 0)
            {
                return preferredMatches;
            }
        }

        var enabledMatches = _packs.GetByIds(workspace.EnabledPacks)
            .Where(x => string.Equals(x.Manifest.PackType, "standards-pack", StringComparison.OrdinalIgnoreCase)
                || x.Manifest.EnabledByDefault)
            .ToList();
        if (enabledMatches.Count > 0)
        {
            return enabledMatches;
        }

        return _packs.GetAll()
            .Where(x => string.Equals(x.Manifest.PackType, "standards-pack", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<PackExport> SelectStandardExports(PackCatalogEntry pack, string? standardKind)
    {
        var exports = pack.Manifest.Exports ?? new List<PackExport>();
        var filtered = exports.Where(x => string.Equals(x.ExportKind, "standard", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(standardKind))
        {
            return filtered;
        }

        var narrowed = filtered.Where(x => x.ExportId.IndexOf(standardKind, StringComparison.OrdinalIgnoreCase) >= 0
            || x.RelativePath.IndexOf(standardKind, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
        return narrowed.Count > 0 ? narrowed : filtered;
    }

    private static bool ShouldCheckKeyAgainstFile(string requestedKey, string fileName)
    {
        if (string.IsNullOrWhiteSpace(requestedKey))
        {
            return false;
        }

        var hashIndex = requestedKey.IndexOf('#');
        if (hashIndex <= 0)
        {
            return true;
        }

        var filePart = requestedKey.Substring(0, hashIndex).Trim();
        return string.Equals(filePart, fileName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(filePart, Path.GetFileNameWithoutExtension(fileName), StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractPathPart(string requestedKey)
    {
        if (string.IsNullOrWhiteSpace(requestedKey))
        {
            return string.Empty;
        }

        var hashIndex = requestedKey.IndexOf('#');
        return hashIndex >= 0 ? requestedKey.Substring(hashIndex + 1).Trim() : requestedKey.Trim();
    }

    private static bool TryResolveJsonPath(string json, string path, out string value)
    {
        value = string.Empty;
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var current = document.RootElement;
            foreach (var segment in path.Split(new[] { '.', '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (current.ValueKind == JsonValueKind.Object)
                {
                    if (!TryGetPropertyIgnoreCase(current, segment, out current))
                    {
                        return false;
                    }

                    continue;
                }

                if (current.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index) && index >= 0 && index < current.GetArrayLength())
                {
                    current = current.EnumerateArray().ElementAt(index);
                    continue;
                }

                return false;
            }

            value = current.ValueKind == JsonValueKind.String
                ? current.GetString() ?? string.Empty
                : current.GetRawText();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string BuildStandardsSummary(string workspaceId, int packCount, int fileCount, IEnumerable<StandardsResolvedValue> values)
    {
        var total = values.Count();
        var matched = values.Count(x => x.Matched);
        return $"Workspace {workspaceId}: resolved {matched}/{total} key(s) tu {packCount} standards pack(s), {fileCount} file(s).";
    }

    private static string GetRelativePath(string rootPath, string fullPath)
    {
        var normalizedRoot = (rootPath ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedFull = fullPath ?? string.Empty;
        if (normalizedRoot.Length == 0 || normalizedFull.Length == 0)
        {
            return normalizedFull;
        }

        if (normalizedFull.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedFull.Substring(normalizedRoot.Length + 1);
        }

        if (normalizedFull.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedFull.Substring(normalizedRoot.Length + 1);
        }

        return normalizedFull;
    }
}

public sealed class PlaybookOrchestrationService
{
    private readonly PlaybookLoaderService _playbooks;
    private readonly PackCatalogService _packs;
    private readonly WorkspaceCatalogService _workspaces;
    private readonly StandardsCatalogService _standards;

    public PlaybookOrchestrationService(PlaybookLoaderService playbooks, PackCatalogService packs, WorkspaceCatalogService workspaces, StandardsCatalogService standards)
    {
        _playbooks = playbooks ?? throw new ArgumentNullException(nameof(playbooks));
        _packs = packs ?? throw new ArgumentNullException(nameof(packs));
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
        _standards = standards ?? throw new ArgumentNullException(nameof(standards));
    }

    public PlaybookMatchResponse Match(IEnumerable<ToolManifest> manifests, PlaybookMatchRequest request)
    {
        request ??= new PlaybookMatchRequest();
        var workspace = _workspaces.GetManifest(request.WorkspaceId).Workspace;
        var availableTools = new HashSet<string>((manifests ?? Array.Empty<ToolManifest>()).Select(x => x.ToolName), StringComparer.OrdinalIgnoreCase);
        var candidates = GetCandidatePlaybooks(workspace)
            .Select(x => new { Playbook = x, Score = ScorePlaybook(x, request.Query, request.DocumentContext, availableTools, request.PreferredCapabilityDomain, request.Discipline) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Playbook.PlaybookId, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, request.MaxResults))
            .ToList();

        var matches = candidates.Select(x => ToRecommendation(x.Playbook, x.Score, candidates.Select(c => c.Playbook.PlaybookId).ToList())).ToList();
        var recommended = matches.FirstOrDefault() ?? new PlaybookRecommendation { DontUseReason = "Khong tim thay playbook phu hop." };
        return new PlaybookMatchResponse
        {
            WorkspaceId = workspace.WorkspaceId,
            Query = request.Query ?? string.Empty,
            RecommendedPlaybook = recommended,
            Matches = matches
        };
    }

    public PlaybookPreviewResponse Preview(IEnumerable<ToolManifest> manifests, PlaybookPreviewRequest request)
    {
        request ??= new PlaybookPreviewRequest();
        var workspace = _workspaces.GetManifest(request.WorkspaceId).Workspace;
        var playbook = GetCandidatePlaybooks(workspace)
            .FirstOrDefault(x => string.Equals(x.PlaybookId, request.PlaybookId, StringComparison.OrdinalIgnoreCase));

        if (playbook == null)
        {
            return new PlaybookPreviewResponse
            {
                WorkspaceId = workspace.WorkspaceId,
                PlaybookId = request.PlaybookId ?? string.Empty,
                Summary = "Khong tim thay playbook trong workspace hien tai.",
                Standards = new StandardsResolution { WorkspaceId = workspace.WorkspaceId }
            };
        }

        var standards = _standards.Resolve(new StandardsResolutionRequest
        {
            WorkspaceId = workspace.WorkspaceId,
            StandardKind = InferStandardsKind(playbook),
            RequestedKeys = playbook.StandardsRefs ?? new List<string>(),
            PreferredPackIds = workspace.PreferredStandardsPacks ?? new List<string>()
        });

        return new PlaybookPreviewResponse
        {
            WorkspaceId = workspace.WorkspaceId,
            PlaybookId = playbook.PlaybookId,
            Summary = BuildPreviewSummary(playbook, manifests, standards),
            RequiredInputs = playbook.RequiredInputs?.ToList() ?? new List<string>(),
            StandardsRefs = playbook.StandardsRefs?.ToList() ?? new List<string>(),
            Standards = standards,
            CapabilityDomain = playbook.CapabilityDomain,
            DeterminismLevel = playbook.DeterminismLevel,
            VerificationMode = playbook.VerificationMode,
            RecommendedSpecialists = playbook.RecommendedSpecialists?.ToList() ?? new List<string>(),
            PolicyPackIds = playbook.PolicyPackIds?.ToList() ?? new List<string>(),
            Steps = (playbook.Steps ?? new List<PlaybookStepDefinition>()).Select(x => new PlaybookPreviewStep
            {
                StepName = x.StepName,
                Tool = x.Tool,
                Purpose = x.Purpose,
                Condition = x.Condition,
                Verify = x.Verify,
                ParametersJson = x.ParametersJson,
                OutputKey = x.OutputKey,
                StepId = x.StepId,
                StepKind = x.StepKind,
                LoopOver = x.LoopOver,
                RequiredStandardsRefs = x.RequiredStandardsRefs?.ToList() ?? new List<string>()
            }).ToList()
        };
    }

    private IEnumerable<PlaybookDefinition> GetCandidatePlaybooks(WorkspaceManifest workspace)
    {
        var preferredPackIds = (workspace.PreferredPlaybookPacks ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var enabledPackIds = (workspace.EnabledPacks ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var playbooks = _playbooks.GetAll();
        if (preferredPackIds.Count > 0)
        {
            var preferredMatches = playbooks.Where(x => preferredPackIds.Contains(x.PackId, StringComparer.OrdinalIgnoreCase)).ToList();
            if (preferredMatches.Count > 0)
            {
                return preferredMatches;
            }
        }

        if (enabledPackIds.Count > 0)
        {
            var enabledMatches = playbooks.Where(x => string.IsNullOrWhiteSpace(x.PackId)
                || enabledPackIds.Contains(x.PackId, StringComparer.OrdinalIgnoreCase)
                || !_packs.TryGet(x.PackId, out _)).ToList();
            if (enabledMatches.Count > 0)
            {
                return enabledMatches;
            }
        }

        return playbooks;
    }

    private static int ScorePlaybook(PlaybookDefinition playbook, string? query, string? documentContext, ISet<string> availableTools, string? preferredCapabilityDomain, string? discipline)
    {
        if (playbook == null)
        {
            return 0;
        }

        var taskLower = (query ?? string.Empty).ToLowerInvariant();
        if (taskLower.Length == 0)
        {
            return 0;
        }

        if (!string.IsNullOrWhiteSpace(playbook.RequiredContext)
            && !string.IsNullOrWhiteSpace(documentContext)
            && !string.Equals(playbook.RequiredContext, documentContext, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var score = 0;
        foreach (var phrase in playbook.TriggerPhrases ?? new List<string>())
        {
            if (ContainsKeywords(taskLower, phrase.ToLowerInvariant()))
            {
                score += 30;
            }
        }

        if (playbook.DecisionGate != null)
        {
            foreach (var cond in playbook.DecisionGate.UseWhen ?? new List<string>())
            {
                if (ContainsKeywords(taskLower, cond.ToLowerInvariant()))
                {
                    score += 25;
                }
            }

            foreach (var cond in playbook.DecisionGate.DontUseWhen ?? new List<string>())
            {
                if (ContainsKeywords(taskLower, cond.ToLowerInvariant()))
                {
                    score -= 35;
                }
            }
        }

        foreach (var part in (playbook.PlaybookId ?? string.Empty).Replace('_', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length > 2 && taskLower.Contains(part.ToLowerInvariant()))
            {
                score += 10;
            }
        }

        foreach (var part in (playbook.Description ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length > 3 && taskLower.Contains(part.ToLowerInvariant()))
            {
                score += 5;
            }
        }

        foreach (var step in playbook.Steps ?? new List<PlaybookStepDefinition>())
        {
            if (!string.IsNullOrWhiteSpace(step.Tool) && availableTools.Count > 0 && !availableTools.Contains(step.Tool))
            {
                score -= 10;
            }
        }

        if (!string.IsNullOrWhiteSpace(preferredCapabilityDomain)
            && string.Equals(playbook.CapabilityDomain, preferredCapabilityDomain, StringComparison.OrdinalIgnoreCase))
        {
            score += 18;
        }

        if (!string.IsNullOrWhiteSpace(discipline)
            && ((playbook.SupportedDisciplines ?? new List<string>()).Count == 0
                || playbook.SupportedDisciplines.Contains(discipline, StringComparer.OrdinalIgnoreCase)
                || playbook.SupportedDisciplines.Contains(CapabilityDisciplines.Common, StringComparer.OrdinalIgnoreCase)))
        {
            score += 8;
        }

        score += Math.Min(10, (playbook.StandardsRefs ?? new List<string>()).Count * 2);
        score += Math.Min(8, (playbook.RequiredInputs ?? new List<string>()).Count);
        return Math.Max(0, score);
    }

    private static PlaybookRecommendation ToRecommendation(PlaybookDefinition playbook, int score, IReadOnlyList<string> rankedPlaybookIds)
    {
        return new PlaybookRecommendation
        {
            PlaybookId = playbook.PlaybookId,
            Description = playbook.Description,
            Confidence = Math.Min(1.0d, score / 100.0d),
            Steps = (playbook.Steps ?? new List<PlaybookStepDefinition>()).Select(x => new PlaybookStepSummary
            {
                StepName = x.StepName,
                Tool = x.Tool,
                Purpose = x.Purpose,
                Condition = x.Condition
            }).ToList(),
            AlternativePlaybooks = rankedPlaybookIds
                .Where(x => !string.Equals(x, playbook.PlaybookId, StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList(),
            PackId = playbook.PackId,
            StandardsRefs = playbook.StandardsRefs?.ToList() ?? new List<string>(),
            RequiredInputs = playbook.RequiredInputs?.ToList() ?? new List<string>(),
            RecommendedSpecialists = playbook.RecommendedSpecialists?.ToList() ?? new List<string>(),
            CapabilityDomain = playbook.CapabilityDomain,
            DeterminismLevel = playbook.DeterminismLevel,
            VerificationMode = playbook.VerificationMode,
            SupportedDisciplines = playbook.SupportedDisciplines?.ToList() ?? new List<string>(),
            IssueKinds = playbook.IssueKinds?.ToList() ?? new List<string>(),
            PolicyPackIds = playbook.PolicyPackIds?.ToList() ?? new List<string>()
        };
    }

    private static bool ContainsKeywords(string text, string condition)
    {
        var keywords = condition.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.Length > 2)
            .ToList();
        if (keywords.Count == 0)
        {
            return false;
        }

        var matches = keywords.Count(x => text.Contains(x));
        return matches >= Math.Max(1, keywords.Count / 2);
    }

    private static string InferStandardsKind(PlaybookDefinition playbook)
    {
        if ((playbook.StandardsRefs ?? new List<string>()).Any(x => x.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0
            || x.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0
            || x.IndexOf("template", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return "sheet";
        }

        return "standards";
    }

    private static string BuildPreviewSummary(PlaybookDefinition playbook, IEnumerable<ToolManifest> manifests, StandardsResolution standards)
    {
        var manifestSet = new HashSet<string>((manifests ?? Array.Empty<ToolManifest>()).Select(x => x.ToolName), StringComparer.OrdinalIgnoreCase);
        var missingTools = (playbook.Steps ?? new List<PlaybookStepDefinition>())
            .Where(x => !string.IsNullOrWhiteSpace(x.Tool) && manifestSet.Count > 0 && !manifestSet.Contains(x.Tool))
            .Select(x => x.Tool)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var status = missingTools.Count == 0
            ? "Tool chain san sang"
            : "Can bo sung/migrate mot so tool trong chain";
        return $"{status}: {playbook.PlaybookId} co {(playbook.Steps ?? new List<PlaybookStepDefinition>()).Count} buoc, {(playbook.StandardsRefs ?? new List<string>()).Count} standards ref, {standards.Values.Count(x => x.Matched)}/{standards.Values.Count} standards key match.";
    }
}
