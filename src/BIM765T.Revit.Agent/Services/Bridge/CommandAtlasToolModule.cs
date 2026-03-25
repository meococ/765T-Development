using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BIM765T.Revit.Agent.Infrastructure;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.Copilot.Core;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class CommandAtlasToolModule : IToolModule
{
    private readonly ToolModuleContext _context;
    private readonly CommandAtlasService _atlas;
    private readonly CuratedScriptRegistryService _curatedScripts;

    internal CommandAtlasToolModule(ToolModuleContext context)
    {
        _context = context;
        _atlas = new CommandAtlasService();
        _curatedScripts = new CuratedScriptRegistryService();
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var atlasRead = ToolManifestPresets.Read()
            .WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul)
            .WithSkillGroup(WorkerSkillGroups.Intent)
            .WithCapabilityDomain(CapabilityDomains.Intent)
            .WithDeterminismLevel(ToolDeterminismLevels.Deterministic)
            .WithVerificationMode(ToolVerificationModes.ReportOnly)
            .WithCommandFamily("command_atlas")
            .WithSourceKind(CommandSourceKinds.Internal)
            .WithCoverageTier(CommandCoverageTiers.Baseline)
            .WithCanPreview();
        var atlasReview = ToolManifestPresets.Review()
            .WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul)
            .WithSkillGroup(WorkerSkillGroups.Intent)
            .WithCapabilityDomain(CapabilityDomains.Intent)
            .WithDeterminismLevel(ToolDeterminismLevels.PolicyBacked)
            .WithVerificationMode(ToolVerificationModes.PolicyCheck)
            .WithCommandFamily("command_atlas")
            .WithSourceKind(CommandSourceKinds.Internal)
            .WithCoverageTier(CommandCoverageTiers.Baseline)
            .WithCanPreview();
        var atlasMutation = ToolManifestPresets.Mutation("document")
            .WithCapabilityPack(WorkerCapabilityPacks.MemoryAndSoul)
            .WithSkillGroup(WorkerSkillGroups.Intent)
            .WithCapabilityDomain(CapabilityDomains.Intent)
            .WithDeterminismLevel(ToolDeterminismLevels.PolicyBacked)
            .WithVerificationMode(ToolVerificationModes.PolicyCheck)
            .WithCommandFamily("command_atlas")
            .WithSourceKind(CommandSourceKinds.Internal)
            .WithCoverageTier(CommandCoverageTiers.Baseline)
            .WithCanPreview();

        registry.Register(
            ToolNames.CommandSearch,
            "Search the curated command atlas across native Revit commands, internal tools, and curated scripts.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            atlasRead.WithExecutionMode(CommandExecutionModes.Workflow),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<CommandAtlasSearchRequest>(request);
                return ToolResponses.Success(request, _atlas.Search(registry.GetToolCatalog(), payload));
            },
            "{\"WorkspaceId\":\"default\",\"Query\":\"create 3d view\",\"Discipline\":\"\",\"DocumentContext\":\"project\",\"CommandFamily\":\"\",\"MaxResults\":10}");

        registry.Register(
            ToolNames.CommandDescribe,
            "Describe one atlas command entry with execution policy, required context, coverage, and fallback metadata.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            atlasRead.WithExecutionMode(CommandExecutionModes.Workflow),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<CommandDescribeRequest>(request);
                return ToolResponses.Success(request, _atlas.Describe(registry.GetToolCatalog(), payload));
            },
            "{\"WorkspaceId\":\"default\",\"CommandId\":\"\",\"Query\":\"duplicate active view\"}");

        registry.Register(
            ToolNames.CommandCoverageReport,
            "Build a command atlas coverage report grouped by command family and baseline status.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            atlasRead.WithExecutionMode(CommandExecutionModes.Workflow),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<CoverageReportRequest>(request);
                return ToolResponses.Success(request, _atlas.BuildCoverageReport(registry.GetToolCatalog(), payload));
            },
            "{\"WorkspaceId\":\"default\",\"CoverageTier\":\"baseline\"}");

        registry.Register(
            ToolNames.WorkflowQuickPlan,
            "Resolve a quick command/workflow action with context auto-fill before falling back to heavier playbook orchestration.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            atlasReview.WithExecutionMode(CommandExecutionModes.Workflow),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<QuickActionRequest>(request);
                var doc = ResolveQuickDocument(uiapp, request);
                FillQuickContext(uiapp, doc, payload);
                return ToolResponses.Success(request, _atlas.PlanQuickAction(registry.GetToolCatalog(), payload));
            },
            "{\"WorkspaceId\":\"default\",\"Query\":\"create 3d view\",\"Discipline\":\"\",\"DocumentContext\":\"project\"}");

        registry.Register(
            ToolNames.FallbackArtifactPlan,
            "Build a guarded fallback artifact proposal when atlas coverage is missing or intentionally blocked.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            atlasReview
                .WithCommandFamily("fallback_artifacts")
                .WithExecutionMode(CommandExecutionModes.Workflow)
                .WithUserValueClass(ToolUserValueClasses.TemplateGeneration)
                .WithAutomationStage(ToolAutomationStages.ArtifactFallback)
                .WithCommercialTier(CommercialTiers.PersonalPro)
                .WithFallbackArtifactKinds(FallbackArtifactKinds.Playbook, FallbackArtifactKinds.CsvMapping, FallbackArtifactKinds.OpenXmlRecipe, FallbackArtifactKinds.ExportProfile),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<FallbackArtifactRequest>(request);
                return ToolResponses.Success(request, _atlas.BuildFallbackProposal(registry.GetToolCatalog(), payload));
            },
            "{\"WorkspaceId\":\"default\",\"Query\":\"import sheet metadata from excel\",\"Reason\":\"atlas_miss\",\"RequestedKinds\":[\"playbook\",\"csv_mapping\",\"openxml_recipe\"]}");

        registry.Register(
            ToolNames.CommandExecuteSafe,
            "Resolve and execute a quick atlas action safely, preserving preview/approval/verify semantics of the underlying tool lane.",
            PermissionLevel.Mutate,
            ApprovalRequirement.None,
            true,
            atlasMutation.WithExecutionMode(CommandExecutionModes.Workflow).WithRiskTags("quick-path", "atlas"),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<CommandExecuteRequest>(request);
                return ExecuteQuickCommand(uiapp, request, payload, registry);
            },
            "{\"WorkspaceId\":\"default\",\"CommandId\":\"\",\"Query\":\"duplicate active view dependent\",\"PayloadJson\":\"\",\"AllowAutoExecute\":false,\"TargetDocument\":\"\",\"TargetView\":\"\"}");

        registry.Register(
            ToolNames.ScriptVerifySource,
            "Verify a curated script manifest for provenance, approval policy, and executable-wrapper readiness.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            atlasRead.WithCommandFamily("script_registry").WithExecutionMode(CommandExecutionModes.Workflow),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ScriptSourceVerifyRequest>(request);
                return ToolResponses.Success(request, _curatedScripts.Verify(payload.Manifest));
            },
            "{\"Manifest\":{\"ScriptId\":\"builtin.audit_family_parameters\",\"DisplayName\":\"Audit family parameters\",\"SourceKind\":\"internal\",\"SourceRef\":\"builtin\",\"EntryPoint\":\"builtin.audit_family_parameters\",\"CapabilityDomain\":\"family_qa\",\"SupportedDisciplines\":[\"common\"],\"AllowedSurfaces\":[\"worker\"],\"SafetyClass\":\"read_only\",\"ApprovalRequirement\":0,\"VerificationMode\":\"report_only\",\"Tags\":[\"family\",\"qa\"],\"Approved\":true,\"VerificationRecipe\":\"read_back\"}}");

        registry.Register(
            ToolNames.ScriptImportManifest,
            "Import a curated script manifest into the local approved registry without executing it.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            atlasReview.WithCommandFamily("script_registry").WithExecutionMode(CommandExecutionModes.Workflow),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ScriptImportManifestRequest>(request);
                var result = _curatedScripts.Import(payload);
                return result.Imported
                    ? ToolResponses.Success(request, result, StatusCodes.ExecuteSucceeded)
                    : ToolResponses.Failure(request, StatusCodes.ScriptManifestImportBlocked, result.Summary);
            },
            "{\"Manifest\":{\"ScriptId\":\"builtin.audit_family_parameters\",\"DisplayName\":\"Audit family parameters\",\"SourceKind\":\"internal\",\"SourceRef\":\"builtin\",\"EntryPoint\":\"builtin.audit_family_parameters\",\"CapabilityDomain\":\"family_qa\",\"SupportedDisciplines\":[\"common\"],\"AllowedSurfaces\":[\"worker\"],\"SafetyClass\":\"read_only\",\"ApprovalRequirement\":0,\"VerificationMode\":\"report_only\",\"Tags\":[\"family\",\"qa\"],\"Approved\":true,\"VerificationRecipe\":\"read_back\"},\"OverwriteExisting\":true}");

        registry.Register(
            ToolNames.ScriptInstallPack,
            "Install a curated script pack into the local registry after provenance validation.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            atlasReview.WithCommandFamily("script_registry").WithExecutionMode(CommandExecutionModes.Workflow),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ScriptInstallPackRequest>(request);
                var result = _curatedScripts.InstallPack(payload);
                return ToolResponses.Success(request, result, StatusCodes.ExecuteSucceeded);
            },
            "{\"WorkspaceId\":\"default\",\"PackId\":\"curated-internal\",\"Scripts\":[]}");

        registry.Register(
            ToolNames.MemorySearchScoped,
            "Search atlas, playbook/policy, and verified-run memory using small scoped namespaces to avoid context bloat.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            atlasRead.WithCommandFamily("memory_scoped").WithExecutionMode(CommandExecutionModes.Workflow),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<MemoryScopedSearchRequest>(request);
                return ToolResponses.Success(request, SearchScopedMemory(payload, registry));
            },
            "{\"Query\":\"sheet package\",\"DocumentKey\":\"\",\"WorkspaceId\":\"default\",\"RetrievalScope\":\"workflow_path\",\"Namespaces\":[],\"Discipline\":\"\",\"CommandFamily\":\"\",\"SafetyClass\":\"\",\"SourceKind\":\"\",\"IssueKind\":\"\",\"MaxResults\":5}");

        registry.Register(
            ToolNames.MemoryPromoteVerifiedRun,
            "Promote a verified run into reusable lesson/evidence memory without opening the full task-runtime API surface.",
            PermissionLevel.Review,
            ApprovalRequirement.None,
            false,
            atlasReview.WithCommandFamily("memory_scoped").WithExecutionMode(CommandExecutionModes.Workflow),
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<TaskPromoteMemoryRequest>(request);
                var result = _context.CopilotTasks.PromoteMemory(payload, request.Caller);
                return ToolResponses.Success(request, result, StatusCodes.ExecuteSucceeded);
            },
            "{\"RunId\":\"\",\"PromotionKind\":\"lesson\",\"Summary\":\"\",\"Tags\":[],\"Notes\":\"\"}");
    }

    private ToolResponseEnvelope ExecuteQuickCommand(Autodesk.Revit.UI.UIApplication uiapp, ToolRequestEnvelope request, CommandExecuteRequest payload, ToolRegistry registry)
    {
        var doc = ResolveQuickDocument(uiapp, request);
        var quickRequest = new QuickActionRequest
        {
            WorkspaceId = payload.WorkspaceId,
            Query = string.IsNullOrWhiteSpace(payload.Query) ? payload.CommandId : payload.Query
        };
        FillQuickContext(uiapp, doc, quickRequest);
        var quick = _atlas.PlanQuickAction(registry.GetToolCatalog(), quickRequest);
        var entry = !string.IsNullOrWhiteSpace(payload.CommandId)
            ? _atlas.Describe(registry.GetToolCatalog(), new CommandDescribeRequest
            {
                WorkspaceId = payload.WorkspaceId,
                CommandId = payload.CommandId,
                Query = payload.Query
            })
            : quick.MatchedEntry;

        var toolName = string.IsNullOrWhiteSpace(quick.PlannedToolName) ? entry.SourceRef : quick.PlannedToolName;
        var fallbackProposal = HasFallbackProposal(quick.FallbackProposal)
            ? quick.FallbackProposal
            : _atlas.BuildFallbackProposal(registry.GetToolCatalog(), new FallbackArtifactRequest
            {
                WorkspaceId = payload.WorkspaceId,
                Query = quickRequest.Query,
                Reason = string.IsNullOrWhiteSpace(quick.ExecutionDisposition) ? "atlas_miss" : quick.ExecutionDisposition,
                CandidateToolNames = new List<string> { toolName }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList(),
                CandidatePlaybookIds = entry.RecommendedPlaybooks?.ToList() ?? new List<string>(),
                RequestedKinds = entry.FallbackArtifactKinds?.ToList() ?? new List<string>(),
                InputSummary = $"doc={quickRequest.DocumentContext}; view={quickRequest.ActiveViewName}; level={quickRequest.CurrentLevelName}; sheet={quickRequest.CurrentSheetNumber}; selection={quickRequest.SelectionCount}",
                CommercialTier = string.IsNullOrWhiteSpace(entry.CommercialTier) ? CommercialTiers.PersonalPro : entry.CommercialTier
            });

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return BuildBlockedResponse(
                request,
                string.Equals(entry.ExecutionMode, CommandExecutionModes.Native, StringComparison.OrdinalIgnoreCase)
                    ? StatusCodes.CommandCoverageIncomplete
                    : StatusCodes.CommandExecutionBlocked,
                quick.Summary,
                entry,
                toolName,
                payload.PayloadJson,
                fallbackProposal);
        }

        if (!CommandAtlasService.IsMvpCuratedCommand(entry.CommandId))
        {
            return BuildBlockedResponse(
                request,
                StatusCodes.CommandExecutionBlocked,
                $"Command `{entry.CommandId}` nam ngoai curated MVP surface va da bi an co chu dich.",
                entry,
                toolName,
                payload.PayloadJson,
                fallbackProposal);
        }

        if (string.Equals(quick.ExecutionDisposition, "mapped_only", StringComparison.OrdinalIgnoreCase)
            || string.Equals(quick.ExecutionDisposition, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            return BuildBlockedResponse(
                request,
                StatusCodes.CommandExecutionBlocked,
                quick.Summary,
                entry,
                toolName,
                payload.PayloadJson,
                fallbackProposal);
        }

        if (string.Equals(toolName, ToolNames.WorkflowQuickPlan, StringComparison.OrdinalIgnoreCase))
        {
            return ToolResponses.Success(request, quick, StatusCodes.ReadSucceeded);
        }

        if (!registry.TryGet(toolName, out var target))
        {
            return BuildBlockedResponse(
                request,
                StatusCodes.CommandNotFound,
                $"Tool lane `{toolName}` is not registered.",
                entry,
                toolName,
                payload.PayloadJson,
                fallbackProposal);
        }

        var forwarded = new ToolRequestEnvelope
        {
            RequestId = Guid.NewGuid().ToString("N"),
            ToolName = toolName,
            PayloadJson = string.IsNullOrWhiteSpace(payload.PayloadJson) ? quick.ResolvedPayloadJson : payload.PayloadJson,
            Caller = string.IsNullOrWhiteSpace(request.Caller) ? "command.execute_safe" : request.Caller,
            SessionId = request.SessionId,
            DryRun = !payload.AllowAutoExecute || string.Equals(quick.ExecutionDisposition, "preview", StringComparison.OrdinalIgnoreCase) || entry.CanPreview || entry.NeedsApproval,
            TargetDocument = string.IsNullOrWhiteSpace(payload.TargetDocument) ? request.TargetDocument : payload.TargetDocument,
            TargetView = string.IsNullOrWhiteSpace(payload.TargetView) ? request.TargetView : payload.TargetView,
            ExpectedContextJson = request.ExpectedContextJson,
            ApprovalToken = request.ApprovalToken,
            ScopeDescriptorJson = request.ScopeDescriptorJson,
            RequestedAtUtc = request.RequestedAtUtc,
            PreviewRunId = request.PreviewRunId,
            CorrelationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString("N") : request.CorrelationId,
            ProtocolVersion = request.ProtocolVersion
        };

        if (string.IsNullOrWhiteSpace(forwarded.PayloadJson))
        {
            return BuildBlockedResponse(
                request,
                StatusCodes.CommandContextMissing,
                quick.Summary,
                entry,
                toolName,
                payload.PayloadJson,
                fallbackProposal);
        }

        var delegated = target.Handler(uiapp, forwarded);
        var response = new CommandExecuteResponse
        {
            StatusCode = delegated.StatusCode,
            Summary = string.IsNullOrWhiteSpace(quick.Summary)
                ? $"Delegated `{entry.DisplayName}` to `{toolName}`."
                : quick.Summary,
            ToolName = toolName,
            RequestPayloadJson = forwarded.PayloadJson,
            Entry = entry,
            ConfirmationRequired = delegated.ConfirmationRequired,
            ApprovalToken = delegated.ApprovalToken,
            PreviewRunId = delegated.PreviewRunId,
            ToolResponseJson = delegated.PayloadJson
        };

        return BuildDelegatedResponse(request, delegated, response);
    }

    private static ToolResponseEnvelope BuildBlockedResponse(
        ToolRequestEnvelope request,
        string statusCode,
        string summary,
        CommandAtlasEntry? entry,
        string? toolName,
        string? payloadJson,
        FallbackArtifactProposal? fallbackProposal)
    {
        var response = new CommandExecuteResponse
        {
            StatusCode = statusCode,
            Summary = summary ?? string.Empty,
            ToolName = toolName ?? string.Empty,
            RequestPayloadJson = payloadJson ?? string.Empty,
            Entry = entry ?? new CommandAtlasEntry(),
            FallbackProposal = fallbackProposal ?? new FallbackArtifactProposal()
        };

        var envelope = ToolResponses.Success(request, response, statusCode);
        envelope.Succeeded = false;
        envelope.StatusCode = statusCode;
        envelope.Stage = WorkerStages.Recovery;
        envelope.Progress = 100;
        envelope.Diagnostics = new List<DiagnosticRecord>
        {
            DiagnosticRecord.Create(statusCode, DiagnosticSeverity.Error, summary ?? string.Empty)
        };
        return envelope;
    }

    private static bool HasFallbackProposal(FallbackArtifactProposal? proposal)
    {
        return proposal != null
               && (((proposal.ArtifactKinds?.Count ?? 0) > 0)
                   || ((proposal.ArtifactPaths?.Count ?? 0) > 0)
                   || !string.IsNullOrWhiteSpace(proposal.Summary)
                   || !string.IsNullOrWhiteSpace(proposal.PreviewSummary));
    }

    private static ToolResponseEnvelope BuildDelegatedResponse(ToolRequestEnvelope request, ToolResponseEnvelope delegated, CommandExecuteResponse payload)
    {
        var response = ToolResponses.Success(request, payload, delegated.StatusCode);
        response.Succeeded = delegated.Succeeded || delegated.ConfirmationRequired;
        response.StatusCode = delegated.StatusCode;
        response.Diagnostics = delegated.Diagnostics?.ToList() ?? new List<DiagnosticRecord>();
        response.ConfirmationRequired = delegated.ConfirmationRequired;
        response.ApprovalToken = delegated.ApprovalToken ?? string.Empty;
        response.PreviewRunId = delegated.PreviewRunId ?? string.Empty;
        response.ChangedIds = delegated.ChangedIds?.ToList() ?? new List<int>();
        response.Artifacts = delegated.Artifacts?.ToList() ?? new List<string>();
        response.DiffSummaryJson = delegated.DiffSummaryJson ?? string.Empty;
        response.ReviewSummaryJson = delegated.ReviewSummaryJson ?? string.Empty;
        response.Stage = string.IsNullOrWhiteSpace(delegated.Stage) ? response.Stage : delegated.Stage;
        response.Progress = delegated.Progress;
        response.ExecutionTier = delegated.ExecutionTier;
        response.HeartbeatUtc = delegated.HeartbeatUtc;
        return response;
    }

    private MemoryScopedSearchResponse SearchScopedMemory(MemoryScopedSearchRequest request, ToolRegistry registry)
    {
        request ??= new MemoryScopedSearchRequest();
        var workspaceId = string.IsNullOrWhiteSpace(request.WorkspaceId) ? "default" : request.WorkspaceId;
        var namespaces = ResolveNamespaces(request);
        var hits = new List<ScopedMemoryHit>();

        if (namespaces.Contains(MemoryNamespaces.AtlasNativeCommands)
            || namespaces.Contains(MemoryNamespaces.AtlasCustomTools)
            || namespaces.Contains(MemoryNamespaces.AtlasCuratedScripts))
        {
            var atlasMatches = _atlas.Search(registry.GetToolCatalog(), new CommandAtlasSearchRequest
            {
                WorkspaceId = workspaceId,
                Query = request.Query,
                Discipline = request.Discipline,
                CommandFamily = request.CommandFamily,
                MaxResults = Math.Min(3, Math.Max(1, request.MaxResults))
            });

            foreach (var match in atlasMatches.Matches)
            {
                var ns = ResolveAtlasNamespace(match.Entry);
                if (!namespaces.Contains(ns))
                {
                    continue;
                }

                hits.Add(new ScopedMemoryHit
                {
                    Namespace = ns,
                    Id = match.Entry.CommandId,
                    Kind = "command_atlas",
                    Title = match.Entry.DisplayName,
                    Snippet = match.Entry.Description,
                    SourceRef = match.Entry.SourceRef,
                    DocumentKey = request.DocumentKey ?? string.Empty,
                    CreatedUtc = string.Empty,
                    Score = match.Score
                });
            }
        }

        if (namespaces.Contains(MemoryNamespaces.PlaybooksPolicies))
        {
            var playbooks = _context.CopilotTasks.MatchPlaybook(registry.GetToolCatalog(), new PlaybookMatchRequest
            {
                WorkspaceId = workspaceId,
                Query = request.Query,
                MaxResults = 2,
                PreferredCapabilityDomain = ResolveCapabilityDomainFromRequest(request),
                Discipline = request.Discipline
            });

            foreach (var match in playbooks.Matches)
            {
                hits.Add(new ScopedMemoryHit
                {
                    Namespace = MemoryNamespaces.PlaybooksPolicies,
                    Id = match.PlaybookId,
                    Kind = "playbook",
                    Title = match.PlaybookId,
                    Snippet = match.Description,
                    SourceRef = match.PackId,
                    DocumentKey = request.DocumentKey ?? string.Empty,
                    Score = Math.Round(match.Confidence * 100d, 2)
                });
            }

            var policy = _context.CopilotTasks.ResolvePolicy(new PolicyResolutionRequest
            {
                WorkspaceId = workspaceId,
                CapabilityDomain = ResolveCapabilityDomainFromRequest(request),
                Discipline = string.IsNullOrWhiteSpace(request.Discipline) ? CapabilityDisciplines.Common : request.Discipline,
                IssueKinds = string.IsNullOrWhiteSpace(request.IssueKind) ? new List<string>() : new List<string> { request.IssueKind }
            });
            foreach (var pack in policy.ResolvedPacks.Take(2))
            {
                hits.Add(new ScopedMemoryHit
                {
                    Namespace = MemoryNamespaces.PlaybooksPolicies,
                    Id = pack.PackId,
                    Kind = "policy_pack",
                    Title = pack.DisplayName,
                    Snippet = pack.Description,
                    SourceRef = pack.PackId,
                    DocumentKey = request.DocumentKey ?? string.Empty,
                    Score = 70
                });
            }
        }

        if (namespaces.Contains(MemoryNamespaces.ProjectRuntimeMemory) || namespaces.Contains(MemoryNamespaces.EvidenceLessons))
        {
            var similar = _context.CopilotTasks.FindSimilarRuns(new MemoryFindSimilarRunsRequest
            {
                DocumentKey = request.DocumentKey ?? string.Empty,
                Query = request.Query ?? string.Empty,
                TaskKind = ResolveTaskKind(request),
                TaskName = request.CommandFamily ?? string.Empty,
                MaxResults = Math.Min(5, Math.Max(1, request.MaxResults))
            });

            foreach (var run in similar.Runs)
            {
                var ns = string.Equals(run.Status, "verified", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase)
                    ? MemoryNamespaces.EvidenceLessons
                    : MemoryNamespaces.ProjectRuntimeMemory;
                if (!namespaces.Contains(ns))
                {
                    continue;
                }

                hits.Add(new ScopedMemoryHit
                {
                    Namespace = ns,
                    Id = run.RunId,
                    Kind = "task_run",
                    Title = string.IsNullOrWhiteSpace(run.TaskName) ? run.TaskKind : run.TaskName,
                    Snippet = run.Summary,
                    SourceRef = $"task:{run.RunId}",
                    DocumentKey = run.DocumentKey,
                    Score = run.Score
                });
            }
        }

        var ordered = hits
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, request.MaxResults))
            .ToList();

        return new MemoryScopedSearchResponse
        {
            Query = request.Query ?? string.Empty,
            RetrievalScope = string.IsNullOrWhiteSpace(request.RetrievalScope) ? RetrievalScopes.WorkflowPath : request.RetrievalScope,
            Hits = ordered,
            Summary = ordered.Count == 0
                ? "Khong co scoped memory hit nao phu hop."
                : $"Scoped memory resolved {ordered.Count} hit(s) across {string.Join(", ", ordered.Select(x => x.Namespace).Distinct(StringComparer.OrdinalIgnoreCase))}."
        };
    }

    private static IReadOnlyCollection<string> ResolveNamespaces(MemoryScopedSearchRequest request)
    {
        var requested = (request.Namespaces ?? new List<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (requested.Count > 0)
        {
            return requested;
        }

        return (request.RetrievalScope ?? RetrievalScopes.WorkflowPath).ToLowerInvariant() switch
        {
            RetrievalScopes.QuickPath => new[]
            {
                MemoryNamespaces.AtlasNativeCommands,
                MemoryNamespaces.AtlasCustomTools,
                MemoryNamespaces.AtlasCuratedScripts
            },
            RetrievalScopes.DeliveryPath => new[]
            {
                MemoryNamespaces.ProjectRuntimeMemory,
                MemoryNamespaces.EvidenceLessons,
                MemoryNamespaces.PlaybooksPolicies
            },
            _ => new[]
            {
                MemoryNamespaces.AtlasCustomTools,
                MemoryNamespaces.PlaybooksPolicies,
                MemoryNamespaces.ProjectRuntimeMemory
            }
        };
    }

    private static string ResolveAtlasNamespace(CommandAtlasEntry entry)
    {
        if (string.Equals(entry.ExecutionMode, CommandExecutionModes.Script, StringComparison.OrdinalIgnoreCase))
        {
            return MemoryNamespaces.AtlasCuratedScripts;
        }

        return string.Equals(entry.SourceKind, CommandSourceKinds.Revit, StringComparison.OrdinalIgnoreCase)
            || string.Equals(entry.ExecutionMode, CommandExecutionModes.Native, StringComparison.OrdinalIgnoreCase)
            ? MemoryNamespaces.AtlasNativeCommands
            : MemoryNamespaces.AtlasCustomTools;
    }

    private static string ResolveCapabilityDomainFromRequest(MemoryScopedSearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.CommandFamily) && request.CommandFamily.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return CapabilityDomains.Governance;
        }

        if (!string.IsNullOrWhiteSpace(request.IssueKind) && request.IssueKind.IndexOf("clash", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return CapabilityDomains.Coordination;
        }

        return CapabilityDomains.General;
    }

    private static string ResolveTaskKind(MemoryScopedSearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.IssueKind))
        {
            return "fix_loop";
        }

        if (!string.IsNullOrWhiteSpace(request.CommandFamily))
        {
            return "workflow";
        }

        return string.Empty;
    }

    private Document ResolveQuickDocument(Autodesk.Revit.UI.UIApplication uiapp, ToolRequestEnvelope request)
    {
        return _context.Platform.ResolveDocument(uiapp, request.TargetDocument);
    }

    private static void FillQuickContext(Autodesk.Revit.UI.UIApplication uiapp, Document doc, QuickActionRequest request)
    {
        request ??= new QuickActionRequest();
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            request.WorkspaceId = "default";
        }

        if (doc == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.DocumentContext))
        {
            request.DocumentContext = doc.IsFamilyDocument ? "family_document" : "project";
        }

        var activeView = doc.ActiveView;
        if (activeView != null)
        {
            request.ActiveViewId ??= checked((int)activeView.Id.Value);
            request.ActiveViewName = string.IsNullOrWhiteSpace(request.ActiveViewName) ? activeView.Name ?? string.Empty : request.ActiveViewName;
            request.ActiveViewType = string.IsNullOrWhiteSpace(request.ActiveViewType) ? activeView.ViewType.ToString() : request.ActiveViewType;

            if (activeView is ViewPlan viewPlan && viewPlan.GenLevel != null)
            {
                request.CurrentLevelId ??= checked((int)viewPlan.GenLevel.Id.Value);
                request.CurrentLevelName = string.IsNullOrWhiteSpace(request.CurrentLevelName) ? viewPlan.GenLevel.Name ?? string.Empty : request.CurrentLevelName;
            }

            if (activeView is ViewSheet sheet)
            {
                request.CurrentSheetId ??= checked((int)sheet.Id.Value);
                request.CurrentSheetNumber = string.IsNullOrWhiteSpace(request.CurrentSheetNumber) ? sheet.SheetNumber ?? string.Empty : request.CurrentSheetNumber;
            }
        }

        var uiDoc = uiapp.ActiveUIDocument;
        if (uiDoc != null && uiDoc.Document.Equals(doc))
        {
            request.SelectionCount = Math.Max(request.SelectionCount, uiDoc.Selection.GetElementIds().Count);
        }
    }
}
