using System;
using System.Collections.Generic;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Copilot.Core;
using BIM765T.Revit.Contracts.Serialization;
using BIM765T.Revit.WorkerHost.Configuration;
using BIM765T.Revit.WorkerHost.Kernel;
using BIM765T.Revit.WorkerHost.Memory;
using ContractStatusCodes = BIM765T.Revit.Contracts.Common.StatusCodes;
using System.Linq;

namespace BIM765T.Revit.WorkerHost.Capabilities;

internal sealed class CapabilityHostService
{
    private readonly PolicyResolutionService _policies;
    private readonly SpecialistRegistryService _specialists;
    private readonly CapabilityTaskCompilerService _compiler;
    private readonly CommandAtlasService _commandAtlas;
    private readonly CuratedScriptRegistryService _curatedScripts;
    private readonly MemorySearchService _memorySearch;
    private readonly IKernelClient _kernel;
    private readonly WorkerHostSettings _settings;
    private readonly IReadOnlyList<ToolManifest> _catalog;

    public CapabilityHostService(
        PolicyResolutionService policies,
        SpecialistRegistryService specialists,
        CapabilityTaskCompilerService compiler,
        CommandAtlasService commandAtlas,
        CuratedScriptRegistryService curatedScripts,
        MemorySearchService memorySearch,
        IKernelClient kernel,
        WorkerHostSettings settings)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _specialists = specialists ?? throw new ArgumentNullException(nameof(specialists));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _commandAtlas = commandAtlas ?? throw new ArgumentNullException(nameof(commandAtlas));
        _curatedScripts = curatedScripts ?? throw new ArgumentNullException(nameof(curatedScripts));
        _memorySearch = memorySearch ?? throw new ArgumentNullException(nameof(memorySearch));
        _kernel = kernel ?? throw new ArgumentNullException(nameof(kernel));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _catalog = BuildCatalog();
    }

    public PolicyResolution ResolvePolicy(PolicyResolutionRequest request)
    {
        request ??= new PolicyResolutionRequest();
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            request.WorkspaceId = "default";
        }

        return _policies.Resolve(request);
    }

    public CapabilitySpecialistResponse ResolveSpecialists(CapabilitySpecialistRequest request)
    {
        request ??= new CapabilitySpecialistRequest();
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            request.WorkspaceId = "default";
        }

        return _specialists.Resolve(request);
    }

    public CompiledTaskPlan Compile(IntentCompileRequest request)
    {
        request ??= new IntentCompileRequest();
        request.Task ??= new IntentTask();
        if (string.IsNullOrWhiteSpace(request.Task.WorkspaceId))
        {
            request.Task.WorkspaceId = "default";
        }

        return _compiler.Compile(_catalog, request);
    }

    public IntentValidationResponse Validate(IntentValidateRequest request)
    {
        request ??= new IntentValidateRequest();
        return _compiler.Validate(_catalog, request);
    }

    public IReadOnlyList<ToolManifest> GetCatalog()
    {
        return _catalog;
    }

    public CommandAtlasSearchResponse SearchCommands(CommandAtlasSearchRequest request)
    {
        request ??= new CommandAtlasSearchRequest();
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            request.WorkspaceId = "default";
        }

        return _commandAtlas.Search(_catalog, request);
    }

    public CommandAtlasEntry DescribeCommand(CommandDescribeRequest request)
    {
        request ??= new CommandDescribeRequest();
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            request.WorkspaceId = "default";
        }

        return _commandAtlas.Describe(_catalog, request);
    }

    public CoverageReportResponse GetCoverageReport(CoverageReportRequest request)
    {
        request ??= new CoverageReportRequest();
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            request.WorkspaceId = "default";
        }

        return _commandAtlas.BuildCoverageReport(_catalog, request);
    }

    public QuickActionResponse QuickPlan(QuickActionRequest request)
    {
        request ??= new QuickActionRequest();
        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            request.WorkspaceId = "default";
        }

        return _commandAtlas.PlanQuickAction(_catalog, request);
    }

    public System.Threading.Tasks.Task<MemoryScopedSearchResponse> SearchScopedMemoryAsync(MemoryScopedSearchRequest request, System.Threading.CancellationToken cancellationToken)
    {
        request ??= new MemoryScopedSearchRequest();
        return _memorySearch.SearchScopedAsync(request, cancellationToken);
    }

    public ScriptSourceVerifyResponse VerifyScriptSource(ScriptSourceVerifyRequest request)
    {
        request ??= new ScriptSourceVerifyRequest();
        return _curatedScripts.Verify(request.Manifest);
    }

    public ScriptImportManifestResponse ImportScriptManifest(ScriptImportManifestRequest request)
    {
        request ??= new ScriptImportManifestRequest();
        return _curatedScripts.Import(request);
    }

    public ScriptInstallPackResponse InstallScriptPack(ScriptInstallPackRequest request)
    {
        request ??= new ScriptInstallPackRequest();
        return _curatedScripts.InstallPack(request);
    }

    public ScriptListResponse GetScriptCatalog(string? workspaceId)
    {
        var atlas = _commandAtlas.BuildAtlas(_catalog, string.IsNullOrWhiteSpace(workspaceId) ? "default" : workspaceId!);
        return new ScriptListResponse
        {
            CatalogPath = _curatedScripts.ManifestRootPath,
            Scripts = atlas
                .Where(x => string.Equals(x.ExecutionMode, CommandExecutionModes.Script, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(x => new ScriptCatalogEntry
                {
                    ScriptId = x.CommandId,
                    FileName = x.SourceRef,
                    Description = x.Description,
                    Language = x.SourceKind switch
                    {
                        var kind when string.Equals(kind, CommandSourceKinds.DynamoOrchid, StringComparison.OrdinalIgnoreCase) => "dynamo",
                        var kind when string.Equals(kind, CommandSourceKinds.PyRevit, StringComparison.OrdinalIgnoreCase) => "python",
                        _ => "manifest"
                    },
                    Tags = x.Tags?.ToList() ?? new List<string>(),
                    ContentHash = $"{x.SourceKind}:{x.CoverageStatus}:{x.CoverageTier}"
                })
                .ToList()
        };
    }

    public System.Threading.Tasks.Task<MemoryScopedSearchResponse> LookupEvidenceBundlesAsync(MemoryScopedSearchRequest request, System.Threading.CancellationToken cancellationToken)
    {
        request ??= new MemoryScopedSearchRequest();
        request.RetrievalScope = RetrievalScopes.DeliveryPath;
        request.Namespaces = new List<string>
        {
            MemoryNamespaces.EvidenceLessons,
            MemoryNamespaces.ProjectRuntimeMemory
        };
        return _memorySearch.SearchScopedAsync(request, cancellationToken);
    }

    public async System.Threading.Tasks.Task<CommandExecuteResponse> ExecuteCommandAsync(CommandExecuteRequest request, System.Threading.CancellationToken cancellationToken)
    {
        request ??= new CommandExecuteRequest();
        if (!_settings.EnableDirectCommandExecuteHttp)
        {
            return new CommandExecuteResponse
            {
                StatusCode = ContractStatusCodes.CommandExecutionBlocked,
                Summary = "Direct HTTP command execution is disabled. Use the mission or external-ai flow instead."
            };
        }

        if (string.IsNullOrWhiteSpace(request.WorkspaceId))
        {
            request.WorkspaceId = "default";
        }

        var quick = _commandAtlas.PlanQuickAction(_catalog, new QuickActionRequest
        {
            WorkspaceId = request.WorkspaceId,
            Query = string.IsNullOrWhiteSpace(request.Query) ? request.CommandId : request.Query
        });
        var entry = !string.IsNullOrWhiteSpace(request.CommandId)
            ? _commandAtlas.Describe(_catalog, new CommandDescribeRequest { WorkspaceId = request.WorkspaceId, CommandId = request.CommandId, Query = request.Query })
            : quick.MatchedEntry;

        var toolName = string.IsNullOrWhiteSpace(quick.PlannedToolName)
            ? (string.Equals(entry.ExecutionMode, CommandExecutionModes.Tool, StringComparison.OrdinalIgnoreCase) ? entry.SourceRef : string.Empty)
            : quick.PlannedToolName;
        var payloadJson = !string.IsNullOrWhiteSpace(request.PayloadJson) ? request.PayloadJson : quick.ResolvedPayloadJson;

        if (string.IsNullOrWhiteSpace(toolName))
        {
            return new CommandExecuteResponse
            {
                StatusCode = string.Equals(entry.ExecutionMode, CommandExecutionModes.Native, StringComparison.OrdinalIgnoreCase)
                    ? ContractStatusCodes.CommandCoverageIncomplete
                    : ContractStatusCodes.CommandExecutionBlocked,
                Summary = string.Equals(entry.ExecutionMode, CommandExecutionModes.Native, StringComparison.OrdinalIgnoreCase)
                    ? "Native command is mapped in the atlas but not yet executable through WorkerHost."
                    : "No executable tool lane resolved for this command.",
                Entry = entry
            };
        }

        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new CommandExecuteResponse
            {
                StatusCode = ContractStatusCodes.CommandContextMissing,
                Summary = quick.Summary,
                Entry = entry,
                ToolName = toolName
            };
        }

        var kernelRequest = new KernelToolRequest
        {
            CorrelationId = Guid.NewGuid().ToString("N"),
            ToolName = toolName,
            PayloadJson = payloadJson,
            Caller = "workerhost.command.execute",
            DryRun = !request.AllowAutoExecute || entry.CanPreview || entry.NeedsApproval,
            TargetDocument = request.TargetDocument ?? string.Empty,
            TargetView = request.TargetView ?? string.Empty,
            DocumentKey = request.TargetDocument ?? string.Empty,
            RequestedAtUtc = DateTime.UtcNow.ToString("O")
        };
        var result = await _kernel.InvokeAsync(kernelRequest, cancellationToken).ConfigureAwait(false);

        return new CommandExecuteResponse
        {
            StatusCode = result.StatusCode,
            Summary = result.StatusCode,
            ToolName = toolName,
            RequestPayloadJson = payloadJson,
            Entry = entry,
            ConfirmationRequired = string.Equals(result.StatusCode, ContractStatusCodes.ConfirmationRequired, StringComparison.OrdinalIgnoreCase),
            ApprovalToken = result.ApprovalToken ?? string.Empty,
            PreviewRunId = result.PreviewRunId ?? string.Empty,
            ToolResponseJson = result.PayloadJson ?? string.Empty
        };
    }

    private static IReadOnlyList<ToolManifest> BuildCatalog()
    {
        return new List<ToolManifest>
        {
            CreateManifest(ToolNames.ToolGetGuidance, CapabilityDomains.General, ToolDeterminismLevels.Deterministic, ToolVerificationModes.ReportOnly),
            CreateManifest(ToolNames.PlaybookMatch, CapabilityDomains.General, ToolDeterminismLevels.Deterministic, ToolVerificationModes.ReportOnly),
            CreateManifest(ToolNames.PlaybookPreview, CapabilityDomains.General, ToolDeterminismLevels.Deterministic, ToolVerificationModes.ReportOnly),
            CreateManifest(ToolNames.WorkspaceGetManifest, CapabilityDomains.General, ToolDeterminismLevels.Deterministic, ToolVerificationModes.ReportOnly),
            CreateManifest(ToolNames.StandardsResolve, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.NamingConvention }),
            CreateManifest(ToolNames.PolicyResolve, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true),
            CreateManifest(ToolNames.SpecialistResolve, CapabilityDomains.Intent, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.ReportOnly),
            CreateManifest(ToolNames.IntentCompile, CapabilityDomains.Intent, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, false, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.IntentCompile }),
            CreateManifest(ToolNames.IntentValidate, CapabilityDomains.Intent, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, false, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.IntentCompile }),
            CreateManifest(ToolNames.CommandSearch, CapabilityDomains.Intent, ToolDeterminismLevels.Deterministic, ToolVerificationModes.ReportOnly, false, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.IntentCompile }),
            CreateManifest(ToolNames.CommandDescribe, CapabilityDomains.Intent, ToolDeterminismLevels.Deterministic, ToolVerificationModes.ReportOnly, false, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.IntentCompile }),
            CreateManifest(ToolNames.CommandCoverageReport, CapabilityDomains.Intent, ToolDeterminismLevels.Deterministic, ToolVerificationModes.ReportOnly, false, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.IntentCompile }),
            CreateManifest(ToolNames.WorkflowQuickPlan, CapabilityDomains.Intent, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, false, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.IntentCompile }),
            CreateManifest(ToolNames.CommandExecuteSafe, CapabilityDomains.Intent, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, false, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.IntentCompile }, PermissionLevel.Mutate),
            CreateManifest(ToolNames.SheetCreateSafe, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Architecture, CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.SheetPackage, CapabilityIssueKinds.NamingConvention }, PermissionLevel.Mutate),
            CreateManifest(ToolNames.SheetPlaceViewsSafe, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Architecture, CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.SheetPackage, CapabilityIssueKinds.NamingConvention }, PermissionLevel.Mutate),
            CreateManifest(ToolNames.SheetRenumberSafe, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Architecture, CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.SheetPackage, CapabilityIssueKinds.NamingConvention }, PermissionLevel.Mutate),
            CreateManifest(ToolNames.ViewCreateProjectViewSafe, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Architecture, CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.SheetPackage }, PermissionLevel.Mutate),
            CreateManifest(ToolNames.ViewCreate3dSafe, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Architecture, CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.SheetPackage }, PermissionLevel.Mutate),
            CreateManifest(ToolNames.ViewDuplicateSafe, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Architecture, CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.SheetPackage }, PermissionLevel.Mutate),
            CreateManifest(ToolNames.ViewSetTemplateSafe, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Architecture, CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.NamingConvention }, PermissionLevel.Mutate),
            CreateManifest(ToolNames.AnnotationAddTextNoteSafe, CapabilityDomains.Annotation, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Architecture, CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.TagOverlap }, PermissionLevel.Mutate),
            CreateManifest(ToolNames.ParameterBatchFillSafe, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Common, CapabilityDisciplines.Mep }, new[] { CapabilityIssueKinds.ParameterPopulation }, PermissionLevel.Mutate),
            CreateManifest(ToolNames.DataPreviewImport, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.ParameterPopulation }),
            CreateManifest(ToolNames.DataImportSafe, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.ParameterPopulation }, PermissionLevel.Mutate),
            CreateManifest(ToolNames.ReviewSmartQc, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.WarningTriage, CapabilityIssueKinds.LodLoiCompliance }),
            CreateManifest(ToolNames.AuditPurgeUnusedSafe, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.ModelCleanup }, PermissionLevel.Mutate),
            CreateManifest(ToolNames.FamilyXray, CapabilityDomains.FamilyQa, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.FamilyQa }),
            CreateManifest(ToolNames.ReviewParameterCompleteness, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Common, CapabilityDisciplines.Mep }, new[] { CapabilityIssueKinds.LodLoiCompliance }),
            CreateManifest(ToolNames.ReviewWorksetHealth, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.WarningTriage }),
            CreateManifest(ToolNames.WorksetBulkReassignElementsSafe, CapabilityDomains.Governance, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.PolicyCheck, true, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.WarningTriage }, PermissionLevel.Mutate),
            CreateManifest(ToolNames.SpatialClashDetect, CapabilityDomains.Coordination, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.GeometryCheck, true, new[] { CapabilityDisciplines.Mep, CapabilityDisciplines.Structure, CapabilityDisciplines.Architecture }, new[] { CapabilityIssueKinds.HardClash, CapabilityIssueKinds.ClearanceSoftClash }),
            CreateManifest(ToolNames.SpatialOpeningDetect, CapabilityDomains.Coordination, ToolDeterminismLevels.PolicyBacked, ToolVerificationModes.GeometryCheck, true, new[] { CapabilityDisciplines.Mep, CapabilityDisciplines.Structure }, new[] { CapabilityIssueKinds.HardClash }),
            CreateManifest(ToolNames.SystemCaptureGraph, CapabilityDomains.Systems, ToolDeterminismLevels.Scaffold, ToolVerificationModes.SystemConsistency, true, new[] { CapabilityDisciplines.Mep, CapabilityDisciplines.Mechanical, CapabilityDisciplines.Plumbing, CapabilityDisciplines.Electrical }, new[] { CapabilityIssueKinds.DisconnectedSystem, CapabilityIssueKinds.SlopeContinuity, CapabilityIssueKinds.BasicRouting }),
            CreateManifest(ToolNames.SystemPlanConnectivityFix, CapabilityDomains.Systems, ToolDeterminismLevels.Scaffold, ToolVerificationModes.SystemConsistency, true, new[] { CapabilityDisciplines.Mep, CapabilityDisciplines.Mechanical, CapabilityDisciplines.Plumbing, CapabilityDisciplines.Electrical }, new[] { CapabilityIssueKinds.DisconnectedSystem, CapabilityIssueKinds.BasicRouting }),
            CreateManifest(ToolNames.SystemPlanSlopeRemediation, CapabilityDomains.Systems, ToolDeterminismLevels.Scaffold, ToolVerificationModes.SystemConsistency, true, new[] { CapabilityDisciplines.Plumbing, CapabilityDisciplines.Mep }, new[] { CapabilityIssueKinds.SlopeContinuity }),
            CreateManifest(ToolNames.IntegrationPreviewSync, CapabilityDomains.Integration, ToolDeterminismLevels.Scaffold, ToolVerificationModes.ReportOnly, true, new[] { CapabilityDisciplines.Common }, new[] { CapabilityIssueKinds.ExternalSync, CapabilityIssueKinds.ScanToBim, CapabilityIssueKinds.LargeModelSplit })
        };
    }

    private static ToolManifest CreateManifest(
        string toolName,
        string capabilityDomain,
        string determinismLevel,
        string verificationMode,
        bool requiresPolicyPack = false,
        IEnumerable<string>? supportedDisciplines = null,
        IEnumerable<string>? issueKinds = null,
        PermissionLevel permissionLevel = PermissionLevel.Read)
    {
        return new ToolManifest
        {
            ToolName = toolName,
            Description = toolName,
            PermissionLevel = permissionLevel,
            CapabilityDomain = capabilityDomain,
            DeterminismLevel = determinismLevel,
            VerificationMode = verificationMode,
            RequiresPolicyPack = requiresPolicyPack,
            SupportedDisciplines = supportedDisciplines != null ? new List<string>(supportedDisciplines) : new List<string> { CapabilityDisciplines.Common },
            IssueKinds = issueKinds != null ? new List<string>(issueKinds) : new List<string>(),
            PackId = capabilityDomain switch
            {
                var x when string.Equals(x, CapabilityDomains.Coordination, StringComparison.OrdinalIgnoreCase) => "bim765t.playbooks.coordination",
                var x when string.Equals(x, CapabilityDomains.Systems, StringComparison.OrdinalIgnoreCase) => "bim765t.playbooks.systems",
                var x when string.Equals(x, CapabilityDomains.Annotation, StringComparison.OrdinalIgnoreCase) => "bim765t.playbooks.annotation",
                var x when string.Equals(x, CapabilityDomains.FamilyQa, StringComparison.OrdinalIgnoreCase) => "bim765t.playbooks.family-qa",
                var x when string.Equals(x, CapabilityDomains.Integration, StringComparison.OrdinalIgnoreCase) => "bim765t.skills.integration",
                var x when string.Equals(x, CapabilityDomains.Intent, StringComparison.OrdinalIgnoreCase) => "bim765t.skills.intent",
                _ => "bim765t.playbooks.governance"
            }
        };
    }
}
