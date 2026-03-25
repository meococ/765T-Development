using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Copilot.Core;

public sealed class CommandAtlasService
{
    // The MVP atlas needs to recognize both pack-level command IDs and tool-level IDs because
    // quick-plan can resolve either source depending on whether the entry comes from command packs
    // or the live tool manifest surface.
    private static readonly HashSet<string> MvpCuratedCommandIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ToolNames.ReviewModelHealth,
        ToolNames.ReviewSmartQc,
        ToolNames.ReviewSheetSummary,
        ToolNames.AuditNamingConvention,
        ToolNames.ViewCreate3dSafe,
        ToolNames.ViewDuplicateSafe,
        ToolNames.SheetCreateSafe,
        ToolNames.SheetPlaceViewsSafe,
        ToolNames.SheetRenumberSafe,
        ToolNames.DataExportSchedule,
        "revit.manage.smart_qc",
        "revit.view.create_3d",
        "revit.view.duplicate",
        "revit.sheet.create",
        "revit.sheet.place_views",
        "revit.sheet.renumber"
    };

    private readonly PackCatalogService _packs;
    private readonly WorkspaceCatalogService _workspaces;
    private readonly CuratedScriptRegistryService _curatedScripts;

    public CommandAtlasService()
        : this(new PackCatalogService(), new WorkspaceCatalogService(), null)
    {
    }

    public CommandAtlasService(PackCatalogService packs, WorkspaceCatalogService workspaces, CuratedScriptRegistryService? curatedScripts = null)
    {
        _packs = packs ?? throw new ArgumentNullException(nameof(packs));
        _workspaces = workspaces ?? throw new ArgumentNullException(nameof(workspaces));
        _curatedScripts = curatedScripts ?? new CuratedScriptRegistryService();
    }

    public IReadOnlyList<CommandAtlasEntry> BuildAtlas(IEnumerable<ToolManifest> manifests, string workspaceId)
    {
        var workspace = _workspaces.GetManifest(workspaceId).Workspace;
        var entries = new List<CommandAtlasEntry>();

        entries.AddRange(LoadPackEntries(workspace));
        entries.AddRange(LoadCuratedRegistryEntries(workspace));
        entries.AddRange((manifests ?? Array.Empty<ToolManifest>()).Select(ToToolEntry));

        return entries
            .Where(IsMvpCuratedEntry)
            .Where(x => !string.IsNullOrWhiteSpace(x.CommandId))
            .GroupBy(x => x.CommandId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => CoverageStatusScore(x.CoverageStatus)).ThenByDescending(x => x.CanAutoExecute ? 1 : 0).First())
            .OrderBy(x => x.CommandFamily, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsMvpCuratedCommand(string? commandId)
    {
        var source = commandId ?? string.Empty;
        var normalized = source.Trim();
        return normalized.Length > 0 && MvpCuratedCommandIds.Contains(normalized);
    }

    public CommandAtlasSearchResponse Search(IEnumerable<ToolManifest> manifests, CommandAtlasSearchRequest request)
    {
        request ??= new CommandAtlasSearchRequest();
        var workspaceId = string.IsNullOrWhiteSpace(request.WorkspaceId) ? "default" : request.WorkspaceId.Trim();
        var atlas = BuildAtlas(manifests, workspaceId);
        var tokens = Tokenize(request.Query);

        var matches = atlas
            .Select(x => new CommandAtlasMatch
            {
                Entry = x,
                Score = Score(x, tokens, request),
                Reason = BuildReason(x, tokens, request)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, request.MaxResults))
            .ToList();

        return new CommandAtlasSearchResponse
        {
            Query = request.Query ?? string.Empty,
            WorkspaceId = workspaceId,
            Matches = matches
        };
    }

    public CommandAtlasEntry Describe(IEnumerable<ToolManifest> manifests, CommandDescribeRequest request)
    {
        request ??= new CommandDescribeRequest();
        var atlas = BuildAtlas(manifests, request.WorkspaceId);
        if (!string.IsNullOrWhiteSpace(request.CommandId))
        {
            if (!IsMvpCuratedCommand(request.CommandId))
            {
                return new CommandAtlasEntry();
            }

            var exact = atlas.FirstOrDefault(x => string.Equals(x.CommandId, request.CommandId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }
        }

        return Search(manifests, new CommandAtlasSearchRequest
        {
            WorkspaceId = request.WorkspaceId,
            Query = request.Query,
            MaxResults = 1
        }).Matches.FirstOrDefault()?.Entry ?? new CommandAtlasEntry();
    }

    public CoverageReportResponse BuildCoverageReport(IEnumerable<ToolManifest> manifests, CoverageReportRequest request)
    {
        request ??= new CoverageReportRequest();
        var atlas = BuildAtlas(manifests, request.WorkspaceId);
        var filtered = string.IsNullOrWhiteSpace(request.CoverageTier)
            ? atlas
            : atlas.Where(x => string.Equals(x.CoverageTier, request.CoverageTier, StringComparison.OrdinalIgnoreCase)).ToList();

        return new CoverageReportResponse
        {
            WorkspaceId = string.IsNullOrWhiteSpace(request.WorkspaceId) ? "default" : request.WorkspaceId,
            TotalCommands = filtered.Count,
            MappedCommands = filtered.Count(x => CoverageStatusScore(x.CoverageStatus) >= CoverageStatusScore(CommandCoverageStatuses.Mapped)),
            ExecutableCommands = filtered.Count(x => CoverageStatusScore(x.CoverageStatus) >= CoverageStatusScore(CommandCoverageStatuses.Executable)),
            PreviewableCommands = filtered.Count(x => CoverageStatusScore(x.CoverageStatus) >= CoverageStatusScore(CommandCoverageStatuses.Previewable)),
            VerifiedCommands = filtered.Count(x => CoverageStatusScore(x.CoverageStatus) >= CoverageStatusScore(CommandCoverageStatuses.Verified)),
            Families = filtered
                .GroupBy(x => x.CommandFamily, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new CoverageMatrixRow
                {
                    CommandFamily = g.Key,
                    CoverageTier = g.Select(x => x.CoverageTier).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? CommandCoverageTiers.Baseline,
                    TotalCommands = g.Count(),
                    MappedCommands = g.Count(x => CoverageStatusScore(x.CoverageStatus) >= CoverageStatusScore(CommandCoverageStatuses.Mapped)),
                    ExecutableCommands = g.Count(x => CoverageStatusScore(x.CoverageStatus) >= CoverageStatusScore(CommandCoverageStatuses.Executable)),
                    PreviewableCommands = g.Count(x => CoverageStatusScore(x.CoverageStatus) >= CoverageStatusScore(CommandCoverageStatuses.Previewable)),
                    VerifiedCommands = g.Count(x => CoverageStatusScore(x.CoverageStatus) >= CoverageStatusScore(CommandCoverageStatuses.Verified))
                })
                .ToList(),
            UncoveredBaselineEntries = filtered
                .Where(x => string.Equals(x.CoverageTier, CommandCoverageTiers.Baseline, StringComparison.OrdinalIgnoreCase)
                    && CoverageStatusScore(x.CoverageStatus) < CoverageStatusScore(CommandCoverageStatuses.Executable))
                .Take(25)
                .ToList()
        };
    }

    public QuickActionResponse PlanQuickAction(IEnumerable<ToolManifest> manifests, QuickActionRequest request)
    {
        request ??= new QuickActionRequest();
        var search = Search(manifests, new CommandAtlasSearchRequest
        {
            WorkspaceId = request.WorkspaceId,
            Query = request.Query,
            Discipline = request.Discipline,
            DocumentContext = request.DocumentContext,
            MaxResults = 3
        });
        var matched = search.Matches.FirstOrDefault()?.Entry ?? new CommandAtlasEntry();
        if (string.IsNullOrWhiteSpace(matched.CommandId))
        {
            var fallback = BuildFallbackProposal(manifests, BuildFallbackRequest(request, "atlas_miss"));
            return new QuickActionResponse
            {
                Query = request.Query ?? string.Empty,
                WorkspaceId = request.WorkspaceId ?? "default",
                RequiresClarification = true,
                Summary = fallback.Summary,
                Confidence = 0,
                FallbackProposal = fallback,
                StrategySummary = BuildQuickStrategySummary(matched, string.Empty, fallback)
            };
        }

        var toolName = ResolvePlannedToolName(matched);
        var payloadJson = matched.DefaultPayloadJson ?? string.Empty;
        var missingContext = new List<string>();
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            payloadJson = ResolvePayloadJson(matched, request, missingContext);
        }

        var disposition = ResolveExecutionDisposition(matched, toolName, missingContext);
        var fallbackProposal = (string.Equals(disposition, "mapped_only", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(disposition, "blocked", StringComparison.OrdinalIgnoreCase))
            ? BuildFallbackProposal(manifests, BuildFallbackRequest(request, disposition, matched, toolName))
            : new FallbackArtifactProposal();
        return new QuickActionResponse
        {
            Query = request.Query ?? string.Empty,
            WorkspaceId = string.IsNullOrWhiteSpace(request.WorkspaceId) ? "default" : request.WorkspaceId,
            MatchedEntry = matched,
            PlannedToolName = toolName,
            ResolvedPayloadJson = payloadJson ?? string.Empty,
            ExecutionDisposition = disposition,
            RequiresClarification = missingContext.Count > 0
                || string.Equals(disposition, "clarify", StringComparison.OrdinalIgnoreCase)
                || string.Equals(disposition, "blocked", StringComparison.OrdinalIgnoreCase),
            MissingContext = missingContext,
            Summary = HasFallbackProposal(fallbackProposal) && missingContext.Count == 0
                ? $"{BuildQuickSummary(matched, toolName, disposition, missingContext)} {fallbackProposal.PreviewSummary}".Trim()
                : BuildQuickSummary(matched, toolName, disposition, missingContext),
            Confidence = search.Matches.FirstOrDefault()?.Score > 0
                ? Math.Min(0.98d, 0.45d + search.Matches.First().Score / 30d)
                : 0.5d,
            FallbackProposal = fallbackProposal,
            StrategySummary = BuildQuickStrategySummary(matched, disposition, fallbackProposal)
        };
    }

    public FallbackArtifactProposal BuildFallbackProposal(IEnumerable<ToolManifest> manifests, FallbackArtifactRequest request)
    {
        request ??= new FallbackArtifactRequest();
        var workspaceId = string.IsNullOrWhiteSpace(request.WorkspaceId) ? "default" : request.WorkspaceId.Trim();
        var query = request.Query ?? string.Empty;
        var kinds = (request.RequestedKinds ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (kinds.Count == 0)
        {
            kinds = ResolveFallbackKindsForQuery(query);
        }

        var candidateTools = request.CandidateToolNames?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            ?? new List<string>();
        if (candidateTools.Count == 0)
        {
            candidateTools = SuggestCandidateToolNames(manifests, query);
        }

        var candidatePlaybooks = request.CandidatePlaybookIds?.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            ?? new List<string>();
        if (candidatePlaybooks.Count == 0)
        {
            candidatePlaybooks = SuggestCandidatePlaybooks(manifests, candidateTools, query);
        }

        var artifactPaths = BuildProposedArtifactPaths(workspaceId, query, kinds);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "atlas_miss" : request.Reason.Trim();
        var inputSummary = string.IsNullOrWhiteSpace(request.InputSummary)
            ? BuildFallbackInputSummary(request)
            : request.InputSummary.Trim();
        var summary = string.Equals(reason, "mapped_only", StringComparison.OrdinalIgnoreCase)
            ? "Command da duoc map nhung chua co execution lane san sang. Worker de xuat artifact fallback thay vi sinh code tu do."
            : "Chua co quick command phu hop trong curated atlas. Worker de xuat artifact fallback an toan de giam token va giu lane deterministic.";

        return new FallbackArtifactProposal
        {
            ProposalId = Guid.NewGuid().ToString("N"),
            WorkspaceId = workspaceId,
            StatusCode = StatusCodes.CommandExecutionBlocked,
            Reason = reason,
            Summary = summary,
            PreviewSummary = $"De xuat {string.Join(", ", kinds)} truoc; user review xong moi promote thanh reusable skill/playbook.",
            VerificationRecipe = BuildVerificationRecipe(kinds),
            ApprovalRequirement = ApprovalRequirement.ConfirmToken,
            ArtifactKinds = kinds,
            ArtifactPaths = artifactPaths,
            CandidateBuiltInToolName = candidateTools.FirstOrDefault() ?? string.Empty,
            CandidatePlaybookId = candidatePlaybooks.FirstOrDefault() ?? string.Empty,
            RequiresHumanReview = true,
            CanSaveToCache = true,
            InputsUsed = BuildFallbackInputs(query, inputSummary),
            CommercialTier = string.IsNullOrWhiteSpace(request.CommercialTier) ? CommercialTiers.PersonalPro : request.CommercialTier,
            CacheValueClass = CacheValueClasses.ArtifactReuse
        };
    }

    private IEnumerable<CommandAtlasEntry> LoadPackEntries(WorkspaceManifest workspace)
    {
        var enabledIds = (workspace.EnabledPacks ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var selectedPacks = enabledIds.Count > 0
            ? _packs.GetByIds(enabledIds)
            : _packs.GetAll()
                .Where(x => string.Equals(x.Manifest.PackType, "command-pack", StringComparison.OrdinalIgnoreCase))
                .ToList();

        foreach (var pack in selectedPacks)
        {
            if (string.Equals(pack.Manifest.PackType, "command-pack", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var entry in LoadCommandEntries(pack))
                {
                    yield return entry;
                }
            }
            else if (string.Equals(pack.Manifest.PackType, "script-pack", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var entry in LoadScriptEntries(pack))
                {
                    yield return entry;
                }
            }
        }
    }

    private IEnumerable<CommandAtlasEntry> LoadCuratedRegistryEntries(WorkspaceManifest workspace)
    {
        if (!HasEnabledPackType(workspace, "script-pack"))
        {
            yield break;
        }

        foreach (var script in _curatedScripts.LoadAll())
        {
            yield return ToScriptEntry(script);
        }
    }

    private bool HasEnabledPackType(WorkspaceManifest workspace, string packType)
    {
        var enabledIds = (workspace?.EnabledPacks ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (enabledIds.Count == 0)
        {
            return false;
        }

        return _packs.GetByIds(enabledIds)
            .Any(x => string.Equals(x.Manifest.PackType, packType, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<CommandAtlasEntry> LoadCommandEntries(PackCatalogEntry pack)
    {
        foreach (var export in (pack.Manifest.Exports ?? new List<PackExport>())
                     .Where(x => string.Equals(x.ExportKind, "command", StringComparison.OrdinalIgnoreCase)))
        {
            var fullPath = Path.Combine(pack.RootPath, export.RelativePath ?? string.Empty);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            CommandAtlasPackFile? file = null;
            try
            {
                file = JsonUtil.DeserializeRequired<CommandAtlasPackFile>(File.ReadAllText(fullPath));
            }
            catch
            {
                continue;
            }

            foreach (var entry in file.Entries ?? new List<CommandAtlasEntry>())
            {
                if (string.IsNullOrWhiteSpace(entry.SourceRef))
                {
                    entry.SourceRef = pack.Manifest.PackId;
                }

                if (string.IsNullOrWhiteSpace(entry.CoverageTier))
                {
                    entry.CoverageTier = CommandCoverageTiers.Baseline;
                }

                yield return entry;
            }
        }
    }

    private static IEnumerable<CommandAtlasEntry> LoadScriptEntries(PackCatalogEntry pack)
    {
        foreach (var export in (pack.Manifest.Exports ?? new List<PackExport>())
                     .Where(x => string.Equals(x.ExportKind, "script_source", StringComparison.OrdinalIgnoreCase)))
        {
            var fullPath = Path.Combine(pack.RootPath, export.RelativePath ?? string.Empty);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            ScriptSourcePackFile? file = null;
            try
            {
                file = JsonUtil.DeserializeRequired<ScriptSourcePackFile>(File.ReadAllText(fullPath));
            }
            catch
            {
                continue;
            }

            foreach (var script in file.Scripts ?? new List<ScriptSourceManifest>())
            {
                yield return ToScriptEntry(script);
            }
        }
    }

    private static CommandAtlasEntry ToScriptEntry(ScriptSourceManifest script)
    {
        return new CommandAtlasEntry
        {
            CommandId = script.ScriptId,
            DisplayName = string.IsNullOrWhiteSpace(script.DisplayName) ? script.ScriptId : script.DisplayName,
            Description = script.Description ?? string.Empty,
            Aliases = BuildAliases(script.ScriptId, script.DisplayName ?? string.Empty, script.Tags != null ? script.Tags : Array.Empty<string>()),
            CommandFamily = BuildScriptCommandFamily(script),
            ExecutionMode = CommandExecutionModes.Script,
            SourceKind = string.IsNullOrWhiteSpace(script.SourceKind) ? CommandSourceKinds.Internal : script.SourceKind,
            SourceRef = string.IsNullOrWhiteSpace(script.ScriptId) ? script.SourceRef : script.ScriptId,
            CapabilityDomain = string.IsNullOrWhiteSpace(script.CapabilityDomain) ? CapabilityDomains.General : script.CapabilityDomain,
            Discipline = script.SupportedDisciplines?.FirstOrDefault() ?? CapabilityDisciplines.Common,
            SafetyClass = string.IsNullOrWhiteSpace(script.SafetyClass) ? CommandSafetyClasses.PreviewedMutation : script.SafetyClass,
            VerificationMode = string.IsNullOrWhiteSpace(script.VerificationMode) ? ToolVerificationModes.ReportOnly : script.VerificationMode,
            RequiredContext = new CommandContextRequirements
            {
                RequiresDocument = true
            },
            CanPreview = true,
            NeedsApproval = script.ApprovalRequirement != ApprovalRequirement.None,
            CanAutoExecute = script.ApprovalRequirement == ApprovalRequirement.None
                && string.Equals(script.SafetyClass, CommandSafetyClasses.ReadOnly, StringComparison.OrdinalIgnoreCase),
            CoverageTier = CommandCoverageTiers.Baseline,
            CoverageStatus = script.Approved ? CommandCoverageStatuses.Previewable : CommandCoverageStatuses.Mapped,
            Tags = script.Tags?.ToList() ?? new List<string>(),
            DefaultPayloadJson = JsonUtil.Serialize(new ScriptRunRequest
            {
                ScriptId = script.ScriptId,
                TimeoutMs = 30000
            }),
            RecommendedPlaybooks = new List<string>(),
            PrimaryPersona = string.IsNullOrWhiteSpace(script.PrimaryPersona) ? ToolPrimaryPersonas.ProductionBimer : script.PrimaryPersona,
            UserValueClass = string.IsNullOrWhiteSpace(script.UserValueClass) ? ToolUserValueClasses.SmartValue : script.UserValueClass,
            RepeatabilityClass = string.IsNullOrWhiteSpace(script.RepeatabilityClass) ? ToolRepeatabilityClasses.Teachable : script.RepeatabilityClass,
            AutomationStage = string.IsNullOrWhiteSpace(script.AutomationStage) ? ToolAutomationStages.ArtifactFallback : script.AutomationStage,
            CanTeachBack = !string.Equals(script.RepeatabilityClass, ToolRepeatabilityClasses.OneOff, StringComparison.OrdinalIgnoreCase),
            FallbackArtifactKinds = script.FallbackArtifactKinds?.ToList() ?? new List<string>(),
            CommercialTier = string.IsNullOrWhiteSpace(script.CommercialTier) ? CommercialTiers.PersonalPro : script.CommercialTier,
            CacheValueClass = string.IsNullOrWhiteSpace(script.CacheValueClass) ? CacheValueClasses.ArtifactReuse : script.CacheValueClass
        };
    }

    private static CommandAtlasEntry ToToolEntry(ToolManifest manifest)
    {
        var coverageStatus =
            string.Equals(manifest.VerificationMode, ToolVerificationModes.None, StringComparison.OrdinalIgnoreCase)
            || string.Equals(manifest.VerificationMode, ToolVerificationModes.ReportOnly, StringComparison.OrdinalIgnoreCase)
                ? manifest.CanPreview ? CommandCoverageStatuses.Previewable : CommandCoverageStatuses.Executable
                : CommandCoverageStatuses.Verified;

        if (!manifest.Enabled)
        {
            coverageStatus = CommandCoverageStatuses.Mapped;
        }

        return new CommandAtlasEntry
        {
            CommandId = manifest.ToolName,
            DisplayName = HumanizeToolName(manifest.ToolName),
            Description = manifest.Description ?? string.Empty,
            Aliases = BuildAliases(manifest.ToolName, manifest.Description ?? string.Empty, manifest.RiskTags != null ? manifest.RiskTags : Array.Empty<string>()),
            CommandFamily = string.IsNullOrWhiteSpace(manifest.CommandFamily) ? manifest.DomainGroup : manifest.CommandFamily,
            ExecutionMode = string.IsNullOrWhiteSpace(manifest.ExecutionMode) ? CommandExecutionModes.Tool : manifest.ExecutionMode,
            NativeCommandId = manifest.NativeCommandId ?? string.Empty,
            SourceKind = string.IsNullOrWhiteSpace(manifest.SourceKind) ? CommandSourceKinds.Repo : manifest.SourceKind,
            SourceRef = string.IsNullOrWhiteSpace(manifest.SourceRef) ? manifest.ToolName : manifest.SourceRef,
            CapabilityDomain = string.IsNullOrWhiteSpace(manifest.CapabilityDomain) ? CapabilityDomains.General : manifest.CapabilityDomain,
            Discipline = manifest.SupportedDisciplines?.FirstOrDefault() ?? CapabilityDisciplines.Common,
            SafetyClass = string.IsNullOrWhiteSpace(manifest.SafetyClass) ? CommandSafetyClasses.ReadOnly : manifest.SafetyClass,
            VerificationMode = string.IsNullOrWhiteSpace(manifest.VerificationMode) ? ToolVerificationModes.ReportOnly : manifest.VerificationMode,
            RequiredContext = ToContextRequirements(manifest.RequiredContext),
            CanPreview = manifest.CanPreview,
            NeedsApproval = manifest.ApprovalRequirement != ApprovalRequirement.None,
            CanAutoExecute = manifest.CanAutoExecute,
            FallbackEntryIds = manifest.FallbackEntryIds?.ToList() ?? new List<string>(),
            CoverageTier = string.IsNullOrWhiteSpace(manifest.CoverageTier) ? CommandCoverageTiers.Baseline : manifest.CoverageTier,
            CoverageStatus = coverageStatus,
            Tags = manifest.IssueKinds?.ToList() ?? new List<string>(),
            PackId = manifest.PackId ?? string.Empty,
            RecommendedPlaybooks = manifest.RecommendedPlaybooks?.ToList() ?? new List<string>(),
            PrimaryPersona = string.IsNullOrWhiteSpace(manifest.PrimaryPersona) ? ToolPrimaryPersonas.ProductionBimer : manifest.PrimaryPersona,
            UserValueClass = string.IsNullOrWhiteSpace(manifest.UserValueClass) ? ToolUserValueClasses.DailyRoi : manifest.UserValueClass,
            RepeatabilityClass = string.IsNullOrWhiteSpace(manifest.RepeatabilityClass) ? ToolRepeatabilityClasses.Repeatable : manifest.RepeatabilityClass,
            AutomationStage = string.IsNullOrWhiteSpace(manifest.AutomationStage) ? ToolAutomationStages.CoreSkill : manifest.AutomationStage,
            CanTeachBack = manifest.CanTeachBack,
            FallbackArtifactKinds = manifest.FallbackArtifactKinds?.ToList() ?? new List<string>(),
            CommercialTier = string.IsNullOrWhiteSpace(manifest.CommercialTier) ? CommercialTiers.Free : manifest.CommercialTier,
            CacheValueClass = string.IsNullOrWhiteSpace(manifest.CacheValueClass) ? CacheValueClasses.IntentToolchain : manifest.CacheValueClass
        };
    }

    private static CommandContextRequirements ToContextRequirements(IEnumerable<string> requiredContext)
    {
        var values = (requiredContext ?? Array.Empty<string>()).ToList();
        return new CommandContextRequirements
        {
            RequiresDocument = values.Count == 0 || values.Contains("document", StringComparer.OrdinalIgnoreCase) || values.Contains("family_document", StringComparer.OrdinalIgnoreCase),
            RequiresActiveView = values.Contains("view", StringComparer.OrdinalIgnoreCase),
            RequiresCurrentLevel = values.Contains("level", StringComparer.OrdinalIgnoreCase),
            RequiresCurrentSheet = values.Contains("sheet", StringComparer.OrdinalIgnoreCase),
            RequiresSelection = values.Contains("selection", StringComparer.OrdinalIgnoreCase),
            RequiredDocumentKinds = values.Where(x => x.EndsWith("_document", StringComparison.OrdinalIgnoreCase)).ToList()
        };
    }

    private static int Score(CommandAtlasEntry entry, IReadOnlyCollection<string> tokens, CommandAtlasSearchRequest request)
    {
        entry ??= new CommandAtlasEntry();
        entry.Aliases ??= new List<string>();
        var score = 0;
        var textScore = 0;
        if (tokens.Count == 0)
        {
            score += 1;
        }

        foreach (var token in tokens)
        {
            if (entry.CommandId.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                textScore += 10;
            }

            if (entry.DisplayName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                textScore += 7;
            }

            if (entry.Description.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                textScore += 5;
            }

            if (entry.Aliases.Any(x => x.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                textScore += 6;
            }

            if (entry.CommandFamily.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                textScore += 4;
            }
        }

        if (tokens.Count > 0 && textScore == 0)
        {
            return 0;
        }

        score += textScore;

        if (!string.IsNullOrWhiteSpace(request.CommandFamily)
            && string.Equals(entry.CommandFamily, request.CommandFamily, StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }

        if (!string.IsNullOrWhiteSpace(request.Discipline)
            && string.Equals(entry.Discipline, request.Discipline, StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }

        var requiredDocumentKinds = entry.RequiredContext?.RequiredDocumentKinds ?? new List<string>();
        if (!string.IsNullOrWhiteSpace(request.DocumentContext)
            && requiredDocumentKinds.Count > 0
            && requiredDocumentKinds.Any(x => string.Equals(x, request.DocumentContext, StringComparison.OrdinalIgnoreCase)))
        {
            score += 2;
        }

        score += CoverageStatusScore(entry.CoverageStatus);
        score += ScoreStrategyFit(entry);
        return score;
    }

    private static string BuildReason(CommandAtlasEntry entry, IReadOnlyCollection<string> tokens, CommandAtlasSearchRequest request)
    {
        entry ??= new CommandAtlasEntry();
        entry.Aliases ??= new List<string>();
        var reasons = new List<string>();
        foreach (var token in tokens)
        {
            if (entry.DisplayName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                reasons.Add("display");
            }
            else if (entry.Aliases.Any(x => x.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                reasons.Add("alias");
            }
            else if (entry.CommandId.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                reasons.Add("id");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Discipline)
            && string.Equals(entry.Discipline, request.Discipline, StringComparison.OrdinalIgnoreCase))
        {
            reasons.Add("discipline");
        }

        reasons.Add(entry.CoverageStatus);
        return string.Join(", ", reasons.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static int CoverageStatusScore(string status)
    {
        if (string.Equals(status, CommandCoverageStatuses.Verified, StringComparison.OrdinalIgnoreCase))
        {
            return 4;
        }

        if (string.Equals(status, CommandCoverageStatuses.Previewable, StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        if (string.Equals(status, CommandCoverageStatuses.Executable, StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    private static string ResolvePlannedToolName(CommandAtlasEntry entry)
    {
        if (entry == null)
        {
            return string.Empty;
        }

        if (string.Equals(entry.ExecutionMode, CommandExecutionModes.Tool, StringComparison.OrdinalIgnoreCase))
        {
            return entry.SourceRef;
        }

        if (string.Equals(entry.ExecutionMode, CommandExecutionModes.Script, StringComparison.OrdinalIgnoreCase))
        {
            return ToolNames.ScriptRunSafe;
        }

        if (string.Equals(entry.ExecutionMode, CommandExecutionModes.Workflow, StringComparison.OrdinalIgnoreCase))
        {
            return ToolNames.WorkflowQuickPlan;
        }

        return string.Empty;
    }

    private static string ResolvePayloadJson(CommandAtlasEntry entry, QuickActionRequest request, ICollection<string> missingContext)
    {
        if (entry == null)
        {
            return string.Empty;
        }

        if (string.Equals(entry.ExecutionMode, CommandExecutionModes.Native, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.ExecutionMode, CommandExecutionModes.Workflow, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (MatchesExecutionKey(entry, ToolNames.ViewCreate3dSafe, "revit.view.create_3d"))
        {
            return JsonUtil.Serialize(new Create3DViewRequest
            {
                ViewName = ExtractQuotedValue(request.Query) ?? $"AI 3D {DateTime.UtcNow:HHmmss}",
                ActivateViewAfterCreate = true
            });
        }

        if (MatchesExecutionKey(entry, ToolNames.ViewDuplicateSafe, "revit.view.duplicate"))
        {
            if (!request.ActiveViewId.HasValue || request.ActiveViewId.Value <= 0)
            {
                missingContext.Add("active_view");
                return string.Empty;
            }

            var mode = "Duplicate";
            var normalized = Normalize(request.Query);
            if (normalized.Contains("dependent"))
            {
                mode = "AsDependent";
            }
            else if (normalized.Contains("detail"))
            {
                mode = "WithDetailing";
            }

            return JsonUtil.Serialize(new DuplicateViewRequest
            {
                ViewId = request.ActiveViewId.Value,
                DuplicateMode = mode,
                NewName = ExtractQuotedValue(request.Query) ?? string.Empty,
                ActivateAfterCreate = true
            });
        }

        if (MatchesExecutionKey(entry, ToolNames.ViewCreateProjectViewSafe, "revit.view.create_floor_plan", "revit.view.create_ceiling_plan"))
        {
            if (!request.CurrentLevelId.HasValue && string.IsNullOrWhiteSpace(request.CurrentLevelName))
            {
                missingContext.Add("current_level");
                return string.Empty;
            }

            var normalized = Normalize(request.Query);
            var viewKind = normalized.Contains("ceiling") ? "ceiling_plan"
                : normalized.Contains("section") ? "section"
                : normalized.Contains("elevation") ? "elevation"
                : normalized.Contains("drafting") ? "drafting"
                : normalized.Contains("legend") ? "legend"
                : "floor_plan";

            var viewName = ExtractQuotedValue(request.Query)
                           ?? $"AI {viewKind.Replace('_', ' ')} {(string.IsNullOrWhiteSpace(request.CurrentLevelName) ? "View" : request.CurrentLevelName)}";

            return JsonUtil.Serialize(new CreateProjectViewRequest
            {
                ViewKind = viewKind,
                LevelId = request.CurrentLevelId,
                LevelName = request.CurrentLevelName ?? string.Empty,
                ViewName = viewName,
                ActivateAfterCreate = true
            });
        }

        if (MatchesExecutionKey(entry, ToolNames.ViewSetTemplateSafe, "revit.view.apply_template"))
        {
            if (!request.ActiveViewId.HasValue || request.ActiveViewId.Value <= 0)
            {
                missingContext.Add("active_view");
            }

            var templateName = ExtractTemplateName(request.Query);
            if (string.IsNullOrWhiteSpace(templateName))
            {
                missingContext.Add("template_name");
            }

            if (missingContext.Count > 0)
            {
                return string.Empty;
            }

            return JsonUtil.Serialize(new SetViewTemplateRequest
            {
                ViewId = request.ActiveViewId ?? 0,
                TemplateName = templateName
            });
        }

        if (MatchesExecutionKey(entry, ToolNames.SheetCreateSafe, "revit.sheet.create"))
        {
            var sheetNumber = ExtractSheetNumber(request.Query);
            if (string.IsNullOrWhiteSpace(sheetNumber))
            {
                missingContext.Add("sheet_number");
                return string.Empty;
            }

            return JsonUtil.Serialize(new CreateSheetRequest
            {
                SheetNumber = sheetNumber,
                SheetName = ExtractQuotedValue(request.Query) ?? sheetNumber
            });
        }

        if (MatchesExecutionKey(entry, ToolNames.SheetRenumberSafe, "revit.sheet.renumber"))
        {
            if (!request.CurrentSheetId.HasValue || request.CurrentSheetId.Value <= 0)
            {
                missingContext.Add("current_sheet");
            }

            var newSheetNumber = ExtractTargetSheetNumber(request.Query, request.CurrentSheetNumber);
            if (string.IsNullOrWhiteSpace(newSheetNumber))
            {
                missingContext.Add("new_sheet_number");
            }

            if (missingContext.Count > 0)
            {
                return string.Empty;
            }

            return JsonUtil.Serialize(new RenumberSheetRequest
            {
                SheetId = request.CurrentSheetId ?? 0,
                OldSheetNumber = request.CurrentSheetNumber ?? string.Empty,
                NewSheetNumber = newSheetNumber,
                NewSheetName = ExtractQuotedValue(request.Query) ?? string.Empty
            });
        }

        if (MatchesExecutionKey(entry, ToolNames.AnnotationAddTextNoteSafe, "revit.annotation.add_text_note"))
        {
            if (!request.ActiveViewId.HasValue || request.ActiveViewId.Value <= 0)
            {
                missingContext.Add("active_view");
                return string.Empty;
            }

            var text = ExtractQuotedValue(request.Query);
            if (string.IsNullOrWhiteSpace(text))
            {
                text = ExtractTrailingText(request.Query, "text note", "note", "ghi chu");
            }

            return JsonUtil.Serialize(new AddTextNoteRequest
            {
                ViewId = request.ActiveViewId,
                Text = string.IsNullOrWhiteSpace(text) ? "AI note" : text,
                UseViewCenterWhenPossible = true
            });
        }

        if (MatchesExecutionKey(entry, ToolNames.AuditPurgeUnusedSafe, "revit.manage.purge_unused"))
        {
            return JsonUtil.Serialize(new PurgeUnusedRequest
            {
                PurgeViews = true,
                PurgeFamilies = true
            });
        }

        if (MatchesExecutionKey(entry, ToolNames.ReviewSmartQc, "revit.manage.smart_qc"))
        {
            return JsonUtil.Serialize(new SmartQcRequest
            {
                RulesetName = "document_health_v1"
            });
        }

        if (MatchesExecutionKey(entry, ToolNames.ReviewModelHealth))
        {
            return string.Empty;
        }

        if (MatchesExecutionKey(entry, ToolNames.ReviewSheetSummary))
        {
            if (request.CurrentSheetId.HasValue && request.CurrentSheetId.Value > 0)
            {
                return JsonUtil.Serialize(new SheetSummaryRequest
                {
                    SheetId = request.CurrentSheetId.Value,
                    SheetNumber = request.CurrentSheetNumber ?? string.Empty,
                    MaxPlacedViews = 20
                });
            }

            var sheetNumber = ExtractSheetNumber(request.Query);
            if (!string.IsNullOrWhiteSpace(sheetNumber))
            {
                return JsonUtil.Serialize(new SheetSummaryRequest
                {
                    SheetNumber = sheetNumber,
                    SheetName = ExtractQuotedValue(request.Query) ?? string.Empty,
                    MaxPlacedViews = 20
                });
            }

            missingContext.Add("current_sheet");
            return string.Empty;
        }

        if (MatchesExecutionKey(entry, ToolNames.AuditNamingConvention))
        {
            var normalized = Normalize(request.Query);
            var scope = normalized.Contains("sheet") ? "sheets"
                : normalized.Contains("family") ? "families"
                : "views";
            var expectedPattern = ExtractQuotedValue(request.Query);

            return JsonUtil.Serialize(new NamingAuditRequest
            {
                Scope = scope,
                ExpectedPattern = expectedPattern,
                MaxResults = 200
            });
        }

        if (MatchesExecutionKey(entry, ToolNames.SheetPlaceViewsSafe))
        {
            if (!request.CurrentSheetId.HasValue || request.CurrentSheetId.Value <= 0)
            {
                missingContext.Add("current_sheet");
            }

            missingContext.Add("view_ids");
            return string.Empty;
        }

        if (MatchesExecutionKey(entry, ToolNames.DataExportSchedule))
        {
            var isScheduleView = !string.IsNullOrWhiteSpace(request.ActiveViewType)
                && request.ActiveViewType.IndexOf("schedule", StringComparison.OrdinalIgnoreCase) >= 0;
            if (request.ActiveViewId.HasValue && request.ActiveViewId.Value > 0 && isScheduleView)
            {
                return JsonUtil.Serialize(new ExportScheduleRequest
                {
                    ScheduleId = request.ActiveViewId.Value,
                    ScheduleName = request.ActiveViewName ?? string.Empty,
                    Format = ResolveExportFormat(request.Query)
                });
            }

            var scheduleName = ExtractQuotedValue(request.Query);
            if (!string.IsNullOrWhiteSpace(scheduleName))
            {
                return JsonUtil.Serialize(new ExportScheduleRequest
                {
                    ScheduleName = scheduleName,
                    Format = ResolveExportFormat(request.Query)
                });
            }

            missingContext.Add("current_schedule_or_name");
            return string.Empty;
        }

        if (string.Equals(entry.ExecutionMode, CommandExecutionModes.Script, StringComparison.OrdinalIgnoreCase))
        {
            return JsonUtil.Serialize(new ScriptRunRequest
            {
                ScriptId = entry.SourceRef,
                TimeoutMs = 30000
            });
        }

        missingContext.Add("explicit_payload");
        return string.Empty;
    }

    private static bool MatchesExecutionKey(CommandAtlasEntry entry, params string[] candidates)
    {
        var keys = new[]
            {
                entry.CommandId,
                entry.SourceRef,
                entry.NativeCommandId
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.Any(candidate => keys.Contains(candidate, StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsMvpCuratedEntry(CommandAtlasEntry entry)
    {
        return entry != null
               && (IsMvpCuratedCommand(entry.CommandId)
                   || IsMvpCuratedCommand(entry.SourceRef)
                   || IsMvpCuratedCommand(entry.NativeCommandId));
    }

    private static string ResolveExecutionDisposition(CommandAtlasEntry entry, string toolName, IReadOnlyCollection<string> missingContext)
    {
        if (missingContext.Count > 0)
        {
            return "clarify";
        }

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return string.Equals(entry.ExecutionMode, CommandExecutionModes.Native, StringComparison.OrdinalIgnoreCase)
                ? "mapped_only"
                : "blocked";
        }

        if (entry.NeedsApproval || entry.CanPreview)
        {
            return "preview";
        }

        return entry.CanAutoExecute ? "direct" : "dispatch";
    }

    private static string BuildQuickSummary(CommandAtlasEntry entry, string toolName, string disposition, IReadOnlyCollection<string> missingContext)
    {
        if (missingContext.Count > 0)
        {
            return $"Quick action `{entry.DisplayName}` can them context: {string.Join(", ", missingContext)}.";
        }

        if (string.Equals(disposition, "mapped_only", StringComparison.OrdinalIgnoreCase))
        {
            return $"`{entry.DisplayName}` da duoc map trong atlas nhung chua co execution lane tu agent.";
        }

        return $"Quick action `{entry.DisplayName}` route qua {toolName} ({disposition}).";
    }

    private static int ScoreStrategyFit(CommandAtlasEntry entry)
    {
        entry ??= new CommandAtlasEntry();
        var score = 0;

        if (string.Equals(entry.PrimaryPersona, ToolPrimaryPersonas.ProductionBimer, StringComparison.OrdinalIgnoreCase))
        {
            score += 6;
        }

        if (string.Equals(entry.UserValueClass, ToolUserValueClasses.DailyRoi, StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }
        else if (string.Equals(entry.UserValueClass, ToolUserValueClasses.SmartValue, StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }

        if (string.Equals(entry.AutomationStage, ToolAutomationStages.CoreSkill, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.AutomationStage, ToolAutomationStages.PlaybookReady, StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
        }

        if (entry.CanTeachBack)
        {
            score += 2;
        }

        if (string.Equals(entry.CommercialTier, CommercialTiers.PersonalPro, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.CommercialTier, CommercialTiers.Free, StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
        }

        return score;
    }

    private static FallbackArtifactRequest BuildFallbackRequest(QuickActionRequest request, string reason, CommandAtlasEntry? matchedEntry = null, string? plannedToolName = null)
    {
        request ??= new QuickActionRequest();
        matchedEntry ??= new CommandAtlasEntry();

        var candidateToolNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(plannedToolName))
        {
            candidateToolNames.Add(plannedToolName!);
        }

        if (!string.IsNullOrWhiteSpace(matchedEntry.SourceRef))
        {
            candidateToolNames.Add(matchedEntry.SourceRef);
        }

        if (!string.IsNullOrWhiteSpace(matchedEntry.CommandId))
        {
            candidateToolNames.Add(matchedEntry.CommandId);
        }

        return new FallbackArtifactRequest
        {
            WorkspaceId = request.WorkspaceId ?? string.Empty,
            Query = request.Query ?? string.Empty,
            Reason = reason,
            PrimaryPersona = string.IsNullOrWhiteSpace(matchedEntry.PrimaryPersona) ? ToolPrimaryPersonas.ProductionBimer : matchedEntry.PrimaryPersona,
            RequestedKinds = matchedEntry.FallbackArtifactKinds?.ToList() ?? new List<string>(),
            CandidateToolNames = candidateToolNames
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            CandidatePlaybookIds = matchedEntry.RecommendedPlaybooks?.ToList() ?? new List<string>(),
            InputSummary = $"doc={request.DocumentContext}; discipline={request.Discipline}; view={request.ActiveViewName}; level={request.CurrentLevelName}; sheet={request.CurrentSheetNumber}; selection={request.SelectionCount}",
            CommercialTier = string.IsNullOrWhiteSpace(matchedEntry.CommercialTier) ? CommercialTiers.PersonalPro : matchedEntry.CommercialTier
        };
    }

    private static List<string> ResolveFallbackKindsForQuery(string query)
    {
        var normalized = Normalize(query);
        var results = new List<string> { FallbackArtifactKinds.Playbook };

        if (normalized.Contains("excel")
            || normalized.Contains("csv")
            || normalized.Contains("openxml")
            || normalized.Contains("parameter")
            || normalized.Contains("schedule")
            || normalized.Contains("import"))
        {
            results.Add(FallbackArtifactKinds.CsvMapping);
            results.Add(FallbackArtifactKinds.OpenXmlRecipe);
        }

        if (normalized.Contains("export")
            || normalized.Contains("pdf")
            || normalized.Contains("dwg")
            || normalized.Contains("ifc")
            || normalized.Contains("issue"))
        {
            results.Add(FallbackArtifactKinds.ExportProfile);
        }

        return results
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> SuggestCandidateToolNames(IEnumerable<ToolManifest> manifests, string query)
    {
        var normalized = Normalize(query);
        var results = new List<string>();
        var catalog = (manifests ?? Array.Empty<ToolManifest>()).ToList();

        if (normalized.Contains("sheet"))
        {
            AddIfKnown(catalog, results, ToolNames.SheetCreateSafe);
            AddIfKnown(catalog, results, ToolNames.SheetPlaceViewsSafe);
            AddIfKnown(catalog, results, ToolNames.SheetRenumberSafe);
        }

        if (normalized.Contains("view"))
        {
            AddIfKnown(catalog, results, ToolNames.ViewDuplicateSafe);
            AddIfKnown(catalog, results, ToolNames.ViewSetTemplateSafe);
            AddIfKnown(catalog, results, ToolNames.ViewCreate3dSafe);
        }

        if (normalized.Contains("parameter")
            || normalized.Contains("excel")
            || normalized.Contains("csv")
            || normalized.Contains("schedule")
            || normalized.Contains("import")
            || normalized.Contains("export"))
        {
            AddIfKnown(catalog, results, ToolNames.DataExportSchedule);
            AddIfKnown(catalog, results, ToolNames.DataPreviewImport);
            AddIfKnown(catalog, results, ToolNames.DataImportSafe);
            AddIfKnown(catalog, results, ToolNames.ParameterBatchFillSafe);
            AddIfKnown(catalog, results, ToolNames.ParameterCopyBetweenSafe);
        }

        if (normalized.Contains("qc") || normalized.Contains("audit"))
        {
            AddIfKnown(catalog, results, ToolNames.ReviewSmartQc);
            AddIfKnown(catalog, results, ToolNames.ReviewModelHealth);
            AddIfKnown(catalog, results, ToolNames.AuditNamingConvention);
        }

        return results
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    private static List<string> SuggestCandidatePlaybooks(IEnumerable<ToolManifest> manifests, IEnumerable<string> candidateToolNames, string query)
    {
        var catalog = (manifests ?? Array.Empty<ToolManifest>()).ToList();
        var results = new List<string>();

        foreach (var tool in candidateToolNames ?? Array.Empty<string>())
        {
            var manifest = catalog.FirstOrDefault(x => string.Equals(x.ToolName, tool, StringComparison.OrdinalIgnoreCase));
            foreach (var playbook in manifest?.RecommendedPlaybooks ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(playbook) && !results.Contains(playbook, StringComparer.OrdinalIgnoreCase))
                {
                    results.Add(playbook);
                }
            }
        }

        var normalized = Normalize(query);
        if (normalized.Contains("sheet"))
        {
            AddIfMissing(results, "sheet_create_arch_package.v1");
            AddIfMissing(results, "sheet_review_team_standard.v1");
        }

        if (normalized.Contains("parameter") || normalized.Contains("import") || normalized.Contains("excel") || normalized.Contains("csv"))
        {
            AddIfMissing(results, "parameter_import_fill.v1");
        }

        return results.Take(4).ToList();
    }

    private static List<string> BuildProposedArtifactPaths(string workspaceId, string query, IReadOnlyCollection<string> kinds)
    {
        var slug = Slugify(query);
        var basePath = $"artifacts/fallback/{(string.IsNullOrWhiteSpace(workspaceId) ? "default" : workspaceId)}/{slug}";
        var paths = new List<string>();

        foreach (var kind in kinds ?? Array.Empty<string>())
        {
            var suffix = kind switch
            {
                var x when string.Equals(x, FallbackArtifactKinds.Playbook, StringComparison.OrdinalIgnoreCase) => ".playbook.json",
                var x when string.Equals(x, FallbackArtifactKinds.CsvMapping, StringComparison.OrdinalIgnoreCase) => ".csv_mapping.json",
                var x when string.Equals(x, FallbackArtifactKinds.OpenXmlRecipe, StringComparison.OrdinalIgnoreCase) => ".openxml_recipe.json",
                var x when string.Equals(x, FallbackArtifactKinds.ExportProfile, StringComparison.OrdinalIgnoreCase) => ".export_profile.json",
                var x when string.Equals(x, FallbackArtifactKinds.DynamoTemplate, StringComparison.OrdinalIgnoreCase) => ".dynamo_template.json",
                var x when string.Equals(x, FallbackArtifactKinds.ExternalWrapper, StringComparison.OrdinalIgnoreCase) => ".external_wrapper.json",
                _ => ".artifact.json"
            };
            paths.Add(basePath + suffix);
        }

        return paths;
    }

    private static string BuildFallbackInputSummary(FallbackArtifactRequest request)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            parts.Add("query=" + request.Query);
        }

        if (!string.IsNullOrWhiteSpace(request.Reason))
        {
            parts.Add("reason=" + request.Reason);
        }

        if (!string.IsNullOrWhiteSpace(request.InputSummary))
        {
            parts.Add(request.InputSummary);
        }

        return string.Join("; ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private static List<string> BuildFallbackInputs(string query, string inputSummary)
    {
        var results = new List<string>();
        if (!string.IsNullOrWhiteSpace(query))
        {
            results.Add("query=" + query);
        }

        if (!string.IsNullOrWhiteSpace(inputSummary))
        {
            results.Add(inputSummary);
        }

        return results;
    }

    private static string BuildVerificationRecipe(IReadOnlyCollection<string> kinds)
    {
        if (kinds.Any(x => string.Equals(x, FallbackArtifactKinds.CsvMapping, StringComparison.OrdinalIgnoreCase)
                           || string.Equals(x, FallbackArtifactKinds.OpenXmlRecipe, StringComparison.OrdinalIgnoreCase)))
        {
            return "review mapping -> preview import/export -> approval -> verify diff/evidence -> save reusable skill";
        }

        if (kinds.Any(x => string.Equals(x, FallbackArtifactKinds.ExportProfile, StringComparison.OrdinalIgnoreCase)))
        {
            return "review export profile -> preview output target -> approval -> verify artifact names/profile reuse";
        }

        return "review playbook artifact -> preview chain -> approval -> verify evidence -> save reusable skill";
    }

    private static string BuildQuickStrategySummary(CommandAtlasEntry entry, string disposition, FallbackArtifactProposal fallbackProposal)
    {
        if (string.IsNullOrWhiteSpace(entry?.CommandId))
        {
            return "atlas_search -> fallback_artifact";
        }

        if (HasFallbackProposal(fallbackProposal))
        {
            return $"tool -> playbook -> fallback_artifact ({fallbackProposal.Reason})";
        }

        return $"tool -> {disposition}";
    }

    private static bool HasFallbackProposal(FallbackArtifactProposal? proposal)
    {
        return proposal != null
               && (((proposal.ArtifactKinds?.Count ?? 0) > 0)
                   || ((proposal.ArtifactPaths?.Count ?? 0) > 0)
                   || !string.IsNullOrWhiteSpace(proposal.Summary)
                   || !string.IsNullOrWhiteSpace(proposal.PreviewSummary));
    }

    private static string Slugify(string value)
    {
        var normalized = Regex.Replace((value ?? string.Empty).Trim().ToLowerInvariant(), @"[^a-z0-9]+", "_");
        normalized = normalized.Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "quick_plan" : normalized;
    }

    private static void AddIfKnown(IEnumerable<ToolManifest> manifests, ICollection<string> results, string toolName)
    {
        if ((manifests ?? Array.Empty<ToolManifest>()).Any(x => string.Equals(x.ToolName, toolName, StringComparison.OrdinalIgnoreCase)))
        {
            AddIfMissing(results, toolName);
        }
    }

    private static void AddIfMissing(ICollection<string> values, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }

    private static string BuildScriptCommandFamily(ScriptSourceManifest script)
    {
        if ((script.Tags ?? new List<string>()).Any(x => x.IndexOf("documentation", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            return "sheet_documentation";
        }

        return string.IsNullOrWhiteSpace(script.CapabilityDomain) ? "automation" : script.CapabilityDomain;
    }

    private static List<string> BuildAliases(string id, string displayName, IEnumerable<string> extra)
    {
        return Tokenize(string.Join(" ", new[] { id, displayName }.Concat(extra ?? Array.Empty<string>())))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string HumanizeToolName(string toolName)
    {
        var value = toolName ?? string.Empty;
        var last = value.LastIndexOf('.');
        var token = last >= 0 ? value.Substring(last + 1) : value;
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(token.Replace('_', ' '));
    }

    private static string Normalize(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static List<string> Tokenize(string value)
    {
        return Regex.Split(value ?? string.Empty, "[\\s,;:/\\\\._-]+")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ExtractQuotedValue(string query)
    {
        var match = Regex.Match(query ?? string.Empty, "\"(?<value>[^\"]+)\"");
        return match.Success ? match.Groups["value"].Value : string.Empty;
    }

    private static string ExtractSheetNumber(string query)
    {
        var match = Regex.Match(query ?? string.Empty, "\\b[A-Za-z]{0,2}\\d{2,4}\\b");
        return match.Success ? match.Value : string.Empty;
    }

    private static string ExtractTemplateName(string query)
    {
        var quoted = ExtractQuotedValue(query);
        if (!string.IsNullOrWhiteSpace(quoted))
        {
            return quoted;
        }

        var normalized = query ?? string.Empty;
        var marker = normalized.IndexOf("template", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return string.Empty;
        }

        var tail = normalized.Substring(marker + "template".Length).Trim();
        if (tail.StartsWith("=", StringComparison.OrdinalIgnoreCase))
        {
            tail = tail.Substring(1).Trim();
        }

        return tail;
    }

    private static string ExtractTargetSheetNumber(string query, string? currentSheetNumber)
    {
        var matches = Regex.Matches(query ?? string.Empty, "\\b[A-Za-z]{0,2}\\d{2,4}\\b");
        if (matches.Count == 0)
        {
            return string.Empty;
        }

        var current = currentSheetNumber ?? string.Empty;
        foreach (Match match in matches)
        {
            if (!string.Equals(match.Value, current, StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }
        }

        return matches[matches.Count - 1].Value;
    }

    private static string ExtractTrailingText(string query, params string[] markers)
    {
        var input = query ?? string.Empty;
        foreach (var marker in markers ?? Array.Empty<string>())
        {
            var index = input.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var tail = input.Substring(index + marker.Length).Trim();
            if (tail.StartsWith("=", StringComparison.OrdinalIgnoreCase))
            {
                tail = tail.Substring(1).Trim();
            }

            if (!string.IsNullOrWhiteSpace(tail))
            {
                return tail;
            }
        }

        return string.Empty;
    }

    private static string ResolveExportFormat(string query)
    {
        var normalized = Normalize(query);
        if (normalized.Contains("csv"))
        {
            return "csv";
        }

        if (normalized.Contains("tsv"))
        {
            return "tsv";
        }

        return "json";
    }
}

[DataContract]
public sealed class CommandAtlasPackFile
{
    [DataMember(Order = 1)]
    public List<CommandAtlasEntry> Entries { get; set; } = new List<CommandAtlasEntry>();
}

[DataContract]
public sealed class ScriptSourcePackFile
{
    [DataMember(Order = 1)]
    public List<ScriptSourceManifest> Scripts { get; set; } = new List<ScriptSourceManifest>();
}
