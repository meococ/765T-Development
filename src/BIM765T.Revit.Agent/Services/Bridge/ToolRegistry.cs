using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure.Bridge.Workflows;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolRegistration> _registrations = new Dictionary<string, ToolRegistration>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WorkflowRegistration> _workflows = new Dictionary<string, WorkflowRegistration>(StringComparer.OrdinalIgnoreCase);
    private readonly PlatformServices _platform;
    private readonly MutationToolPipeline _mutationPipeline;

    internal ToolRegistry(
        PlatformBundle platformBundle,
        InspectionBundle inspectionBundle,
        HullBundle hullBundle,
        WorkflowBundle workflowBundle,
        CopilotBundle copilotBundle)
    {
        _platform = platformBundle.Platform;
        _mutationPipeline = new MutationToolPipeline(platformBundle.Platform);

        var context = new ToolModuleContext(
            platformBundle,
            inspectionBundle,
            hullBundle,
            workflowBundle,
            copilotBundle);

        IToolModule[] modules =
        {
            new WorkerToolModule(context),
            new SessionDocumentToolModule(context),
            new ViewAnnotationAndTypeToolModule(context),
            new ParameterToolModule(context),
            new DataLifecycleToolModule(context),
            new AuditCenterToolModule(context),
            new ElementAndReviewToolModule(context),
            new PenetrationWorkflowToolModule(context),
            new MutationFileAndDomainToolModule(context),
            new WorkflowInspectorToolModule(context),
            new SheetViewToolModule(context),
            new IntelligenceToolModule(context),
            new FixLoopToolModule(context),
            new DeliveryOpsToolModule(context),
            new CopilotTaskToolModule(context),
            new CommandAtlasToolModule(context),
            new QueryPerformanceToolModule(context),
            new SpatialIntelligenceToolModule(context),
            new FamilyAuthoringToolModule(context),
            new ScriptOrchestrationToolModule(context)
        };

        foreach (var module in modules)
        {
            module.Register(this);
        }
    }

    internal bool TryGet(string toolName, out ToolRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            registration = null!;
            return false;
        }

        return _registrations.TryGetValue(toolName, out registration!);
    }

    internal bool TryGetWorkflow(string toolName, out WorkflowRegistration workflow)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            workflow = null!;
            return false;
        }

        return _workflows.TryGetValue(toolName, out workflow!);
    }

    internal void RegisterWorkflow(
        string toolName,
        string description,
        PermissionLevel permissionLevel,
        ApprovalRequirement approvalRequirement,
        bool supportsDryRun,
        string inputSchemaHint,
        ToolManifestMetadata metadata,
        Func<ToolRequestEnvelope, WorkflowSetup> factory)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return;
        }

        _workflows[toolName] = new WorkflowRegistration
        {
            Manifest = CreateManifest(toolName, description, permissionLevel, approvalRequirement, supportsDryRun, inputSchemaHint, metadata),
            Factory = factory
        };

        if (!_registrations.ContainsKey(toolName))
        {
            _registrations[toolName] = new ToolRegistration
            {
                Manifest = _workflows[toolName].Manifest,
                Handler = (_, request) => throw new InvalidOperationException($"Tool '{toolName}' is workflow-based. Use workflow execution path.")
            };
        }
    }

    internal List<ToolManifest> GetToolCatalog()
    {
        return GetToolCatalog(ToolCatalogFilter.ToolCatalogAudience.WorkerUi);
    }

    internal List<ToolManifest> GetToolCatalog(ToolCatalogFilter.ToolCatalogAudience audience)
    {
        return FilterToolCatalog(CloneToolCatalog(_registrations.Values.Select(registration => registration.Manifest)), audience);
    }

    internal static List<ToolManifest> FilterToolCatalog(IEnumerable<ToolManifest> manifests, ToolCatalogFilter.ToolCatalogAudience audience)
    {
        return audience switch
        {
            ToolCatalogFilter.ToolCatalogAudience.Mcp => ToolCatalogFilter.FilterForMcp(manifests),
            ToolCatalogFilter.ToolCatalogAudience.PublicCatalog => ToolCatalogFilter.FilterForPublicCatalog(manifests),
            _ => ToolCatalogFilter.FilterForSessionList(manifests)
        };
    }

    internal static List<ToolManifest> CloneToolCatalog(IEnumerable<ToolManifest> manifests)
    {
        return (manifests ?? Enumerable.Empty<ToolManifest>())
            .Where(manifest => manifest != null)
            .Select(CloneManifest)
            .OrderBy(manifest => manifest.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal void RegisterMutationTool<TPayload>(
        string toolName,
        string description,
        ApprovalRequirement approvalRequirement,
        string inputSchemaHint,
        ToolManifestMetadata metadata,
        Func<bool> isEnabled,
        string disabledStatusCode,
        Func<UIApplication, ToolRequestEnvelope, TPayload, Autodesk.Revit.DB.Document> resolveDocument,
        Action<Autodesk.Revit.DB.Document, TPayload>? normalizePayload,
        Func<UIApplication, PlatformServices, Autodesk.Revit.DB.Document, TPayload, ToolRequestEnvelope, ExecutionResult> preview,
        Func<UIApplication, PlatformServices, Autodesk.Revit.DB.Document, TPayload, ExecutionResult> execute)
    {
        Register(
            toolName,
            description,
            PermissionLevel.Mutate,
            approvalRequirement,
            true,
            metadata,
            _mutationPipeline.BuildHandler(isEnabled, disabledStatusCode, resolveDocument, normalizePayload, preview, execute),
            inputSchemaHint);
    }

    internal void Register(
        string toolName,
        string description,
        PermissionLevel permissionLevel,
        ApprovalRequirement approvalRequirement,
        bool supportsDryRun,
        ToolManifestMetadata metadata,
        Func<UIApplication, ToolRequestEnvelope, ToolResponseEnvelope> handler,
        string inputSchemaHint = "")
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            throw new ArgumentException("Tool name must be provided.", nameof(toolName));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Tool description must be provided.", nameof(description));
        }

        if (metadata == null)
        {
            throw new ArgumentNullException(nameof(metadata));
        }

        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        var effectiveApproval = approvalRequirement;
        if (_platform.Policy.HighRiskTools != null && _platform.Policy.HighRiskTools.Exists(x => string.Equals(x, toolName, StringComparison.OrdinalIgnoreCase)))
        {
            effectiveApproval = ApprovalRequirement.HighRiskToken;
        }

        if (_registrations.ContainsKey(toolName))
        {
            throw new InvalidOperationException("Tool already registered: " + toolName);
        }

        _registrations[toolName] = new ToolRegistration
        {
            Manifest = CreateManifest(toolName, description, permissionLevel, effectiveApproval, supportsDryRun, inputSchemaHint, metadata),
            Handler = handler
        };
    }

    private ToolManifest CreateManifest(string toolName, string description, PermissionLevel permissionLevel, ApprovalRequirement approvalRequirement, bool supportsDryRun, string inputSchemaHint, ToolManifestMetadata metadata)
    {
        return ToolManifestFactory.Create(
            toolName,
            description,
            permissionLevel,
            approvalRequirement,
            supportsDryRun,
            _platform.IsToolEnabled(toolName, permissionLevel, metadata),
            inputSchemaHint ?? string.Empty,
            BuildInputSchemaJson(toolName, permissionLevel, supportsDryRun, inputSchemaHint ?? string.Empty),
            metadata,
            _platform.Settings);
    }

    private static ToolManifest CloneManifest(ToolManifest manifest)
    {
        return new ToolManifest
        {
            ToolName = manifest.ToolName,
            Description = manifest.Description,
            PermissionLevel = manifest.PermissionLevel,
            ApprovalRequirement = manifest.ApprovalRequirement,
            SupportsDryRun = manifest.SupportsDryRun,
            Enabled = manifest.Enabled,
            InputSchemaHint = manifest.InputSchemaHint,
            RequiredContext = new List<string>(manifest.RequiredContext),
            MutatesModel = manifest.MutatesModel,
            TouchesActiveView = manifest.TouchesActiveView,
            RequiresExpectedContext = manifest.RequiresExpectedContext,
            BatchMode = manifest.BatchMode,
            Idempotency = manifest.Idempotency,
            PreviewArtifacts = new List<string>(manifest.PreviewArtifacts),
            RiskTags = new List<string>(manifest.RiskTags),
            RulePackTags = new List<string>(manifest.RulePackTags),
            InputSchemaJson = manifest.InputSchemaJson,
            ExecutionTimeoutMs = manifest.ExecutionTimeoutMs,
            CapabilityPack = manifest.CapabilityPack,
            SkillGroup = manifest.SkillGroup,
            Audience = manifest.Audience,
            Visibility = manifest.Visibility,
            RiskTier = manifest.RiskTier,
            CanAutoExecute = manifest.CanAutoExecute,
            LatencyClass = manifest.LatencyClass,
            UiSurface = manifest.UiSurface,
            ProgressMode = manifest.ProgressMode,
            RecommendedNextTools = new List<string>(manifest.RecommendedNextTools),
            DomainGroup = manifest.DomainGroup,
            TaskFamily = manifest.TaskFamily,
            PackId = manifest.PackId,
            RecommendedPlaybooks = new List<string>(manifest.RecommendedPlaybooks),
            CapabilityDomain = manifest.CapabilityDomain,
            DeterminismLevel = manifest.DeterminismLevel,
            RequiresPolicyPack = manifest.RequiresPolicyPack,
            VerificationMode = manifest.VerificationMode,
            SupportedDisciplines = new List<string>(manifest.SupportedDisciplines),
            IssueKinds = new List<string>(manifest.IssueKinds),
            CommandFamily = manifest.CommandFamily,
            ExecutionMode = manifest.ExecutionMode,
            NativeCommandId = manifest.NativeCommandId,
            SourceKind = manifest.SourceKind,
            SourceRef = manifest.SourceRef,
            SafetyClass = manifest.SafetyClass,
            CanPreview = manifest.CanPreview,
            CoverageTier = manifest.CoverageTier,
            FallbackEntryIds = new List<string>(manifest.FallbackEntryIds),
            PrimaryPersona = manifest.PrimaryPersona,
            UserValueClass = manifest.UserValueClass,
            RepeatabilityClass = manifest.RepeatabilityClass,
            AutomationStage = manifest.AutomationStage,
            CanTeachBack = manifest.CanTeachBack,
            FallbackArtifactKinds = new List<string>(manifest.FallbackArtifactKinds),
            CommercialTier = manifest.CommercialTier,
            CacheValueClass = manifest.CacheValueClass
        };
    }

    private static string BuildInputSchemaJson(string toolName, PermissionLevel permissionLevel, bool supportsDryRun, string inputSchemaHint)
    {
        var properties = new List<string>
        {
            "\"target_document\":{\"type\":\"string\"}",
            "\"target_view\":{\"type\":\"string\"}",
            "\"correlation_id\":{\"type\":\"string\"}",
            "\"payload\":{\"type\":\"object\"}"
        };

        if (supportsDryRun || permissionLevel == PermissionLevel.Mutate || permissionLevel == PermissionLevel.FileLifecycle)
        {
            properties.Add("\"dry_run\":{\"type\":\"boolean\"}");
            properties.Add("\"approval_token\":{\"type\":\"string\"}");
            properties.Add("\"preview_run_id\":{\"type\":\"string\"}");
            properties.Add("\"expected_context\":{\"type\":\"object\"}");
            properties.Add("\"scope_descriptor\":{\"type\":\"object\"}");
        }

        if (toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase))
        {
            properties.Add("\"run_id\":{\"type\":\"string\"}");
        }

        if (!string.IsNullOrWhiteSpace(inputSchemaHint))
        {
            properties.Add("\"payload_example\":{\"type\":\"string\",\"description\":" + JsonUtil.Serialize(inputSchemaHint) + "}");
        }

        return "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{" + string.Join(",", properties) + "}}";
    }
}

internal sealed class ToolRegistration
{
    internal ToolManifest Manifest { get; set; } = new ToolManifest();
    internal Func<UIApplication, ToolRequestEnvelope, ToolResponseEnvelope> Handler { get; set; } = (_, request) => ToolResponses.Failure(request, StatusCodes.InternalError);
}

internal sealed class WorkflowRegistration
{
    internal ToolManifest Manifest { get; set; } = new ToolManifest();
    internal Func<ToolRequestEnvelope, WorkflowSetup> Factory { get; set; } = _ => throw new InvalidOperationException("Workflow factory not configured.");
}
