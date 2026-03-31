using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal static class ToolCatalogFilter
{
    internal static List<ToolManifest> FilterForSessionList(IEnumerable<ToolManifest> manifests)
    {
        return Filter(manifests, ToolCatalogAudience.WorkerUi);
    }

    internal static List<ToolManifest> FilterForMcp(IEnumerable<ToolManifest> manifests)
    {
        return Filter(manifests, ToolCatalogAudience.Mcp);
    }

    internal static List<ToolManifest> FilterForPublicCatalog(IEnumerable<ToolManifest> manifests)
    {
        return Filter(manifests, ToolCatalogAudience.PublicCatalog);
    }

    private static List<ToolManifest> Filter(IEnumerable<ToolManifest> manifests, ToolCatalogAudience audience)
    {
        return (manifests ?? Enumerable.Empty<ToolManifest>())
            .Where(manifest => manifest != null)
            .Where(manifest => IsVisibleToAudience(manifest, audience))
            .Select(Clone)
            .OrderBy(manifest => manifest.ToolName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsVisibleToAudience(ToolManifest manifest, ToolCatalogAudience audience)
    {
        var visibility = manifest.Visibility ?? string.Empty;
        var manifestAudience = manifest.Audience ?? string.Empty;
        var primaryPersona = manifest.PrimaryPersona ?? string.Empty;

        if (string.Equals(visibility, WorkerVisibility.Hidden, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (audience == ToolCatalogAudience.PublicCatalog)
        {
            if (string.Equals(visibility, WorkerVisibility.BetaInternal, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(manifestAudience, WorkerAudience.Commercial, StringComparison.OrdinalIgnoreCase);
        }

        if (audience == ToolCatalogAudience.Mcp)
        {
            if (string.Equals(visibility, WorkerVisibility.BetaInternal, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(manifestAudience, WorkerAudience.Internal, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.Equals(primaryPersona, ToolPrimaryPersonas.PlatformAuthor, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(manifestAudience, WorkerAudience.Commercial, StringComparison.OrdinalIgnoreCase)
                || string.Equals(manifestAudience, WorkerAudience.Connector, StringComparison.OrdinalIgnoreCase);
        }

        return !string.Equals(visibility, WorkerVisibility.Hidden, StringComparison.OrdinalIgnoreCase);
    }

    private static ToolManifest Clone(ToolManifest manifest)
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
            RequiredContext = manifest.RequiredContext?.ToList() ?? new List<string>(),
            MutatesModel = manifest.MutatesModel,
            TouchesActiveView = manifest.TouchesActiveView,
            RequiresExpectedContext = manifest.RequiresExpectedContext,
            BatchMode = manifest.BatchMode,
            Idempotency = manifest.Idempotency,
            PreviewArtifacts = manifest.PreviewArtifacts?.ToList() ?? new List<string>(),
            RiskTags = manifest.RiskTags?.ToList() ?? new List<string>(),
            RulePackTags = manifest.RulePackTags?.ToList() ?? new List<string>(),
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
            RecommendedNextTools = manifest.RecommendedNextTools?.ToList() ?? new List<string>(),
            DomainGroup = manifest.DomainGroup,
            TaskFamily = manifest.TaskFamily,
            PackId = manifest.PackId,
            RecommendedPlaybooks = manifest.RecommendedPlaybooks?.ToList() ?? new List<string>(),
            CapabilityDomain = manifest.CapabilityDomain,
            DeterminismLevel = manifest.DeterminismLevel,
            RequiresPolicyPack = manifest.RequiresPolicyPack,
            VerificationMode = manifest.VerificationMode,
            SupportedDisciplines = manifest.SupportedDisciplines?.ToList() ?? new List<string>(),
            IssueKinds = manifest.IssueKinds?.ToList() ?? new List<string>(),
            CommandFamily = manifest.CommandFamily,
            ExecutionMode = manifest.ExecutionMode,
            NativeCommandId = manifest.NativeCommandId,
            SourceKind = manifest.SourceKind,
            SourceRef = manifest.SourceRef,
            SafetyClass = manifest.SafetyClass,
            CanPreview = manifest.CanPreview,
            CoverageTier = manifest.CoverageTier,
            FallbackEntryIds = manifest.FallbackEntryIds?.ToList() ?? new List<string>(),
            PrimaryPersona = manifest.PrimaryPersona,
            UserValueClass = manifest.UserValueClass,
            RepeatabilityClass = manifest.RepeatabilityClass,
            AutomationStage = manifest.AutomationStage,
            CanTeachBack = manifest.CanTeachBack,
            FallbackArtifactKinds = manifest.FallbackArtifactKinds?.ToList() ?? new List<string>(),
            CommercialTier = manifest.CommercialTier,
            CacheValueClass = manifest.CacheValueClass
        };
    }

    internal enum ToolCatalogAudience
    {
        WorkerUi,
        Mcp,
        PublicCatalog
    }
}
