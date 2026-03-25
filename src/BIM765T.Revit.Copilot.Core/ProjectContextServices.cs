using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Copilot.Core;

public sealed class ProjectInitService
{
    private static readonly string[] ExcludedDirectoryNames =
    {
        ".git",
        ".vs",
        ".idea",
        ".assistant",
        ".claude",
        "bin",
        "obj",
        "node_modules",
        "workspaces",
        "catalog",
        "dist"
    };

    private readonly PackCatalogService _packs;
    private readonly WorkspaceCatalogService _workspaces;
    private readonly string _workspacesRoot;

    public ProjectInitService(PackCatalogService packs, WorkspaceCatalogService workspaces, string? baseDirectory = null, string? workspaceRootPath = null)
    {
        _packs = packs ?? throw new ArgumentNullException(nameof(packs));
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
        _workspacesRoot = RepoLayoutService.ResolveWorkspacesRoot(workspaceRootPath, baseDirectory);
    }

    public ProjectInitPreviewResponse Preview(ProjectInitPreviewRequest request)
    {
        request ??= new ProjectInitPreviewRequest();
        var response = new ProjectInitPreviewResponse();
        var errors = new List<string>();
        var warnings = new List<string>();
        var normalizedRoot = NormalizeExistingPath(request.SourceRootPath);
        if (string.IsNullOrWhiteSpace(normalizedRoot) || !Directory.Exists(normalizedRoot))
        {
            errors.Add("Source root khong ton tai hoac khong hop le.");
            response.StatusCode = StatusCodes.ProjectSourceRootNotFound;
            response.IsValid = false;
            response.Errors = errors;
            response.Warnings = warnings;
            response.Summary = "Khong the project init vi source root khong ton tai.";
            response.OnboardingStatus = new OnboardingStatusDto
            {
                WorkspaceId = NormalizeWorkspaceId(request.WorkspaceId),
                WorkspaceRootPath = string.IsNullOrWhiteSpace(request.WorkspaceId)
                    ? string.Empty
                    : Path.Combine(_workspacesRoot, NormalizeWorkspaceId(request.WorkspaceId)),
                InitStatus = ProjectOnboardingStatuses.Blocked,
                DeepScanStatus = ProjectDeepScanStatuses.NotStarted,
                Summary = response.Summary
            };
            return response;
        }

        var manifest = DiscoverManifest(normalizedRoot!, request.PrimaryRevitFilePath);
        var resolvedWorkspaceId = ResolveWorkspaceId(request.WorkspaceId, normalizedRoot!);
        var workspaceRoot = Path.Combine(_workspacesRoot, resolvedWorkspaceId);
        var (validFirmPackIds, packErrors, packWarnings) = ValidateFirmPackIds(request.FirmPackIds);
        errors.AddRange(packErrors);
        warnings.AddRange(packWarnings);

        response.SuggestedWorkspaceId = resolvedWorkspaceId;
        response.WorkspaceId = resolvedWorkspaceId;
        response.WorkspaceExists = Directory.Exists(workspaceRoot) || File.Exists(Path.Combine(workspaceRoot, "workspace.json"));
        response.Manifest = manifest;
        response.ManifestStats = manifest.Stats ?? BuildStats(manifest);
        response.FirmPackIds = validFirmPackIds;
        response.EffectivePackIds = BuildEffectivePackIds(validFirmPackIds);

        if (response.ManifestStats.RevitProjectCount > 1 && string.IsNullOrWhiteSpace(manifest.PrimaryRevitFilePath))
        {
            response.RequiresPrimaryRevitSelection = true;
            errors.Add("Co nhieu file .rvt; can chon PrimaryRevitFilePath ro rang.");
        }

        if (response.ManifestStats.TotalFiles == 0)
        {
            warnings.Add("Source root chua co file .rvt/.rfa/.pdf phu hop de bootstrap.");
        }

        response.LayerSummaries = BuildPreviewLayers(validFirmPackIds, manifest);
        response.Errors = errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        response.Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        response.IsValid = response.Errors.Count == 0;
        response.StatusCode = response.IsValid ? StatusCodes.Ok : StatusCodes.InvalidRequest;
        response.Summary = BuildPreviewSummary(response, normalizedRoot!);
        response.OnboardingStatus = BuildPreviewOnboardingStatus(response, workspaceRoot);
        return response;
    }

