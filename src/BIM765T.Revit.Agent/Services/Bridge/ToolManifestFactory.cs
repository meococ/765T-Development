using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal static class ToolManifestFactory
{
    internal static ToolManifest Create(
        string toolName,
        string description,
        PermissionLevel permissionLevel,
        ApprovalRequirement approvalRequirement,
        bool supportsDryRun,
        bool enabled,
        string inputSchemaHint,
        string inputSchemaJson,
        ToolManifestMetadata metadata,
        AgentSettings settings)
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

        if (string.IsNullOrWhiteSpace(metadata.BatchMode))
        {
            throw new ArgumentException("Tool metadata must define BatchMode.", nameof(metadata));
        }

        if (string.IsNullOrWhiteSpace(metadata.Idempotency))
        {
            throw new ArgumentException("Tool metadata must define Idempotency.", nameof(metadata));
        }

        var riskTags = new List<string>(metadata.RiskTags);
        if ((permissionLevel == PermissionLevel.Mutate || permissionLevel == PermissionLevel.FileLifecycle)
            && !riskTags.Exists(x => string.Equals(x, "mutation", StringComparison.OrdinalIgnoreCase)))
        {
            riskTags.Add("mutation");
        }

        if (approvalRequirement == ApprovalRequirement.HighRiskToken
            && !riskTags.Exists(x => string.Equals(x, "high_risk", StringComparison.OrdinalIgnoreCase)))
        {
            riskTags.Add("high_risk");
        }

        var product = WorkerProductClassifier.Classify(
            toolName,
            permissionLevel,
            metadata.CapabilityPack,
            metadata.SkillGroup,
            metadata.Audience,
            metadata.Visibility);

        var riskTier = ResolveRiskTier(permissionLevel, approvalRequirement, riskTags, metadata);
        var latencyClass = ResolveLatencyClass(toolName, metadata, settings);
        var progressMode = ResolveProgressMode(metadata, latencyClass);
        var uiSurface = ResolveUiSurface(toolName, permissionLevel, approvalRequirement, metadata);
        var canAutoExecute = ResolveAutoExecute(permissionLevel, approvalRequirement, riskTier, metadata);
        var recommendedNextTools = ResolveRecommendedNextTools(toolName, permissionLevel, approvalRequirement, metadata);
        var domainGroup = ResolveDomainGroup(toolName, metadata);
        var taskFamily = ResolveTaskFamily(toolName, metadata);
        var capabilityDomain = ResolveCapabilityDomain(toolName, metadata, domainGroup, taskFamily);
        var determinismLevel = ResolveDeterminismLevel(toolName, metadata, capabilityDomain);
        var requiresPolicyPack = ResolveRequiresPolicyPack(toolName, metadata, capabilityDomain, domainGroup);
        var verificationMode = ResolveVerificationMode(toolName, metadata, capabilityDomain);
        var supportedDisciplines = ResolveSupportedDisciplines(toolName, metadata, capabilityDomain);
        var issueKinds = ResolveIssueKinds(toolName, metadata, capabilityDomain, domainGroup, taskFamily);
        var packId = ResolvePackId(metadata, product.CapabilityPack, domainGroup);
        var recommendedPlaybooks = ResolveRecommendedPlaybooks(toolName, metadata, domainGroup, taskFamily);
        var commandFamily = ResolveCommandFamily(metadata, domainGroup, taskFamily);
        var executionMode = ResolveExecutionMode(metadata);
        var sourceKind = ResolveSourceKind(metadata, executionMode);
        var sourceRef = ResolveSourceRef(toolName, metadata, executionMode);
        var safetyClass = ResolveSafetyClass(permissionLevel, approvalRequirement, metadata);
        var canPreview = ResolveCanPreview(supportsDryRun, permissionLevel, metadata);
        var coverageTier = ResolveCoverageTier(metadata);
        var primaryPersona = ResolvePrimaryPersona(toolName, metadata, capabilityDomain, taskFamily);
        var userValueClass = ResolveUserValueClass(toolName, metadata, domainGroup, taskFamily);
        var repeatabilityClass = ResolveRepeatabilityClass(toolName, metadata, permissionLevel, userValueClass);
        var automationStage = ResolveAutomationStage(toolName, metadata, domainGroup, executionMode);
        var canTeachBack = ResolveCanTeachBack(toolName, metadata, repeatabilityClass, automationStage, userValueClass);
        var fallbackArtifactKinds = ResolveFallbackArtifactKinds(toolName, metadata, domainGroup, taskFamily, capabilityDomain, automationStage);
        var commercialTier = ResolveCommercialTier(metadata, product, permissionLevel, capabilityDomain, automationStage, userValueClass, fallbackArtifactKinds);
        var cacheValueClass = ResolveCacheValueClass(metadata, toolName, permissionLevel, canTeachBack, automationStage, fallbackArtifactKinds);

        return new ToolManifest
        {
            ToolName = toolName,
            Description = description,
            PermissionLevel = permissionLevel,
            ApprovalRequirement = approvalRequirement,
            SupportsDryRun = supportsDryRun,
            Enabled = enabled,
            InputSchemaHint = inputSchemaHint ?? string.Empty,
            RequiredContext = new List<string>(metadata.RequiredContext),
            MutatesModel = permissionLevel == PermissionLevel.Mutate || permissionLevel == PermissionLevel.FileLifecycle,
            TouchesActiveView = metadata.TouchesActiveView,
            RequiresExpectedContext = permissionLevel == PermissionLevel.Mutate || permissionLevel == PermissionLevel.FileLifecycle,
            BatchMode = metadata.BatchMode,
            Idempotency = metadata.Idempotency,
            PreviewArtifacts = new List<string>(metadata.PreviewArtifacts),
            RiskTags = riskTags,
            RulePackTags = new List<string>(metadata.RulePackTags),
            InputSchemaJson = inputSchemaJson ?? string.Empty,
            ExecutionTimeoutMs = metadata.ExecutionTimeoutMs ?? ToolExecutionTimeoutPolicy.GetRecommendedTimeoutMs(settings, toolName),
            CapabilityPack = product.CapabilityPack,
            SkillGroup = product.SkillGroup,
            Audience = product.Audience,
            Visibility = product.Visibility,
            RiskTier = riskTier,
            CanAutoExecute = canAutoExecute,
            LatencyClass = latencyClass,
            UiSurface = uiSurface,
            ProgressMode = progressMode,
            RecommendedNextTools = recommendedNextTools,
            DomainGroup = domainGroup,
            TaskFamily = taskFamily,
            PackId = packId,
            RecommendedPlaybooks = recommendedPlaybooks,
            CapabilityDomain = capabilityDomain,
            DeterminismLevel = determinismLevel,
            RequiresPolicyPack = requiresPolicyPack,
            VerificationMode = verificationMode,
            SupportedDisciplines = supportedDisciplines,
            IssueKinds = issueKinds,
            CommandFamily = commandFamily,
            ExecutionMode = executionMode,
            NativeCommandId = string.IsNullOrWhiteSpace(metadata.NativeCommandId) ? string.Empty : metadata.NativeCommandId,
            SourceKind = sourceKind,
            SourceRef = sourceRef,
            SafetyClass = safetyClass,
            CanPreview = canPreview,
            CoverageTier = coverageTier,
            FallbackEntryIds = new List<string>(metadata.FallbackEntryIds),
            PrimaryPersona = primaryPersona,
            UserValueClass = userValueClass,
            RepeatabilityClass = repeatabilityClass,
            AutomationStage = automationStage,
            CanTeachBack = canTeachBack,
            FallbackArtifactKinds = fallbackArtifactKinds,
            CommercialTier = commercialTier,
            CacheValueClass = cacheValueClass
        };
    }

    private static string ResolveRiskTier(PermissionLevel permissionLevel, ApprovalRequirement approvalRequirement, List<string> riskTags, ToolManifestMetadata metadata)
    {
        if (approvalRequirement == ApprovalRequirement.HighRiskToken
            || ContainsAny(riskTags, "delete", "destructive", "save", "sync", "export", "high_risk"))
        {
            return ToolRiskTiers.Tier2;
        }

        if (!string.IsNullOrWhiteSpace(metadata.RiskTier))
        {
            return metadata.RiskTier;
        }

        if (permissionLevel == PermissionLevel.Read || permissionLevel == PermissionLevel.Review)
        {
            return ToolRiskTiers.Tier0;
        }

        return ToolRiskTiers.Tier1;
    }

    private static string ResolveLatencyClass(string toolName, ToolManifestMetadata metadata, AgentSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(metadata.LatencyClass))
        {
            return metadata.LatencyClass;
        }

        var timeoutMs = metadata.ExecutionTimeoutMs ?? ToolExecutionTimeoutPolicy.GetRecommendedTimeoutMs(settings, toolName);
        if (string.Equals(metadata.BatchMode, "chunked", StringComparison.OrdinalIgnoreCase) && timeoutMs >= 180_000)
        {
            return ToolLatencyClasses.Batch;
        }

        if (timeoutMs >= 180_000)
        {
            return ToolLatencyClasses.LongRunning;
        }

        return timeoutMs <= 60_000 ? ToolLatencyClasses.Interactive : ToolLatencyClasses.Standard;
    }

    private static string ResolveProgressMode(ToolManifestMetadata metadata, string latencyClass)
    {
        if (!string.IsNullOrWhiteSpace(metadata.ProgressMode))
        {
            return metadata.ProgressMode;
        }

        if (string.Equals(latencyClass, ToolLatencyClasses.Batch, StringComparison.OrdinalIgnoreCase)
            || string.Equals(latencyClass, ToolLatencyClasses.LongRunning, StringComparison.OrdinalIgnoreCase))
        {
            return ToolProgressModes.Heartbeat;
        }

        return string.Equals(metadata.BatchMode, "none", StringComparison.OrdinalIgnoreCase)
            ? ToolProgressModes.None
            : ToolProgressModes.StageOnly;
    }

    private static string ResolveUiSurface(string toolName, PermissionLevel permissionLevel, ApprovalRequirement approvalRequirement, ToolManifestMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.UiSurface))
        {
            return metadata.UiSurface;
        }

        if (toolName.StartsWith("worker.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolUiSurfaces.WorkerHome;
        }

        if (toolName.StartsWith("session.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("context.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolUiSurfaces.Queue;
        }

        if (approvalRequirement != ApprovalRequirement.None
            || permissionLevel == PermissionLevel.Mutate
            || permissionLevel == PermissionLevel.FileLifecycle)
        {
            return ToolUiSurfaces.Approvals;
        }

        if (toolName.StartsWith("review.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("artifact.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("sheet.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("family.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolUiSurfaces.Evidence;
        }

        return ToolUiSurfaces.ExpertLab;
    }

    private static bool ResolveAutoExecute(PermissionLevel permissionLevel, ApprovalRequirement approvalRequirement, string riskTier, ToolManifestMetadata metadata)
    {
        if (metadata.CanAutoExecute.HasValue)
        {
            return metadata.CanAutoExecute.Value;
        }

        if (permissionLevel == PermissionLevel.Read || permissionLevel == PermissionLevel.Review)
        {
            return true;
        }

        return approvalRequirement == ApprovalRequirement.None
               && string.Equals(riskTier, ToolRiskTiers.Tier1, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(metadata.Idempotency, "non_idempotent", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ResolveRecommendedNextTools(string toolName, PermissionLevel permissionLevel, ApprovalRequirement approvalRequirement, ToolManifestMetadata metadata)
    {
        if (metadata.RecommendedNextTools.Count > 0)
        {
            return new List<string>(metadata.RecommendedNextTools);
        }

        var next = new List<string>();
        if (toolName.StartsWith("worker.", StringComparison.OrdinalIgnoreCase))
        {
            next.Add(ToolNames.WorkerGetContext);
        }

        if (permissionLevel == PermissionLevel.Read || permissionLevel == PermissionLevel.Review)
        {
            next.Add(ToolNames.ToolGetGuidance);
        }

        if (permissionLevel == PermissionLevel.Mutate || permissionLevel == PermissionLevel.FileLifecycle)
        {
            next.Add(ToolNames.SessionGetQueueState);
            next.Add(ToolNames.ContextGetDeltaSummary);
        }

        if (approvalRequirement != ApprovalRequirement.None)
        {
            next.Add(ToolNames.SessionGetTaskContext);
        }

        return next
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveDomainGroup(string toolName, ToolManifestMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.DomainGroup))
        {
            return metadata.DomainGroup;
        }

        var split = (toolName ?? string.Empty).Split('.');
        return split.Length > 0 ? split[0] : string.Empty;
    }

    private static string ResolveTaskFamily(string toolName, ToolManifestMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.TaskFamily))
        {
            return metadata.TaskFamily;
        }

        var value = toolName ?? string.Empty;
        if (value.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0
            || value.StartsWith("view.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("viewport.", StringComparison.OrdinalIgnoreCase))
        {
            return "documentation";
        }

        if (value.IndexOf("family", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "family_authoring";
        }

        if (value.StartsWith("audit.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("review.", StringComparison.OrdinalIgnoreCase))
        {
            return "audit_qc";
        }

        if (value.StartsWith("worker.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("task.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("playbook.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("workspace.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("pack.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("standards.", StringComparison.OrdinalIgnoreCase))
        {
            return "orchestration";
        }

        if (value.StartsWith("data.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("artifact.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("export.", StringComparison.OrdinalIgnoreCase))
        {
            return "delivery_data";
        }

        return "revit_ops";
    }

    private static string ResolveCapabilityDomain(string toolName, ToolManifestMetadata metadata, string domainGroup, string taskFamily)
    {
        if (!string.IsNullOrWhiteSpace(metadata.CapabilityDomain))
        {
            return metadata.CapabilityDomain;
        }

        var value = toolName ?? string.Empty;
        if (value.StartsWith("annotation.", StringComparison.OrdinalIgnoreCase))
        {
            return CapabilityDomains.Annotation;
        }

        if (value.StartsWith("family.", StringComparison.OrdinalIgnoreCase))
        {
            return CapabilityDomains.FamilyQa;
        }

        if (value.StartsWith("spatial.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("penetration.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("opening.", StringComparison.OrdinalIgnoreCase)
            || value.IndexOf("clash", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("clearance", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return CapabilityDomains.Coordination;
        }

        if (value.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            return CapabilityDomains.Systems;
        }

        if (value.StartsWith("intent.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("policy.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("specialist.", StringComparison.OrdinalIgnoreCase))
        {
            return CapabilityDomains.Intent;
        }

        if (value.StartsWith("integration.", StringComparison.OrdinalIgnoreCase))
        {
            return CapabilityDomains.Integration;
        }

        if (value.StartsWith("sheet.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("view.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("viewport.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("workset.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("parameter.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("data.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("audit.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("review.", StringComparison.OrdinalIgnoreCase))
        {
            return CapabilityDomains.Governance;
        }

        if (string.Equals(taskFamily, "family_authoring", StringComparison.OrdinalIgnoreCase))
        {
            return CapabilityDomains.FamilyQa;
        }

        return string.Equals(domainGroup, "worker", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "task", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "workflow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "playbook", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "workspace", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "pack", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "project", StringComparison.OrdinalIgnoreCase)
            ? CapabilityDomains.General
            : CapabilityDomains.Governance;
    }

    private static string ResolveDeterminismLevel(string toolName, ToolManifestMetadata metadata, string capabilityDomain)
    {
        if (!string.IsNullOrWhiteSpace(metadata.DeterminismLevel))
        {
            return metadata.DeterminismLevel;
        }

        if (toolName.StartsWith("system.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("integration.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolDeterminismLevels.Scaffold;
        }

        if (toolName.StartsWith("intent.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capabilityDomain, CapabilityDomains.Intent, StringComparison.OrdinalIgnoreCase))
        {
            return ToolDeterminismLevels.PolicyBacked;
        }

        if (string.Equals(capabilityDomain, CapabilityDomains.Coordination, StringComparison.OrdinalIgnoreCase)
            || string.Equals(capabilityDomain, CapabilityDomains.Systems, StringComparison.OrdinalIgnoreCase)
            || string.Equals(capabilityDomain, CapabilityDomains.Governance, StringComparison.OrdinalIgnoreCase)
            || string.Equals(capabilityDomain, CapabilityDomains.Annotation, StringComparison.OrdinalIgnoreCase)
            || string.Equals(capabilityDomain, CapabilityDomains.FamilyQa, StringComparison.OrdinalIgnoreCase))
        {
            return ToolDeterminismLevels.PolicyBacked;
        }

        return ToolDeterminismLevels.Deterministic;
    }

    private static bool ResolveRequiresPolicyPack(string toolName, ToolManifestMetadata metadata, string capabilityDomain, string domainGroup)
    {
        if (metadata.RequiresPolicyPack.HasValue)
        {
            return metadata.RequiresPolicyPack.Value;
        }

        if (toolName.StartsWith("worker.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("task.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("playbook.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("workspace.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("policy.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("specialist.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("intent.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(capabilityDomain, CapabilityDomains.General, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(domainGroup, "session", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(domainGroup, "context", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveVerificationMode(string toolName, ToolManifestMetadata metadata, string capabilityDomain)
    {
        if (!string.IsNullOrWhiteSpace(metadata.VerificationMode))
        {
            return metadata.VerificationMode;
        }

        if (toolName.IndexOf("verify", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("review", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("qc", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return ToolVerificationModes.ReadBack;
        }

        return capabilityDomain switch
        {
            var x when string.Equals(x, CapabilityDomains.Coordination, StringComparison.OrdinalIgnoreCase) => ToolVerificationModes.GeometryCheck,
            var x when string.Equals(x, CapabilityDomains.Systems, StringComparison.OrdinalIgnoreCase) => ToolVerificationModes.SystemConsistency,
            var x when string.Equals(x, CapabilityDomains.Governance, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(x, CapabilityDomains.Annotation, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(x, CapabilityDomains.FamilyQa, StringComparison.OrdinalIgnoreCase) => ToolVerificationModes.PolicyCheck,
            _ => ToolVerificationModes.ReportOnly
        };
    }

    private static List<string> ResolveSupportedDisciplines(string toolName, ToolManifestMetadata metadata, string capabilityDomain)
    {
        if (metadata.SupportedDisciplines.Count > 0)
        {
            return new List<string>(metadata.SupportedDisciplines);
        }

        if (toolName.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string>
            {
                CapabilityDisciplines.Mep,
                CapabilityDisciplines.Mechanical,
                CapabilityDisciplines.Plumbing,
                CapabilityDisciplines.Electrical,
                CapabilityDisciplines.FireProtection
            };
        }

        if (toolName.StartsWith("spatial.", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string>
            {
                CapabilityDisciplines.Common,
                CapabilityDisciplines.Architecture,
                CapabilityDisciplines.Structure,
                CapabilityDisciplines.Mep,
                CapabilityDisciplines.Mechanical,
                CapabilityDisciplines.Plumbing,
                CapabilityDisciplines.Electrical,
                CapabilityDisciplines.FireProtection
            };
        }

        if (toolName.StartsWith("sheet.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("view.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("annotation.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("workset.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("parameter.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("data.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("audit.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("review.", StringComparison.OrdinalIgnoreCase))
        {
            return new List<string>
            {
                CapabilityDisciplines.Common,
                CapabilityDisciplines.Architecture,
                CapabilityDisciplines.Structure,
                CapabilityDisciplines.Mep,
                CapabilityDisciplines.Mechanical,
                CapabilityDisciplines.Plumbing,
                CapabilityDisciplines.Electrical,
                CapabilityDisciplines.FireProtection
            };
        }

        if (string.Equals(capabilityDomain, CapabilityDomains.Integration, StringComparison.OrdinalIgnoreCase))
        {
            return new List<string> { CapabilityDisciplines.Common };
        }

        return new List<string> { CapabilityDisciplines.Common };
    }

    private static List<string> ResolveIssueKinds(string toolName, ToolManifestMetadata metadata, string capabilityDomain, string domainGroup, string taskFamily)
    {
        if (metadata.IssueKinds.Count > 0)
        {
            return new List<string>(metadata.IssueKinds);
        }

        var value = toolName ?? string.Empty;
        var results = new List<string>();
        if (value.StartsWith("sheet.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("view.", StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(results, CapabilityIssueKinds.SheetPackage);
            AddDistinct(results, CapabilityIssueKinds.NamingConvention);
        }

        if (value.StartsWith("annotation.", StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(results, CapabilityIssueKinds.TagOverlap);
            AddDistinct(results, CapabilityIssueKinds.DimensionCollision);
        }

        if (value.StartsWith("parameter.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("data.", StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(results, CapabilityIssueKinds.ParameterPopulation);
        }

        if (value.StartsWith("audit.", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("review.", StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(results, CapabilityIssueKinds.WarningTriage);
            AddDistinct(results, CapabilityIssueKinds.LodLoiCompliance);
        }

        if (value.StartsWith("family.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(taskFamily, "family_authoring", StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(results, CapabilityIssueKinds.FamilyQa);
        }

        if (value.StartsWith("spatial.", StringComparison.OrdinalIgnoreCase)
            || value.IndexOf("clash", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("penetration", StringComparison.OrdinalIgnoreCase) >= 0
            || value.IndexOf("opening", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            AddDistinct(results, CapabilityIssueKinds.HardClash);
            AddDistinct(results, CapabilityIssueKinds.ClearanceSoftClash);
        }

        if (value.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(results, CapabilityIssueKinds.DisconnectedSystem);
            AddDistinct(results, CapabilityIssueKinds.SlopeContinuity);
            AddDistinct(results, CapabilityIssueKinds.BasicRouting);
            AddDistinct(results, CapabilityIssueKinds.SystemSizing);
        }

        if (value.StartsWith("intent.", StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(results, CapabilityIssueKinds.IntentCompile);
        }

        if (value.StartsWith("integration.", StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(results, CapabilityIssueKinds.ExternalSync);
            AddDistinct(results, CapabilityIssueKinds.ScanToBim);
            AddDistinct(results, CapabilityIssueKinds.LargeModelSplit);
        }

        if (results.Count == 0 && string.Equals(capabilityDomain, CapabilityDomains.Governance, StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(results, CapabilityIssueKinds.WarningTriage);
        }

        return results;
    }

    private static string ResolvePackId(ToolManifestMetadata metadata, string capabilityPack, string domainGroup)
    {
        if (!string.IsNullOrWhiteSpace(metadata.PackId))
        {
            return metadata.PackId;
        }

        if (string.Equals(domainGroup, "worker", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "playbook", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "workspace", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "pack", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "standards", StringComparison.OrdinalIgnoreCase))
        {
            return "bim765t.agents.orchestrator";
        }

        if (string.Equals(domainGroup, "sheet", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "view", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "viewport", StringComparison.OrdinalIgnoreCase))
        {
            return "bim765t.playbooks.core";
        }

        if (string.Equals(domainGroup, "family", StringComparison.OrdinalIgnoreCase))
        {
            return "bim765t.agents.specialist.family";
        }

        if (string.Equals(domainGroup, "annotation", StringComparison.OrdinalIgnoreCase))
        {
            return "bim765t.agents.specialist.annotation";
        }

        if (string.Equals(domainGroup, "spatial", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "penetration", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "opening", StringComparison.OrdinalIgnoreCase))
        {
            return "bim765t.agents.specialist.coordination";
        }

        if (string.Equals(domainGroup, "system", StringComparison.OrdinalIgnoreCase))
        {
            return "bim765t.agents.specialist.systems";
        }

        if (string.Equals(domainGroup, "integration", StringComparison.OrdinalIgnoreCase))
        {
            return "bim765t.agents.specialist.integration";
        }

        if (string.Equals(domainGroup, "intent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "policy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "specialist", StringComparison.OrdinalIgnoreCase))
        {
            return "bim765t.skills.intent";
        }

        if (string.Equals(domainGroup, "audit", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "review", StringComparison.OrdinalIgnoreCase))
        {
            return "bim765t.agents.specialist.audit";
        }

        if (string.Equals(domainGroup, "sheet", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "view", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "viewport", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "workset", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "parameter", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "data", StringComparison.OrdinalIgnoreCase))
        {
            return "bim765t.skills.governance";
        }

        return string.IsNullOrWhiteSpace(capabilityPack)
            ? "bim765t.core.platform"
            : "bim765t.pack." + capabilityPack.Replace('_', '-');
    }

    private static List<string> ResolveRecommendedPlaybooks(string toolName, ToolManifestMetadata metadata, string domainGroup, string taskFamily)
    {
        if (metadata.RecommendedPlaybooks.Count > 0)
        {
            return new List<string>(metadata.RecommendedPlaybooks);
        }

        var results = new List<string>();
        if (string.Equals(taskFamily, "documentation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "sheet", StringComparison.OrdinalIgnoreCase))
        {
            results.Add("sheet_create_arch_package.v1");
            results.Add("sheet_review_team_standard.v1");
        }

        if (string.Equals(domainGroup, "annotation", StringComparison.OrdinalIgnoreCase))
        {
            results.Add("annotation_smart_tag_dimension.v1");
            results.Add("room_finish_generate.v1");
        }

        if (string.Equals(taskFamily, "audit_qc", StringComparison.OrdinalIgnoreCase))
        {
            results.Add("sheet_review_team_standard.v1");
            results.Add("warning_triage_safe.v1");
        }

        if (string.Equals(taskFamily, "family_authoring", StringComparison.OrdinalIgnoreCase))
        {
            results.Add("family_benchmark_servicebox.v1");
            results.Add("family_intake_qa.v1");
        }

        if (string.Equals(domainGroup, "parameter", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "data", StringComparison.OrdinalIgnoreCase))
        {
            results.Add("parameter_import_fill.v1");
        }

        if (string.Equals(domainGroup, "spatial", StringComparison.OrdinalIgnoreCase))
        {
            results.Add("coordination_clash_resolution.v1");
        }

        if (string.Equals(domainGroup, "system", StringComparison.OrdinalIgnoreCase))
        {
            results.Add("system_disconnected_fix.v1");
            results.Add("system_slope_remediation.v1");
        }

        if (string.Equals(domainGroup, "intent", StringComparison.OrdinalIgnoreCase))
        {
            results.Add("intent_nl_query_compile.v1");
        }

        return results
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveCommandFamily(ToolManifestMetadata metadata, string domainGroup, string taskFamily)
    {
        if (!string.IsNullOrWhiteSpace(metadata.CommandFamily))
        {
            return metadata.CommandFamily;
        }

        if (string.Equals(taskFamily, "documentation", StringComparison.OrdinalIgnoreCase))
        {
            return "sheet_documentation";
        }

        if (string.Equals(taskFamily, "audit_qc", StringComparison.OrdinalIgnoreCase))
        {
            return "audit_qc";
        }

        if (string.Equals(taskFamily, "family_authoring", StringComparison.OrdinalIgnoreCase))
        {
            return "family_load_edit";
        }

        return string.IsNullOrWhiteSpace(domainGroup) ? "general" : domainGroup.Replace('_', '-');
    }

    private static string ResolveExecutionMode(ToolManifestMetadata metadata)
    {
        return string.IsNullOrWhiteSpace(metadata.ExecutionMode)
            ? CommandExecutionModes.Tool
            : metadata.ExecutionMode;
    }

    private static string ResolveSourceKind(ToolManifestMetadata metadata, string executionMode)
    {
        if (!string.IsNullOrWhiteSpace(metadata.SourceKind))
        {
            return metadata.SourceKind;
        }

        if (string.Equals(executionMode, CommandExecutionModes.Native, StringComparison.OrdinalIgnoreCase))
        {
            return CommandSourceKinds.Revit;
        }

        return string.Equals(executionMode, CommandExecutionModes.Script, StringComparison.OrdinalIgnoreCase)
            ? CommandSourceKinds.Internal
            : CommandSourceKinds.Repo;
    }

    private static string ResolveSourceRef(string toolName, ToolManifestMetadata metadata, string executionMode)
    {
        if (!string.IsNullOrWhiteSpace(metadata.SourceRef))
        {
            return metadata.SourceRef;
        }

        if (string.Equals(executionMode, CommandExecutionModes.Native, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(metadata.NativeCommandId))
        {
            return metadata.NativeCommandId;
        }

        return toolName;
    }

    private static string ResolveSafetyClass(PermissionLevel permissionLevel, ApprovalRequirement approvalRequirement, ToolManifestMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.SafetyClass))
        {
            return metadata.SafetyClass;
        }

        if (permissionLevel == PermissionLevel.Read || permissionLevel == PermissionLevel.Review)
        {
            return CommandSafetyClasses.ReadOnly;
        }

        return approvalRequirement == ApprovalRequirement.HighRiskToken
            ? CommandSafetyClasses.HighRiskMutation
            : CommandSafetyClasses.PreviewedMutation;
    }

    private static bool ResolveCanPreview(bool supportsDryRun, PermissionLevel permissionLevel, ToolManifestMetadata metadata)
    {
        if (metadata.CanPreview.HasValue)
        {
            return metadata.CanPreview.Value;
        }

        return supportsDryRun || permissionLevel == PermissionLevel.Mutate || permissionLevel == PermissionLevel.FileLifecycle;
    }

    private static string ResolveCoverageTier(ToolManifestMetadata metadata)
    {
        return string.IsNullOrWhiteSpace(metadata.CoverageTier)
            ? CommandCoverageTiers.Baseline
            : metadata.CoverageTier;
    }

    private static string ResolvePrimaryPersona(string toolName, ToolManifestMetadata metadata, string capabilityDomain, string taskFamily)
    {
        if (!string.IsNullOrWhiteSpace(metadata.PrimaryPersona))
        {
            return metadata.PrimaryPersona;
        }

        if (string.Equals(capabilityDomain, CapabilityDomains.Systems, StringComparison.OrdinalIgnoreCase)
            || string.Equals(capabilityDomain, CapabilityDomains.Coordination, StringComparison.OrdinalIgnoreCase))
        {
            return ToolPrimaryPersonas.MepSpecialist;
        }

        if (string.Equals(taskFamily, "audit_qc", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("audit.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("review.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolPrimaryPersonas.QaManager;
        }

        if (toolName.StartsWith("script.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("intent.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("policy.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolPrimaryPersonas.PlatformAuthor;
        }

        return ToolPrimaryPersonas.ProductionBimer;
    }

    private static string ResolveUserValueClass(string toolName, ToolManifestMetadata metadata, string domainGroup, string taskFamily)
    {
        if (!string.IsNullOrWhiteSpace(metadata.UserValueClass))
        {
            return metadata.UserValueClass;
        }

        if (toolName.StartsWith("project.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("workspace.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("standards.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("playbook.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolUserValueClasses.TemplateGeneration;
        }

        if (toolName.StartsWith("watch.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolUserValueClasses.Autopilot;
        }

        if (string.Equals(taskFamily, "documentation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "sheet", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "view", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "viewport", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "parameter", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "data", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("export.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolUserValueClasses.DailyRoi;
        }

        if (string.Equals(taskFamily, "audit_qc", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("audit.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("review.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("query.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolUserValueClasses.SmartValue;
        }

        return ToolUserValueClasses.DailyRoi;
    }

    private static string ResolveRepeatabilityClass(string toolName, ToolManifestMetadata metadata, PermissionLevel permissionLevel, string userValueClass)
    {
        if (!string.IsNullOrWhiteSpace(metadata.RepeatabilityClass))
        {
            return metadata.RepeatabilityClass;
        }

        if (toolName.StartsWith("session.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("context.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, ToolNames.CommandSearch, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, ToolNames.CommandDescribe, StringComparison.OrdinalIgnoreCase))
        {
            return ToolRepeatabilityClasses.OneOff;
        }

        if (string.Equals(userValueClass, ToolUserValueClasses.Autopilot, StringComparison.OrdinalIgnoreCase))
        {
            return ToolRepeatabilityClasses.Watchable;
        }

        if (permissionLevel == PermissionLevel.Mutate
            || permissionLevel == PermissionLevel.FileLifecycle
            || toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("playbook.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("script.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolRepeatabilityClasses.Teachable;
        }

        return ToolRepeatabilityClasses.Repeatable;
    }

    private static string ResolveAutomationStage(string toolName, ToolManifestMetadata metadata, string domainGroup, string executionMode)
    {
        if (!string.IsNullOrWhiteSpace(metadata.AutomationStage))
        {
            return metadata.AutomationStage;
        }

        if (toolName.StartsWith("watch.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolAutomationStages.ProactiveWatch;
        }

        if (toolName.StartsWith("script.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(executionMode, CommandExecutionModes.Script, StringComparison.OrdinalIgnoreCase))
        {
            return ToolAutomationStages.ArtifactFallback;
        }

        if (toolName.StartsWith("playbook.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "worker", StringComparison.OrdinalIgnoreCase))
        {
            return ToolAutomationStages.PlaybookReady;
        }

        if (toolName.StartsWith("project.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("workspace.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("standards.", StringComparison.OrdinalIgnoreCase))
        {
            return ToolAutomationStages.TemplateSynthesis;
        }

        return ToolAutomationStages.CoreSkill;
    }

    private static bool ResolveCanTeachBack(string toolName, ToolManifestMetadata metadata, string repeatabilityClass, string automationStage, string userValueClass)
    {
        if (metadata.CanTeachBack.HasValue)
        {
            return metadata.CanTeachBack.Value;
        }

        if (string.Equals(repeatabilityClass, ToolRepeatabilityClasses.OneOff, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (toolName.StartsWith("session.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("context.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("worker.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(repeatabilityClass, ToolRepeatabilityClasses.Teachable, StringComparison.OrdinalIgnoreCase)
               || string.Equals(repeatabilityClass, ToolRepeatabilityClasses.Watchable, StringComparison.OrdinalIgnoreCase)
               || string.Equals(automationStage, ToolAutomationStages.PlaybookReady, StringComparison.OrdinalIgnoreCase)
               || string.Equals(userValueClass, ToolUserValueClasses.DailyRoi, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ResolveFallbackArtifactKinds(string toolName, ToolManifestMetadata metadata, string domainGroup, string taskFamily, string capabilityDomain, string automationStage)
    {
        if (metadata.FallbackArtifactKinds.Count > 0)
        {
            return new List<string>(metadata.FallbackArtifactKinds);
        }

        var results = new List<string>();
        var normalized = toolName ?? string.Empty;

        if (string.Equals(automationStage, ToolAutomationStages.ArtifactFallback, StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(results, FallbackArtifactKinds.Playbook);
        }

        if (string.Equals(domainGroup, "parameter", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "data", StringComparison.OrdinalIgnoreCase)
            || normalized.IndexOf("import", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("schedule", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            AddDistinct(results, FallbackArtifactKinds.Playbook);
            AddDistinct(results, FallbackArtifactKinds.CsvMapping);
            AddDistinct(results, FallbackArtifactKinds.OpenXmlRecipe);
        }

        if (string.Equals(taskFamily, "documentation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "sheet", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "view", StringComparison.OrdinalIgnoreCase)
            || string.Equals(domainGroup, "viewport", StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(results, FallbackArtifactKinds.Playbook);
        }

        if (normalized.IndexOf("export", StringComparison.OrdinalIgnoreCase) >= 0
            || normalized.IndexOf("print", StringComparison.OrdinalIgnoreCase) >= 0
            || string.Equals(capabilityDomain, CapabilityDomains.Integration, StringComparison.OrdinalIgnoreCase))
        {
            AddDistinct(results, FallbackArtifactKinds.ExportProfile);
        }

        return results;
    }

    private static string ResolveCommercialTier(ToolManifestMetadata metadata, WorkerProductDescriptor product, PermissionLevel permissionLevel, string capabilityDomain, string automationStage, string userValueClass, IReadOnlyCollection<string> fallbackArtifactKinds)
    {
        if (!string.IsNullOrWhiteSpace(metadata.CommercialTier))
        {
            return metadata.CommercialTier;
        }

        if (string.Equals(product.Audience, WorkerAudience.Internal, StringComparison.OrdinalIgnoreCase)
            || string.Equals(product.Visibility, WorkerVisibility.BetaInternal, StringComparison.OrdinalIgnoreCase))
        {
            return CommercialTiers.Internal;
        }

        if (string.Equals(automationStage, ToolAutomationStages.TemplateSynthesis, StringComparison.OrdinalIgnoreCase)
            || string.Equals(automationStage, ToolAutomationStages.ProactiveWatch, StringComparison.OrdinalIgnoreCase)
            || string.Equals(capabilityDomain, CapabilityDomains.Integration, StringComparison.OrdinalIgnoreCase)
            || string.Equals(capabilityDomain, CapabilityDomains.Systems, StringComparison.OrdinalIgnoreCase))
        {
            return CommercialTiers.StudioAutopilot;
        }

        if (permissionLevel == PermissionLevel.Mutate
            || permissionLevel == PermissionLevel.FileLifecycle
            || fallbackArtifactKinds.Count > 0
            || string.Equals(userValueClass, ToolUserValueClasses.TemplateGeneration, StringComparison.OrdinalIgnoreCase))
        {
            return CommercialTiers.PersonalPro;
        }

        return CommercialTiers.Free;
    }

    private static string ResolveCacheValueClass(ToolManifestMetadata metadata, string toolName, PermissionLevel permissionLevel, bool canTeachBack, string automationStage, IReadOnlyCollection<string> fallbackArtifactKinds)
    {
        if (!string.IsNullOrWhiteSpace(metadata.CacheValueClass))
        {
            return metadata.CacheValueClass;
        }

        if (canTeachBack)
        {
            return CacheValueClasses.TeachBack;
        }

        if (fallbackArtifactKinds.Count > 0
            || string.Equals(automationStage, ToolAutomationStages.ArtifactFallback, StringComparison.OrdinalIgnoreCase))
        {
            return CacheValueClasses.ArtifactReuse;
        }

        if ((permissionLevel == PermissionLevel.Read || permissionLevel == PermissionLevel.Review)
            && (toolName.StartsWith("session.", StringComparison.OrdinalIgnoreCase)
                || toolName.StartsWith("context.", StringComparison.OrdinalIgnoreCase)
                || toolName.StartsWith("worker.", StringComparison.OrdinalIgnoreCase)))
        {
            return CacheValueClasses.None;
        }

        return CacheValueClasses.IntentToolchain;
    }

    private static bool ContainsAny(IEnumerable<string> values, params string[] expected)
    {
        return values.Any(value => expected.Any(match => string.Equals(value, match, StringComparison.OrdinalIgnoreCase)));
    }

    private static void AddDistinct(ICollection<string> values, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !values.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            values.Add(value);
        }
    }
}