    public ProjectInitApplyResponse Apply(ProjectInitApplyRequest request, ProjectPrimaryModelReport? livePrimaryModelReport = null)
    {
        request ??= new ProjectInitApplyRequest();
        var preview = Preview(new ProjectInitPreviewRequest
        {
            SourceRootPath = request.SourceRootPath,
            WorkspaceId = request.WorkspaceId,
            DisplayName = request.DisplayName,
            FirmPackIds = request.FirmPackIds?.ToList() ?? new List<string>(),
            PrimaryRevitFilePath = request.PrimaryRevitFilePath
        });

        if (!preview.IsValid)
        {
            throw new InvalidOperationException(!string.IsNullOrWhiteSpace(preview.StatusCode) ? preview.StatusCode : StatusCodes.InvalidRequest);
        }

        if (preview.RequiresPrimaryRevitSelection)
        {
            throw new InvalidOperationException(StatusCodes.ProjectPrimaryModelSelectionRequired);
        }

        var workspaceRoot = Path.Combine(_workspacesRoot, preview.WorkspaceId);
        if (preview.WorkspaceExists && !request.AllowExistingWorkspaceOverwrite)
        {
            throw new InvalidOperationException(StatusCodes.ProjectWorkspaceConflict);
        }

        Directory.CreateDirectory(workspaceRoot);
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "reports"));
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "memory"));
        Directory.CreateDirectory(Path.Combine(workspaceRoot, "scratch"));

        var workspaceManifestPath = Path.Combine(workspaceRoot, "workspace.json");
        var projectContextPath = Path.Combine(workspaceRoot, "project.context.json");
        var manifestReportPath = Path.Combine(workspaceRoot, "reports", "project-init.manifest.json");
        var summaryReportPath = Path.Combine(workspaceRoot, "reports", "project-init.summary.md");
        var briefPath = Path.Combine(workspaceRoot, "memory", "project-brief.md");
        var primaryModelReportPath = Path.Combine(workspaceRoot, "reports", "project-init.primary-model.json");

        var workspaceManifest = BuildWorkspaceManifest(preview, request.DisplayName);
        var primaryModelReport = BuildPrimaryModelReport(preview.Manifest, request.IncludeLivePrimaryModelSummary, livePrimaryModelReport);
        var projectContext = BuildProjectContextState(preview, request.DisplayName, manifestReportPath, primaryModelReport.Status);

        WriteJson(workspaceManifestPath, workspaceManifest);
        WriteJson(projectContextPath, projectContext);
        WriteJson(manifestReportPath, preview.Manifest);
        WriteMarkdown(summaryReportPath, BuildSummaryMarkdown(preview, workspaceManifest, primaryModelReport));
        WriteMarkdown(briefPath, BuildProjectBrief(preview, workspaceManifest, primaryModelReport));
        if (!string.IsNullOrWhiteSpace(primaryModelReport.FilePath) || request.IncludeLivePrimaryModelSummary)
        {
            WriteJson(primaryModelReportPath, primaryModelReport);
        }

        return new ProjectInitApplyResponse
        {
            StatusCode = StatusCodes.Ok,
            WorkspaceId = preview.WorkspaceId,
            WorkspaceRootPath = workspaceRoot,
            Manifest = preview.Manifest,
            ManifestStats = preview.ManifestStats,
            WorkspaceManifestPath = workspaceManifestPath,
            ProjectContextPath = projectContextPath,
            ManifestReportPath = manifestReportPath,
            SummaryReportPath = summaryReportPath,
            ProjectBriefPath = briefPath,
            PrimaryModelReportPath = File.Exists(primaryModelReportPath) ? primaryModelReportPath : string.Empty,
            PrimaryModelStatus = primaryModelReport.Status,
            Summary = $"Workspace {preview.WorkspaceId} da duoc bootstrap voi {preview.ManifestStats.TotalFiles} source file(s).",
            OnboardingStatus = BuildInitializedOnboardingStatus(preview.WorkspaceId, workspaceRoot, primaryModelReport.Status, ProjectDeepScanStatuses.NotStarted)
        };
    }

    public ProjectManifestResponse GetManifest(ProjectManifestRequest request)
    {
        request ??= new ProjectManifestRequest();
        var workspaceId = NormalizeWorkspaceId(request.WorkspaceId);
        var workspaceRoot = Path.Combine(_workspacesRoot, workspaceId);
        var manifestPath = Path.Combine(workspaceRoot, "reports", "project-init.manifest.json");
        var summaryPath = Path.Combine(workspaceRoot, "reports", "project-init.summary.md");
        if (!File.Exists(manifestPath))
        {
            return new ProjectManifestResponse
            {
                StatusCode = StatusCodes.ProjectManifestNotFound,
                Exists = false,
                WorkspaceId = workspaceId,
                WorkspaceRootPath = workspaceRoot,
                ManifestPath = manifestPath,
                SummaryReportPath = summaryPath,
                Summary = "Workspace chua co project-init manifest."
            };
        }

        var manifest = ReadJson<ProjectSourceManifest>(manifestPath) ?? new ProjectSourceManifest();
        manifest.Stats ??= BuildStats(manifest);
        return new ProjectManifestResponse
        {
            StatusCode = StatusCodes.Ok,
            Exists = true,
            WorkspaceId = workspaceId,
            WorkspaceRootPath = workspaceRoot,
            ManifestPath = manifestPath,
            SummaryReportPath = summaryPath,
            Manifest = manifest,
            ManifestStats = manifest.Stats,
            Summary = string.IsNullOrWhiteSpace(manifest.Summary) ? $"Workspace {workspaceId} co {manifest.Stats.TotalFiles} source file(s)." : manifest.Summary
        };
    }
    public string ResolveWorkspaceIdForPrimaryModelPath(string? filePath)
    {
        var normalizedPath = NormalizeExistingPath(filePath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || !Directory.Exists(_workspacesRoot))
        {
            return string.Empty;
        }

        foreach (var workspaceDir in Directory.GetDirectories(_workspacesRoot))
        {
            var contextPath = Path.Combine(workspaceDir, "project.context.json");
            if (!File.Exists(contextPath))
            {
                continue;
            }

            var context = ReadJson<ProjectContextState>(contextPath);
            if (context == null)
            {
                continue;
            }

            if (PathsEqual(context.PrimaryRevitFilePath, normalizedPath))
            {
                return context.WorkspaceId ?? Path.GetFileName(workspaceDir);
            }
        }

        return string.Empty;
    }

    public OnboardingStatusDto GetOnboardingStatus(string? workspaceId)
    {
        var normalizedWorkspaceId = NormalizeWorkspaceId(workspaceId);
        var workspaceRoot = Path.Combine(_workspacesRoot, normalizedWorkspaceId);
        var projectContextPath = Path.Combine(workspaceRoot, "project.context.json");
        var manifestPath = Path.Combine(workspaceRoot, "reports", "project-init.manifest.json");
        var deepScanPath = Path.Combine(workspaceRoot, "reports", "project-brain.deep-scan.json");

        var context = ReadJson<ProjectContextState>(projectContextPath);
        var deepScan = ReadJson<ProjectDeepScanReport>(deepScanPath);
        var initCompleted = context != null && File.Exists(manifestPath);
        var deepScanStatus = deepScan?.Status ?? context?.DeepScanStatus ?? ProjectDeepScanStatuses.NotStarted;
        var primaryModelStatus = context?.PrimaryModelStatus ?? ProjectPrimaryModelStatuses.NotRequested;
        var summary = !string.IsNullOrWhiteSpace(deepScan?.Summary)
            ? deepScan!.Summary
            : context?.Summary ?? string.Empty;

        return new OnboardingStatusDto
        {
            WorkspaceId = normalizedWorkspaceId,
            WorkspaceRootPath = workspaceRoot,
            InitStatus = initCompleted ? ProjectOnboardingStatuses.Initialized : ProjectOnboardingStatuses.NotInitialized,
            DeepScanStatus = string.IsNullOrWhiteSpace(deepScanStatus) ? ProjectDeepScanStatuses.NotStarted : deepScanStatus,
            ResumeEligible = initCompleted,
            PrimaryModelStatus = primaryModelStatus,
            Summary = summary
        };
    }

    private OnboardingStatusDto BuildPreviewOnboardingStatus(ProjectInitPreviewResponse response, string workspaceRoot)
    {
        var existing = GetOnboardingStatus(response.WorkspaceId);
        if (existing.ResumeEligible)
        {
            existing.WorkspaceRootPath = workspaceRoot;
            if (string.IsNullOrWhiteSpace(existing.Summary))
            {
                existing.Summary = response.Summary;
            }

            return existing;
        }

        var initStatus = response.RequiresPrimaryRevitSelection
            ? ProjectOnboardingStatuses.RequiresPrimaryModel
            : response.IsValid
                ? ProjectOnboardingStatuses.PreviewReady
                : ProjectOnboardingStatuses.Blocked;
        return new OnboardingStatusDto
        {
            WorkspaceId = response.WorkspaceId,
            WorkspaceRootPath = workspaceRoot,
            InitStatus = initStatus,
            DeepScanStatus = existing.DeepScanStatus,
            ResumeEligible = false,
            PrimaryModelStatus = existing.PrimaryModelStatus,
            Summary = response.Summary
        };
    }

    private static OnboardingStatusDto BuildInitializedOnboardingStatus(string workspaceId, string workspaceRoot, string primaryModelStatus, string deepScanStatus)
    {
        return new OnboardingStatusDto
        {
            WorkspaceId = workspaceId,
            WorkspaceRootPath = workspaceRoot,
            InitStatus = ProjectOnboardingStatuses.Initialized,
            DeepScanStatus = string.IsNullOrWhiteSpace(deepScanStatus) ? ProjectDeepScanStatuses.NotStarted : deepScanStatus,
            ResumeEligible = true,
            PrimaryModelStatus = string.IsNullOrWhiteSpace(primaryModelStatus) ? ProjectPrimaryModelStatuses.NotRequested : primaryModelStatus
        };
    }

    private ProjectSourceManifest DiscoverManifest(string sourceRootPath, string primaryRevitFilePath)
    {
        var files = new List<ProjectSourceFile>();
        foreach (var file in EnumerateSourceFiles(sourceRootPath))
        {
            var info = new FileInfo(file);
            var kind = InferSourceKind(info.Extension);
            var normalizedFilePath = NormalizeExistingPath(file) ?? file;
            var metadata = RevitSourceMetadataDetector.Detect(normalizedFilePath, kind);
            files.Add(new ProjectSourceFile
            {
                SourcePath = normalizedFilePath,
                RelativePath = GetRelativePath(sourceRootPath, normalizedFilePath),
                FileName = info.Name,
                Extension = info.Extension,
                SourceKind = kind,
                SizeBytes = info.Exists ? info.Length : 0,
                LastWriteUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue,
                Fingerprint = BuildFileFingerprint(normalizedFilePath, info),
                IsPrimaryCandidate = string.Equals(kind, ProjectSourceKinds.RevitProject, StringComparison.OrdinalIgnoreCase),
                Summary = BuildSourceSummary(kind, info.Name, info.Exists ? info.Length : 0, metadata),
                RevitVersion = metadata.RevitVersion,
                IsWorkshared = metadata.IsWorkshared,
                WorksharingSummary = metadata.WorksharingSummary
            });
        }

        var resolvedPrimary = ResolvePrimaryRevitPath(files, primaryRevitFilePath);
        foreach (var item in files)
        {
            item.IsSelectedPrimary = PathsEqual(item.SourcePath, resolvedPrimary);
        }

        var manifest = new ProjectSourceManifest
        {
            SourceRootPath = sourceRootPath,
            GeneratedUtc = DateTime.UtcNow,
            PrimaryRevitFilePath = resolvedPrimary,
            Files = files
                .OrderByDescending(x => x.IsSelectedPrimary)
                .ThenBy(x => x.SourceKind, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
        manifest.Stats = BuildStats(manifest);
        manifest.Summary = $"Discovered {manifest.Stats.TotalFiles} file(s): {manifest.Stats.RevitProjectCount} rvt, {manifest.Stats.RevitFamilyCount} rfa, {manifest.Stats.PdfCount} pdf.";
        return manifest;
    }

    private IEnumerable<string> EnumerateSourceFiles(string sourceRootPath)
    {
        var pending = new Stack<string>();
        pending.Push(sourceRootPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> childDirectories = Array.Empty<string>();
            IEnumerable<string> childFiles = Array.Empty<string>();
            try
            {
                childDirectories = Directory.EnumerateDirectories(current);
                childFiles = Directory.EnumerateFiles(current);
            }
            catch
            {
                continue;
            }

            foreach (var directory in childDirectories)
            {
                var name = Path.GetFileName(directory);
                if (ExcludedDirectoryNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                pending.Push(directory);
            }

            foreach (var file in childFiles)
            {
                var extension = Path.GetExtension(file);
                if (string.Equals(extension, ".rvt", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".rfa", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
        }
    }

    private (List<string> validPackIds, List<string> errors, List<string> warnings) ValidateFirmPackIds(IEnumerable<string>? packIds)
    {
        var valid = new List<string>();
        var errors = new List<string>();
        var warnings = new List<string>();
        foreach (var packId in (packIds ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_packs.TryGet(packId, out var pack))
            {
                errors.Add($"Firm pack khong ton tai: {packId}.");
                continue;
            }

            if (!IsAllowedFirmPackType(pack.Manifest.PackType))
            {
                errors.Add($"Firm pack {packId} co pack type khong hop le: {pack.Manifest.PackType}.");
                continue;
            }

            valid.Add(pack.Manifest.PackId);
        }

        if (valid.Count == 0)
        {
            warnings.Add("Firm doctrine pending: chua co firm pack nao duoc chon.");
        }

        return (valid, errors, warnings);
    }

    private static bool IsAllowedFirmPackType(string? packType)
    {
        return string.Equals(packType, "standards-pack", StringComparison.OrdinalIgnoreCase)
            || string.Equals(packType, "playbook-pack", StringComparison.OrdinalIgnoreCase)
            || string.Equals(packType, "skill-pack", StringComparison.OrdinalIgnoreCase);
    }

    private List<string> BuildEffectivePackIds(IEnumerable<string> firmPackIds)
    {
        var defaultWorkspace = _workspaces.GetManifest("default").Workspace;
        var corePackIds = (defaultWorkspace.EnabledPacks ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (corePackIds.Count == 0)
        {
            corePackIds.AddRange(new[]
            {
                "bim765t.standards.core",
                "bim765t.playbooks.core",
                "bim765t.skills.core"
            });
        }

        return corePackIds
            .Concat(firmPackIds ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private WorkspaceManifest BuildWorkspaceManifest(ProjectInitPreviewResponse preview, string displayName)
    {
        var seed = _workspaces.GetManifest("default").Workspace ?? new WorkspaceManifest();
        var enabledPackIds = BuildEffectivePackIds(preview.FirmPackIds);
        var preferredStandards = ResolvePreferredPackIds(preview.FirmPackIds, seed.PreferredStandardsPacks, "standards-pack", "bim765t.standards.core");
        var preferredPlaybooks = ResolvePreferredPackIds(preview.FirmPackIds, seed.PreferredPlaybookPacks, "playbook-pack", "bim765t.playbooks.core");

        return new WorkspaceManifest
        {
            WorkspaceId = preview.WorkspaceId,
            DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? (!string.IsNullOrWhiteSpace(preview.Manifest.SourceRootPath) ? Path.GetFileName(preview.Manifest.SourceRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) : preview.WorkspaceId)
                : displayName.Trim(),
            EnabledPacks = enabledPackIds,
            PreferredStandardsPacks = preferredStandards,
            PreferredPlaybookPacks = preferredPlaybooks,
            AllowedAgents = seed.AllowedAgents?.ToList() ?? new List<string>(),
            AllowedSpecialists = seed.AllowedSpecialists?.ToList() ?? new List<string>(),
            ModelProvider = seed.ModelProvider ?? new WorkspaceModelProviderConfig(),
            RuntimePolicy = seed.RuntimePolicy ?? new WorkspaceRuntimePolicy()
        };
    }
    private List<string> ResolvePreferredPackIds(IEnumerable<string> firmPackIds, IEnumerable<string>? seedPackIds, string packType, string fallbackPackId)
    {
        var chosen = (firmPackIds ?? Array.Empty<string>())
            .Where(x => _packs.TryGet(x, out var entry) && string.Equals(entry.Manifest.PackType, packType, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (chosen.Count > 0)
        {
            return chosen;
        }

        var seed = (seedPackIds ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (seed.Count > 0)
        {
            return seed;
        }

        return string.IsNullOrWhiteSpace(fallbackPackId)
            ? new List<string>()
            : new List<string> { fallbackPackId };
    }

    private ProjectPrimaryModelReport BuildPrimaryModelReport(ProjectSourceManifest manifest, bool includeLivePrimaryModelSummary, ProjectPrimaryModelReport? livePrimaryModelReport)
    {
        if (string.IsNullOrWhiteSpace(manifest.PrimaryRevitFilePath))
        {
            return new ProjectPrimaryModelReport
            {
                FilePath = string.Empty,
                Status = ProjectPrimaryModelStatuses.NotRequested,
                PendingLiveSummary = false,
                Summary = "Khong co primary .rvt nao duoc chon cho project init."
            };
        }

        if (!includeLivePrimaryModelSummary)
        {
            return new ProjectPrimaryModelReport
            {
                FilePath = manifest.PrimaryRevitFilePath,
                Status = ProjectPrimaryModelStatuses.NotRequested,
                PendingLiveSummary = false,
                Summary = "Live primary model summary da duoc tat theo request."
            };
        }

        if (livePrimaryModelReport == null || string.IsNullOrWhiteSpace(livePrimaryModelReport.FilePath))
        {
            return new ProjectPrimaryModelReport
            {
                FilePath = manifest.PrimaryRevitFilePath,
                Status = ProjectPrimaryModelStatuses.PendingLiveSummary,
                PendingLiveSummary = true,
                Summary = "Primary model summary dang pending vi kernel/summary provider chua san sang."
            };
        }

        livePrimaryModelReport.FilePath = string.IsNullOrWhiteSpace(livePrimaryModelReport.FilePath)
            ? manifest.PrimaryRevitFilePath
            : livePrimaryModelReport.FilePath;
        livePrimaryModelReport.Status = string.IsNullOrWhiteSpace(livePrimaryModelReport.Status)
            ? ProjectPrimaryModelStatuses.Captured
            : livePrimaryModelReport.Status;
        return livePrimaryModelReport;
    }

    private ProjectContextState BuildProjectContextState(ProjectInitPreviewResponse preview, string displayName, string manifestPath, string primaryModelStatus)
    {
        var workspaceManifest = BuildWorkspaceManifest(preview, displayName);
        var pendingUnknowns = new List<string>();
        if (preview.FirmPackIds.Count == 0)
        {
            pendingUnknowns.Add("Firm doctrine pending");
        }

        if (preview.ManifestStats.RevitProjectCount == 0)
        {
            pendingUnknowns.Add("Chua co file .rvt primary cho project workspace");
        }

        if (string.Equals(primaryModelStatus, ProjectPrimaryModelStatuses.PendingLiveSummary, StringComparison.OrdinalIgnoreCase))
        {
            pendingUnknowns.Add("Primary model live summary pending");
        }

        return new ProjectContextState
        {
            WorkspaceId = preview.WorkspaceId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? workspaceManifest.DisplayName : displayName.Trim(),
            SourceRootPath = preview.Manifest.SourceRootPath,
            PrimaryRevitFilePath = preview.Manifest.PrimaryRevitFilePath,
            SourceManifestPath = manifestPath,
            FirmPackIds = preview.FirmPackIds.ToList(),
            EnabledPackIds = workspaceManifest.EnabledPacks.ToList(),
            PreferredStandardsPackIds = workspaceManifest.PreferredStandardsPacks.ToList(),
            PreferredPlaybookPackIds = workspaceManifest.PreferredPlaybookPacks.ToList(),
            Summary = preview.Summary,
            PendingUnknowns = pendingUnknowns,
            FirmDoctrinePending = preview.FirmPackIds.Count == 0,
            PrimaryModelStatus = primaryModelStatus,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
    }

    private static string BuildPreviewSummary(ProjectInitPreviewResponse response, string sourceRoot)
    {
        var primaryText = string.IsNullOrWhiteSpace(response.ManifestStats.PrimaryRevitFilePath)
            ? "chua co primary model"
            : $"primary={Path.GetFileName(response.ManifestStats.PrimaryRevitFilePath)}";
        var firmText = response.FirmPackIds.Count == 0
            ? "firm doctrine pending"
            : $"firm packs={response.FirmPackIds.Count}";
        return $"Workspace {response.WorkspaceId} preview tu '{sourceRoot}' - {response.ManifestStats.TotalFiles} file(s), {primaryText}, {firmText}.";
    }

    private static List<ProjectLayerSummary> BuildPreviewLayers(IEnumerable<string> firmPackIds, ProjectSourceManifest manifest)
    {
        return new List<ProjectLayerSummary>
        {
            new ProjectLayerSummary
            {
                LayerKey = "core_safety",
                Title = "Core safety",
                Status = "active",
                Summary = "Core safety > firm doctrine > project overlay > session memory."
            },
            new ProjectLayerSummary
            {
                LayerKey = "firm_doctrine",
                Title = "Firm doctrine",
                Status = firmPackIds.Any() ? "ready" : "pending",
                Summary = firmPackIds.Any()
                    ? $"Firm overlay se consume {firmPackIds.Count()} pack(s)."
                    : "Chua co firm pack; se fallback core packs."
            },
            new ProjectLayerSummary
            {
                LayerKey = "project_overlay",
                Title = "Project overlay",
                Status = "ready",
                Summary = $"Manifest grounding gom {manifest.Stats.TotalFiles} source file(s), primary='{Path.GetFileName(manifest.PrimaryRevitFilePath)}'."
            }
        };
    }

    private static ProjectManifestStats BuildStats(ProjectSourceManifest manifest)
    {
        manifest ??= new ProjectSourceManifest();
        var files = manifest.Files ?? new List<ProjectSourceFile>();
        return new ProjectManifestStats
        {
            TotalFiles = files.Count,
            RevitProjectCount = files.Count(x => string.Equals(x.SourceKind, ProjectSourceKinds.RevitProject, StringComparison.OrdinalIgnoreCase)),
            RevitFamilyCount = files.Count(x => string.Equals(x.SourceKind, ProjectSourceKinds.RevitFamily, StringComparison.OrdinalIgnoreCase)),
            PdfCount = files.Count(x => string.Equals(x.SourceKind, ProjectSourceKinds.Pdf, StringComparison.OrdinalIgnoreCase)),
            PrimaryRevitFilePath = manifest.PrimaryRevitFilePath ?? string.Empty
        };
    }

    private static string ResolvePrimaryRevitPath(IEnumerable<ProjectSourceFile> files, string? requestedPrimaryRevitFilePath)
    {
        var candidates = (files ?? Array.Empty<ProjectSourceFile>())
            .Where(x => string.Equals(x.SourceKind, ProjectSourceKinds.RevitProject, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var requested = NormalizeExistingPath(requestedPrimaryRevitFilePath);
        if (!string.IsNullOrWhiteSpace(requested)
            && candidates.Any(x => PathsEqual(x.SourcePath, requested)))
        {
            return requested ?? string.Empty;
        }

        return candidates.Count == 1 ? candidates[0].SourcePath : string.Empty;
    }

    private static string InferSourceKind(string? extension)
    {
        if (string.Equals(extension, ".rvt", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSourceKinds.RevitProject;
        }

        if (string.Equals(extension, ".rfa", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSourceKinds.RevitFamily;
        }

        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSourceKinds.Pdf;
        }

        return ProjectSourceKinds.Other;
    }

    private static string BuildSourceSummary(string sourceKind, string fileName, long sizeBytes, RevitSourceMetadata metadata)
    {
        var sizeMb = sizeBytes > 0 ? (sizeBytes / 1024d / 1024d).ToString("0.##", CultureInfo.InvariantCulture) : "0";
        var summary = sourceKind switch
        {
            ProjectSourceKinds.RevitProject => $"Revit project '{fileName}' ({sizeMb} MB).",
            ProjectSourceKinds.RevitFamily => $"Revit family '{fileName}' ({sizeMb} MB).",
            ProjectSourceKinds.Pdf => $"PDF reference '{fileName}' ({sizeMb} MB).",
            _ => $"Source '{fileName}' ({sizeMb} MB)."
        };

        var metadataSummary = FormatSourceMetadataSummary(metadata);
        return string.IsNullOrWhiteSpace(metadataSummary)
            ? summary
            : $"{summary.TrimEnd('.')} Detected {metadataSummary}.";
    }

    private static string FormatSourceMetadataSummary(RevitSourceMetadata? metadata)
    {
        if (metadata == null)
        {
            return string.Empty;
        }

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(metadata.RevitVersion))
        {
            details.Add($"Revit {metadata.RevitVersion}");
        }

        if (!string.IsNullOrWhiteSpace(metadata.WorksharingSummary))
        {
            details.Add(metadata.WorksharingSummary);
        }

        return string.Join(", ", details);
    }

    private static string FormatSourceMetadataSummary(ProjectSourceFile? file)
    {
        if (file == null)
        {
            return string.Empty;
        }

        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(file.RevitVersion))
        {
            details.Add($"Revit {file.RevitVersion}");
        }

        if (!string.IsNullOrWhiteSpace(file.WorksharingSummary))
        {
            details.Add(file.WorksharingSummary);
        }

        return string.Join(", ", details);
    }

    private static ProjectSourceFile? ResolvePrimarySourceFile(ProjectSourceManifest manifest)
    {
        return (manifest.Files ?? new List<ProjectSourceFile>())
            .FirstOrDefault(x => x.IsSelectedPrimary)
            ?? (manifest.Files ?? new List<ProjectSourceFile>())
                .FirstOrDefault(x => PathsEqual(x.SourcePath, manifest.PrimaryRevitFilePath));
    }

    private static string BuildSummaryMarkdown(ProjectInitPreviewResponse preview, WorkspaceManifest workspaceManifest, ProjectPrimaryModelReport primaryModelReport)
    {
        var builder = new StringBuilder();
        var primarySource = ResolvePrimarySourceFile(preview.Manifest);
        var primarySourceMetadata = FormatSourceMetadataSummary(primarySource);
        builder.AppendLine("# Project init summary");
        builder.AppendLine();
        builder.AppendLine($"- WorkspaceId: `{preview.WorkspaceId}`");
        builder.AppendLine($"- DisplayName: {workspaceManifest.DisplayName}");
        builder.AppendLine($"- SourceRoot: `{preview.Manifest.SourceRootPath}`");
        builder.AppendLine($"- Files: {preview.ManifestStats.TotalFiles} (rvt={preview.ManifestStats.RevitProjectCount}, rfa={preview.ManifestStats.RevitFamilyCount}, pdf={preview.ManifestStats.PdfCount})");
        builder.AppendLine($"- PrimaryModel: {(string.IsNullOrWhiteSpace(preview.Manifest.PrimaryRevitFilePath) ? "pending selection" : "`" + preview.Manifest.PrimaryRevitFilePath + "`")}");
        if (!string.IsNullOrWhiteSpace(primarySourceMetadata))
        {
            builder.AppendLine($"- PrimaryModelMetadata: {primarySourceMetadata}");
        }
        builder.AppendLine($"- FirmPacks: {(preview.FirmPackIds.Count == 0 ? "pending" : string.Join(", ", preview.FirmPackIds))}");
        builder.AppendLine($"- EnabledPacks: {string.Join(", ", workspaceManifest.EnabledPacks ?? new List<string>())}");
        builder.AppendLine($"- PrimaryModelStatus: {primaryModelReport.Status}");
        builder.AppendLine();
        builder.AppendLine("## Notes");
        builder.AppendLine("- Project init chi bootstrap manifest/context curated; chua deep-scan model hoac parse full PDF.");
        builder.AppendLine("- Files goc (.rvt/.rfa/.pdf) khong bi copy vao workspace; chi luu metadata + refs.");
        return builder.ToString();
    }
    private static string BuildProjectBrief(ProjectInitPreviewResponse preview, WorkspaceManifest workspaceManifest, ProjectPrimaryModelReport primaryModelReport)
    {
        var builder = new StringBuilder();
        var primarySource = ResolvePrimarySourceFile(preview.Manifest);
        var primarySourceMetadata = FormatSourceMetadataSummary(primarySource);
        builder.AppendLine("# Project brief");
        builder.AppendLine();
        builder.AppendLine($"Workspace `{preview.WorkspaceId}` da duoc khoi tao cho source root `{preview.Manifest.SourceRootPath}`.");
        builder.AppendLine();
        builder.AppendLine("## Grounding nhanh");
        builder.AppendLine($"- Model chinh: {(string.IsNullOrWhiteSpace(preview.Manifest.PrimaryRevitFilePath) ? "chua chon" : Path.GetFileName(preview.Manifest.PrimaryRevitFilePath))}");
        if (!string.IsNullOrWhiteSpace(primarySourceMetadata))
        {
            builder.AppendLine($"- Source metadata: {primarySourceMetadata}");
        }
        builder.AppendLine($"- Manifest stats: {preview.ManifestStats.TotalFiles} file(s), {preview.ManifestStats.RevitProjectCount} rvt, {preview.ManifestStats.RevitFamilyCount} rfa, {preview.ManifestStats.PdfCount} pdf");
        builder.AppendLine($"- Primary model status: {primaryModelReport.Status}");
        builder.AppendLine($"- Firm doctrine: {(preview.FirmPackIds.Count == 0 ? "pending" : string.Join(", ", preview.FirmPackIds))}");
        builder.AppendLine($"- Standards/playbooks theo workspace: {string.Join(", ", workspaceManifest.EnabledPacks ?? new List<string>())}");
        builder.AppendLine();
        builder.AppendLine("## Scope hien tai");
        builder.AppendLine("- Bootstrap workspace + manifest + project context curated");
        builder.AppendLine("- Khong deep scan, khong ingest full PDF, khong Ask Project tu do");
        builder.AppendLine("- Worker/project context se doc bundle compact thay vi load raw source files");
        return builder.ToString();
    }

    private static string ResolveWorkspaceId(string? requestedWorkspaceId, string sourceRootPath)
    {
        var basis = string.IsNullOrWhiteSpace(requestedWorkspaceId)
            ? Path.GetFileName(sourceRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : requestedWorkspaceId;
        return NormalizeWorkspaceId(basis);
    }

    internal static string NormalizeWorkspaceId(string? workspaceId)
    {
        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return "default";
        }

        var builder = new StringBuilder();
        var normalized = (workspaceId ?? string.Empty).Trim().ToLowerInvariant();
        foreach (var ch in normalized)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (builder.Length == 0 || builder[builder.Length - 1] != '-')
            {
                builder.Append('-');
            }
        }

        var value = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(value) ? "default" : value;
    }

    public static string? NormalizeExistingPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var trimmed = (path ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : Path.GetFullPath(trimmed);
        }
        catch
        {
            return null;
        }
    }

    internal static bool PathsEqual(string? left, string? right)
    {
        return string.Equals(NormalizeExistingPath(left), NormalizeExistingPath(right), StringComparison.OrdinalIgnoreCase);
    }

    internal static T? ReadJson<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonUtil.DeserializeRequired<T>(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    internal static void WriteJson<T>(string path, T payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        File.WriteAllText(path, JsonUtil.Serialize(payload), Encoding.UTF8);
    }

    internal static void WriteMarkdown(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        File.WriteAllText(path, content ?? string.Empty, Encoding.UTF8);
    }

    internal static string GetRelativePath(string rootPath, string fullPath)
    {
        var normalizedRoot = NormalizeExistingPath(rootPath) ?? rootPath ?? string.Empty;
        var normalizedFull = NormalizeExistingPath(fullPath) ?? fullPath ?? string.Empty;
        if (normalizedRoot.Length == 0 || normalizedFull.Length == 0)
        {
            return normalizedFull;
        }

        try
        {
            return GetRelativePathCompat(normalizedRoot, normalizedFull);
        }
        catch
        {
            return normalizedFull;
        }
    }

    internal static string BuildFileFingerprint(string fullPath, FileInfo info)
    {
        var raw = string.Join("|", new[]
        {
            NormalizeExistingPath(fullPath) ?? fullPath ?? string.Empty,
            info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) : "0",
            info.Exists ? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "0"
        });
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return BytesToHex(bytes);
    }

    private static string GetRelativePathCompat(string normalizedRoot, string normalizedFull)
    {
        var rootWithSeparator = EnsureTrailingDirectorySeparator(normalizedRoot);
        if (normalizedFull.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedFull.Substring(rootWithSeparator.Length);
        }

        if (string.Equals(normalizedRoot, normalizedFull, StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileName(normalizedFull);
        }

        return normalizedFull;
    }

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            || path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string BytesToHex(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}

public sealed class ProjectContextComposer
{
    private readonly ProjectInitService _projectInit;
    private readonly WorkspaceCatalogService _workspaces;
    private readonly StandardsCatalogService _standards;
    private readonly PlaybookOrchestrationService _playbooks;
    private readonly string _workspacesRoot;

    public ProjectContextComposer(ProjectInitService projectInit, WorkspaceCatalogService workspaces, StandardsCatalogService standards, PlaybookOrchestrationService playbooks, string? baseDirectory = null, string? workspaceRootPath = null)
    {
        _projectInit = projectInit ?? throw new ArgumentNullException(nameof(projectInit));
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
        _standards = standards ?? throw new ArgumentNullException(nameof(standards));
        _playbooks = playbooks ?? throw new ArgumentNullException(nameof(playbooks));
        _workspacesRoot = RepoLayoutService.ResolveWorkspacesRoot(workspaceRootPath, baseDirectory);
    }

    public ProjectContextBundleResponse GetContextBundle(ProjectContextBundleRequest request, IEnumerable<ToolManifest>? manifests = null, Func<MemoryFindSimilarRunsRequest, MemoryFindSimilarRunsResponse>? memoryFinder = null)
    {
        request ??= new ProjectContextBundleRequest();
        var workspaceId = ProjectInitService.NormalizeWorkspaceId(request.WorkspaceId);
        var workspaceRoot = Path.Combine(_workspacesRoot, workspaceId);
        var projectContextPath = Path.Combine(workspaceRoot, "project.context.json");
        var manifestPath = Path.Combine(workspaceRoot, "reports", "project-init.manifest.json");
        var briefPath = Path.Combine(workspaceRoot, "memory", "project-brief.md");
        var primaryModelPath = Path.Combine(workspaceRoot, "reports", "project-init.primary-model.json");
        var summaryPath = Path.Combine(workspaceRoot, "reports", "project-init.summary.md");
        var deepScanPath = Path.Combine(workspaceRoot, "reports", "project-brain.deep-scan.json");

        var context = ProjectInitService.ReadJson<ProjectContextState>(projectContextPath);
        var manifest = ProjectInitService.ReadJson<ProjectSourceManifest>(manifestPath);
        if (context == null || manifest == null)
        {
            return new ProjectContextBundleResponse
            {
                StatusCode = StatusCodes.ProjectContextNotInitialized,
                Exists = false,
                WorkspaceId = workspaceId,
                WorkspaceRootPath = workspaceRoot,
                Summary = "Chat van san sang, nhung workspace chua duoc project.init_apply.",
                ProjectBrief = string.Empty,
                PrimaryModelStatus = ProjectPrimaryModelStatuses.NotRequested,
                OnboardingStatus = _projectInit.GetOnboardingStatus(workspaceId)
            };
        }

        manifest.Stats ??= new ProjectManifestStats();
        if (manifest.Stats.TotalFiles == 0 && manifest.Files.Count > 0)
        {
            manifest.Stats = new ProjectManifestStats
            {
                TotalFiles = manifest.Files.Count,
                RevitProjectCount = manifest.Files.Count(x => string.Equals(x.SourceKind, ProjectSourceKinds.RevitProject, StringComparison.OrdinalIgnoreCase)),
                RevitFamilyCount = manifest.Files.Count(x => string.Equals(x.SourceKind, ProjectSourceKinds.RevitFamily, StringComparison.OrdinalIgnoreCase)),
                PdfCount = manifest.Files.Count(x => string.Equals(x.SourceKind, ProjectSourceKinds.Pdf, StringComparison.OrdinalIgnoreCase)),
                PrimaryRevitFilePath = manifest.PrimaryRevitFilePath
            };
        }

        var workspaceManifest = _workspaces.GetManifest(workspaceId).Workspace;
        var standards = _standards.Resolve(new StandardsResolutionRequest
        {
            WorkspaceId = workspaceId,
            PreferredPackIds = context.PreferredStandardsPackIds?.ToList() ?? new List<string>()
        });

        var query = string.IsNullOrWhiteSpace(request.Query) ? "project init" : request.Query;
        var match = _playbooks.Match(manifests ?? Array.Empty<ToolManifest>(), new PlaybookMatchRequest
        {
            WorkspaceId = workspaceId,
            Query = query,
            DocumentContext = "project",
            MaxResults = 3
        });
        var recommendedPlaybookId = match.RecommendedPlaybook != null
            ? match.RecommendedPlaybook.PlaybookId
            : string.Empty;
        var preview = !string.IsNullOrWhiteSpace(recommendedPlaybookId)
            ? _playbooks.Preview(manifests ?? Array.Empty<ToolManifest>(), new PlaybookPreviewRequest
            {
                WorkspaceId = workspaceId,
                PlaybookId = recommendedPlaybookId,
                Query = query,
                DocumentContext = "project"
            })
            : new PlaybookPreviewResponse
            {
                WorkspaceId = workspaceId,
                Standards = standards
            };

        var primaryModel = ProjectInitService.ReadJson<ProjectPrimaryModelReport>(primaryModelPath);
        var deepScan = ProjectInitService.ReadJson<ProjectDeepScanReport>(deepScanPath);
        var brief = File.Exists(briefPath) ? File.ReadAllText(briefPath) : string.Empty;
        var summaryMarkdown = File.Exists(summaryPath) ? File.ReadAllText(summaryPath) : string.Empty;

        var sourceRefs = manifest.Files
            .OrderByDescending(x => x.IsSelectedPrimary)
            .ThenBy(x => x.SourceKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, request.MaxSourceRefs))
            .Select((x, index) => new ProjectContextRef
            {
                RefId = $"source:{index + 1}",
                Title = x.FileName,
                RefKind = x.SourceKind,
                SourcePath = x.SourcePath,
                RelativePath = x.RelativePath,
                Summary = x.Summary
            })
            .ToList();

        var standardsRefs = BuildStandardsRefs(preview, standards, request.MaxStandardsRefs);
        var pendingUnknowns = (context.PendingUnknowns ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (standardsRefs.Count == 0)
        {
            pendingUnknowns.Add("Chua resolve duoc top standards refs cho workspace hien tai.");
        }

        var deepScanRefs = (deepScan?.EvidenceRefs ?? new List<ProjectContextRef>())
            .Take(Math.Max(1, request.MaxSourceRefs / 2))
            .ToList();
        if (deepScan == null || string.IsNullOrWhiteSpace(deepScan.Status) || string.Equals(deepScan.Status, ProjectDeepScanStatuses.NotStarted, StringComparison.OrdinalIgnoreCase))
        {
            pendingUnknowns.Add("Project Brain deep scan pending");
        }
        else
        {
            sourceRefs.AddRange(deepScanRefs);
        }

        if (memoryFinder != null && !string.IsNullOrWhiteSpace(request.Query))
        {
            var similarRuns = memoryFinder(new MemoryFindSimilarRunsRequest
            {
                Query = request.Query,
                DocumentKey = string.Empty,
                TaskKind = "project_init",
                MaxResults = 3
            });
            foreach (var run in similarRuns.Runs.Take(2))
            {
                sourceRefs.Add(new ProjectContextRef
                {
                    RefId = $"similar-run:{run.RunId}",
                    Title = run.TaskName,
                    RefKind = "similar_run",
                    SourcePath = run.RunId,
                    RelativePath = string.Empty,
                    Summary = $"{run.TaskKind}:{run.TaskName} - {run.Status} ({run.Summary})"
                });
            }
        }

        sourceRefs = sourceRefs
            .GroupBy(x => string.IsNullOrWhiteSpace(x.RefId) ? (x.SourcePath + "|" + x.Title) : x.RefId, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .Take(Math.Max(1, request.MaxSourceRefs))
            .ToList();

        var bundleSummary = BuildBundleSummary(context, manifest, primaryModel, deepScan, summaryMarkdown, preview, workspaceManifest);
        return new ProjectContextBundleResponse
        {
            StatusCode = StatusCodes.Ok,
            Exists = true,
            WorkspaceId = workspaceId,
            WorkspaceRootPath = workspaceRoot,
            Summary = bundleSummary,
            ProjectBrief = brief,
            ManifestStats = manifest.Stats,
            PrimaryModelStatus = primaryModel?.Status ?? context.PrimaryModelStatus ?? ProjectPrimaryModelStatuses.NotRequested,
            LayerSummaries = BuildLayerSummaries(context, primaryModel, deepScan),
            TopStandardsRefs = standardsRefs,
            SourceRefs = sourceRefs,
            PendingUnknowns = pendingUnknowns,
            RecommendedPlaybookId = match.RecommendedPlaybook?.PlaybookId ?? string.Empty,
            StandardsSummary = preview.Standards?.Summary ?? standards.Summary,
            DeepScanStatus = deepScan?.Status ?? context.DeepScanStatus ?? ProjectDeepScanStatuses.NotStarted,
            DeepScanSummary = deepScan?.Summary ?? context.DeepScanSummary ?? string.Empty,
            DeepScanFindingCount = deepScan?.Stats?.FindingCount ?? deepScan?.Findings?.Count ?? 0,
            DeepScanReportPath = deepScanPath,
            DeepScanRefs = deepScanRefs,
            OnboardingStatus = _projectInit.GetOnboardingStatus(workspaceId)
        };
    }

    public string ResolveWorkspaceIdForPrimaryModelPath(string? filePath)
    {
        return _projectInit.ResolveWorkspaceIdForPrimaryModelPath(filePath);
    }

    private static List<ProjectContextRef> BuildStandardsRefs(PlaybookPreviewResponse preview, StandardsResolution standards, int maxStandardsRefs)
    {
        var results = new List<ProjectContextRef>();
        foreach (var standardsRef in (preview.StandardsRefs ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var matchedValue = (preview.Standards?.Values ?? new List<StandardsResolvedValue>())
                .FirstOrDefault(x => string.Equals(x.RequestedKey, standardsRef, StringComparison.OrdinalIgnoreCase));
            results.Add(new ProjectContextRef
            {
                RefId = standardsRef,
                Title = standardsRef,
                RefKind = "standard_ref",
                SourcePath = matchedValue?.SourceFile ?? string.Empty,
                RelativePath = matchedValue?.SourceFile ?? string.Empty,
                Summary = matchedValue != null && matchedValue.Matched
                    ? $"{matchedValue.SourcePackId}:{matchedValue.SourceFile} => {matchedValue.Value}"
                    : "Standard ref from matched playbook preview.",
                SourcePackId = matchedValue?.SourcePackId ?? string.Empty
            });
        }

        if (results.Count == 0)
        {
            results.AddRange((standards.Files ?? new List<StandardsResolvedFile>())
                .Take(Math.Max(1, maxStandardsRefs))
                .Select((file, index) => new ProjectContextRef
                {
                    RefId = $"standards-file:{index + 1}",
                    Title = file.FileName,
                    RefKind = "standards_file",
                    SourcePath = file.RelativePath,
                    RelativePath = file.RelativePath,
                    Summary = $"Standards file from {file.SourcePackId}",
                    SourcePackId = file.SourcePackId
                }));
        }

        return results
            .Take(Math.Max(1, maxStandardsRefs))
            .ToList();
    }

    private static string BuildBundleSummary(ProjectContextState context, ProjectSourceManifest manifest, ProjectPrimaryModelReport? primaryModel, ProjectDeepScanReport? deepScan, string summaryMarkdown, PlaybookPreviewResponse preview, WorkspaceManifest workspace)
    {
        if (deepScan != null && !string.IsNullOrWhiteSpace(deepScan.Summary))
        {
            return $"{context.DisplayName}: {deepScan.Summary}";
        }

        if (!string.IsNullOrWhiteSpace(summaryMarkdown))
        {
            var firstBullet = summaryMarkdown
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(x => x.StartsWith("- ", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(firstBullet))
            {
                return $"{context.DisplayName}: {firstBullet.TrimStart('-', ' ')}";
            }
        }

        var primaryText = string.IsNullOrWhiteSpace(manifest.PrimaryRevitFilePath)
            ? "chua chon primary model"
            : Path.GetFileName(manifest.PrimaryRevitFilePath);
        var primaryStatus = primaryModel?.Status ?? context.PrimaryModelStatus ?? ProjectPrimaryModelStatuses.NotRequested;
        return $"Workspace {context.WorkspaceId}: {manifest.Stats.TotalFiles} source file(s), primary={primaryText}, primaryStatus={primaryStatus}, playbook={preview.PlaybookId}, standardsPacks={string.Join(",", workspace.PreferredStandardsPacks ?? new List<string>())}.";
    }

    private static List<ProjectLayerSummary> BuildLayerSummaries(ProjectContextState context, ProjectPrimaryModelReport? primaryModel, ProjectDeepScanReport? deepScan = null)
    {
        return new List<ProjectLayerSummary>
        {
            new ProjectLayerSummary
            {
                LayerKey = "core_safety",
                Title = "Core safety",
                Status = "active",
                Summary = "Core safety luon uu tien cao hon firm/project/session overlays."
            },
            new ProjectLayerSummary
            {
                LayerKey = "firm_doctrine",
                Title = "Firm doctrine",
                Status = context.FirmDoctrinePending ? "pending" : "ready",
                Summary = context.FirmDoctrinePending
                    ? "Firm doctrine pending; workspace dang fallback core packs."
                    : $"Firm packs: {string.Join(", ", context.FirmPackIds ?? new List<string>())}"
            },
            new ProjectLayerSummary
            {
                LayerKey = "project_overlay",
                Title = "Project overlay",
                Status = "ready",
                Summary = $"Project overlay anchored at {context.SourceRootPath}; enabled packs={string.Join(", ", context.EnabledPackIds ?? new List<string>())}."
            },
            new ProjectLayerSummary
            {
                LayerKey = "live_primary_model",
                Title = "Primary model summary",
                Status = primaryModel?.Status ?? context.PrimaryModelStatus,
                Summary = primaryModel?.Summary ?? "Primary model report chua co live summary."
            },
            new ProjectLayerSummary
            {
                LayerKey = "project_brain_deep_scan",
                Title = "Project Brain deep scan",
                Status = deepScan?.Status ?? context.DeepScanStatus ?? ProjectDeepScanStatuses.NotStarted,
                Summary = deepScan?.Summary ?? context.DeepScanSummary ?? "Deep scan chua duoc thuc hien."
            }
        };
    }
}
