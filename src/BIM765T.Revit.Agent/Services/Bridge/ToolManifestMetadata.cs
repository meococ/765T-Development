using System;
using System.Collections.Generic;

namespace BIM765T.Revit.Agent.Services.Bridge;

internal sealed class ToolManifestMetadata
{
    internal ToolManifestMetadata(
        IEnumerable<string>? requiredContext = null,
        bool touchesActiveView = false,
        string batchMode = "none",
        string idempotency = "read_only",
        IEnumerable<string>? previewArtifacts = null,
        IEnumerable<string>? riskTags = null,
        IEnumerable<string>? rulePackTags = null,
        int? executionTimeoutMs = null,
        string capabilityPack = "",
        string skillGroup = "",
        string audience = "",
        string visibility = "",
        string riskTier = "",
        bool? canAutoExecute = null,
        string latencyClass = "",
        string uiSurface = "",
        string progressMode = "",
        IEnumerable<string>? recommendedNextTools = null,
        string domainGroup = "",
        string taskFamily = "",
        string packId = "",
        IEnumerable<string>? recommendedPlaybooks = null,
        string capabilityDomain = "",
        string determinismLevel = "",
        bool? requiresPolicyPack = null,
        string verificationMode = "",
        IEnumerable<string>? supportedDisciplines = null,
        IEnumerable<string>? issueKinds = null,
        string commandFamily = "",
        string executionMode = "",
        string nativeCommandId = "",
        string sourceKind = "",
        string sourceRef = "",
        string safetyClass = "",
        bool? canPreview = null,
        string coverageTier = "",
        IEnumerable<string>? fallbackEntryIds = null,
        string primaryPersona = "",
        string userValueClass = "",
        string repeatabilityClass = "",
        string automationStage = "",
        bool? canTeachBack = null,
        IEnumerable<string>? fallbackArtifactKinds = null,
        string commercialTier = "",
        string cacheValueClass = "")
    {
        RequiredContext = Normalize(requiredContext);
        TouchesActiveView = touchesActiveView;
        BatchMode = string.IsNullOrWhiteSpace(batchMode) ? "none" : batchMode.Trim();
        Idempotency = string.IsNullOrWhiteSpace(idempotency) ? "unknown" : idempotency.Trim();
        PreviewArtifacts = Normalize(previewArtifacts);
        RiskTags = Normalize(riskTags);
        RulePackTags = Normalize(rulePackTags);
        ExecutionTimeoutMs = executionTimeoutMs;
        CapabilityPack = NormalizeSingle(capabilityPack);
        SkillGroup = NormalizeSingle(skillGroup);
        Audience = NormalizeSingle(audience);
        Visibility = NormalizeSingle(visibility);
        RiskTier = NormalizeSingle(riskTier);
        CanAutoExecute = canAutoExecute;
        LatencyClass = NormalizeSingle(latencyClass);
        UiSurface = NormalizeSingle(uiSurface);
        ProgressMode = NormalizeSingle(progressMode);
        RecommendedNextTools = Normalize(recommendedNextTools);
        DomainGroup = NormalizeSingle(domainGroup);
        TaskFamily = NormalizeSingle(taskFamily);
        PackId = NormalizeSingle(packId);
        RecommendedPlaybooks = Normalize(recommendedPlaybooks);
        CapabilityDomain = NormalizeSingle(capabilityDomain);
        DeterminismLevel = NormalizeSingle(determinismLevel);
        RequiresPolicyPack = requiresPolicyPack;
        VerificationMode = NormalizeSingle(verificationMode);
        SupportedDisciplines = Normalize(supportedDisciplines);
        IssueKinds = Normalize(issueKinds);
        CommandFamily = NormalizeSingle(commandFamily);
        ExecutionMode = NormalizeSingle(executionMode);
        NativeCommandId = NormalizeSingle(nativeCommandId);
        SourceKind = NormalizeSingle(sourceKind);
        SourceRef = NormalizeSingle(sourceRef);
        SafetyClass = NormalizeSingle(safetyClass);
        CanPreview = canPreview;
        CoverageTier = NormalizeSingle(coverageTier);
        FallbackEntryIds = Normalize(fallbackEntryIds);
        PrimaryPersona = NormalizeSingle(primaryPersona);
        UserValueClass = NormalizeSingle(userValueClass);
        RepeatabilityClass = NormalizeSingle(repeatabilityClass);
        AutomationStage = NormalizeSingle(automationStage);
        CanTeachBack = canTeachBack;
        FallbackArtifactKinds = Normalize(fallbackArtifactKinds);
        CommercialTier = NormalizeSingle(commercialTier);
        CacheValueClass = NormalizeSingle(cacheValueClass);
    }

    internal List<string> RequiredContext { get; }

    internal bool TouchesActiveView { get; }

    internal string BatchMode { get; }

    internal string Idempotency { get; }

    internal List<string> PreviewArtifacts { get; }

    internal List<string> RiskTags { get; }

    internal List<string> RulePackTags { get; }

    internal int? ExecutionTimeoutMs { get; }

    internal string CapabilityPack { get; }

    internal string SkillGroup { get; }

    internal string Audience { get; }

    internal string Visibility { get; }

    internal string RiskTier { get; }

    internal bool? CanAutoExecute { get; }

    internal string LatencyClass { get; }

    internal string UiSurface { get; }

    internal string ProgressMode { get; }

    internal List<string> RecommendedNextTools { get; }

    internal string DomainGroup { get; }

    internal string TaskFamily { get; }

    internal string PackId { get; }

    internal List<string> RecommendedPlaybooks { get; }

    internal string CapabilityDomain { get; }

    internal string DeterminismLevel { get; }

    internal bool? RequiresPolicyPack { get; }

    internal string VerificationMode { get; }

    internal List<string> SupportedDisciplines { get; }

    internal List<string> IssueKinds { get; }

    internal string CommandFamily { get; }

    internal string ExecutionMode { get; }

    internal string NativeCommandId { get; }

    internal string SourceKind { get; }

    internal string SourceRef { get; }

    internal string SafetyClass { get; }

    internal bool? CanPreview { get; }

    internal string CoverageTier { get; }

    internal List<string> FallbackEntryIds { get; }

    internal string PrimaryPersona { get; }

    internal string UserValueClass { get; }

    internal string RepeatabilityClass { get; }

    internal string AutomationStage { get; }

    internal bool? CanTeachBack { get; }

    internal List<string> FallbackArtifactKinds { get; }

    internal string CommercialTier { get; }

    internal string CacheValueClass { get; }

    internal ToolManifestMetadata WithRequiredContext(params string[] requiredContext)
    {
        return Clone(requiredContext: requiredContext);
    }

    internal ToolManifestMetadata WithTouchesActiveView(bool touchesActiveView = true)
    {
        return Clone(touchesActiveView: touchesActiveView);
    }

    internal ToolManifestMetadata WithBatchMode(string batchMode)
    {
        return Clone(batchMode: batchMode);
    }

    internal ToolManifestMetadata WithIdempotency(string idempotency)
    {
        return Clone(idempotency: idempotency);
    }

    internal ToolManifestMetadata WithPreviewArtifacts(params string[] previewArtifacts)
    {
        return Clone(previewArtifacts: previewArtifacts);
    }

    internal ToolManifestMetadata WithRiskTags(params string[] riskTags)
    {
        return Clone(riskTags: riskTags);
    }

    internal ToolManifestMetadata WithRulePackTags(params string[] rulePackTags)
    {
        return Clone(rulePackTags: rulePackTags);
    }

    internal ToolManifestMetadata WithExecutionTimeoutMs(int executionTimeoutMs)
    {
        return Clone(executionTimeoutMs: executionTimeoutMs);
    }

    internal ToolManifestMetadata WithCapabilityPack(string capabilityPack)
    {
        return Clone(capabilityPack: capabilityPack);
    }

    internal ToolManifestMetadata WithSkillGroup(string skillGroup)
    {
        return Clone(skillGroup: skillGroup);
    }

    internal ToolManifestMetadata WithAudience(string audience)
    {
        return Clone(audience: audience);
    }

    internal ToolManifestMetadata WithVisibility(string visibility)
    {
        return Clone(visibility: visibility);
    }

    internal ToolManifestMetadata WithRiskTier(string riskTier)
    {
        return Clone(riskTier: riskTier);
    }

    internal ToolManifestMetadata WithAutoExecute(bool canAutoExecute = true)
    {
        return Clone(canAutoExecute: canAutoExecute);
    }

    internal ToolManifestMetadata WithLatencyClass(string latencyClass)
    {
        return Clone(latencyClass: latencyClass);
    }

    internal ToolManifestMetadata WithUiSurface(string uiSurface)
    {
        return Clone(uiSurface: uiSurface);
    }

    internal ToolManifestMetadata WithProgressMode(string progressMode)
    {
        return Clone(progressMode: progressMode);
    }

    internal ToolManifestMetadata WithRecommendedNextTools(params string[] recommendedNextTools)
    {
        return Clone(recommendedNextTools: recommendedNextTools);
    }

    internal ToolManifestMetadata WithDomainGroup(string domainGroup)
    {
        return Clone(domainGroup: domainGroup);
    }

    internal ToolManifestMetadata WithTaskFamily(string taskFamily)
    {
        return Clone(taskFamily: taskFamily);
    }

    internal ToolManifestMetadata WithPackId(string packId)
    {
        return Clone(packId: packId);
    }

    internal ToolManifestMetadata WithRecommendedPlaybooks(params string[] recommendedPlaybooks)
    {
        return Clone(recommendedPlaybooks: recommendedPlaybooks);
    }

    internal ToolManifestMetadata WithCapabilityDomain(string capabilityDomain)
    {
        return Clone(capabilityDomain: capabilityDomain);
    }

    internal ToolManifestMetadata WithDeterminismLevel(string determinismLevel)
    {
        return Clone(determinismLevel: determinismLevel);
    }

    internal ToolManifestMetadata WithRequiresPolicyPack(bool requiresPolicyPack = true)
    {
        return Clone(requiresPolicyPack: requiresPolicyPack);
    }

    internal ToolManifestMetadata WithVerificationMode(string verificationMode)
    {
        return Clone(verificationMode: verificationMode);
    }

    internal ToolManifestMetadata WithSupportedDisciplines(params string[] supportedDisciplines)
    {
        return Clone(supportedDisciplines: supportedDisciplines);
    }

    internal ToolManifestMetadata WithIssueKinds(params string[] issueKinds)
    {
        return Clone(issueKinds: issueKinds);
    }

    internal ToolManifestMetadata WithCommandFamily(string commandFamily)
    {
        return Clone(commandFamily: commandFamily);
    }

    internal ToolManifestMetadata WithExecutionMode(string executionMode)
    {
        return Clone(executionMode: executionMode);
    }

    internal ToolManifestMetadata WithNativeCommandId(string nativeCommandId)
    {
        return Clone(nativeCommandId: nativeCommandId);
    }

    internal ToolManifestMetadata WithSourceKind(string sourceKind)
    {
        return Clone(sourceKind: sourceKind);
    }

    internal ToolManifestMetadata WithSourceRef(string sourceRef)
    {
        return Clone(sourceRef: sourceRef);
    }

    internal ToolManifestMetadata WithSafetyClass(string safetyClass)
    {
        return Clone(safetyClass: safetyClass);
    }

    internal ToolManifestMetadata WithCanPreview(bool canPreview = true)
    {
        return Clone(canPreview: canPreview);
    }

    internal ToolManifestMetadata WithCoverageTier(string coverageTier)
    {
        return Clone(coverageTier: coverageTier);
    }

    internal ToolManifestMetadata WithFallbackEntryIds(params string[] fallbackEntryIds)
    {
        return Clone(fallbackEntryIds: fallbackEntryIds);
    }

    internal ToolManifestMetadata WithPrimaryPersona(string primaryPersona)
    {
        return Clone(primaryPersona: primaryPersona);
    }

    internal ToolManifestMetadata WithUserValueClass(string userValueClass)
    {
        return Clone(userValueClass: userValueClass);
    }

    internal ToolManifestMetadata WithRepeatabilityClass(string repeatabilityClass)
    {
        return Clone(repeatabilityClass: repeatabilityClass);
    }

    internal ToolManifestMetadata WithAutomationStage(string automationStage)
    {
        return Clone(automationStage: automationStage);
    }

    internal ToolManifestMetadata WithCanTeachBack(bool canTeachBack = true)
    {
        return Clone(canTeachBack: canTeachBack);
    }

    internal ToolManifestMetadata WithFallbackArtifactKinds(params string[] fallbackArtifactKinds)
    {
        return Clone(fallbackArtifactKinds: fallbackArtifactKinds);
    }

    internal ToolManifestMetadata WithCommercialTier(string commercialTier)
    {
        return Clone(commercialTier: commercialTier);
    }

    internal ToolManifestMetadata WithCacheValueClass(string cacheValueClass)
    {
        return Clone(cacheValueClass: cacheValueClass);
    }

    private ToolManifestMetadata Clone(
        IEnumerable<string>? requiredContext = null,
        bool? touchesActiveView = null,
        string? batchMode = null,
        string? idempotency = null,
        IEnumerable<string>? previewArtifacts = null,
        IEnumerable<string>? riskTags = null,
        IEnumerable<string>? rulePackTags = null,
        int? executionTimeoutMs = null,
        string? capabilityPack = null,
        string? skillGroup = null,
        string? audience = null,
        string? visibility = null,
        string? riskTier = null,
        bool? canAutoExecute = null,
        string? latencyClass = null,
        string? uiSurface = null,
        string? progressMode = null,
        IEnumerable<string>? recommendedNextTools = null,
        string? domainGroup = null,
        string? taskFamily = null,
        string? packId = null,
        IEnumerable<string>? recommendedPlaybooks = null,
        string? capabilityDomain = null,
        string? determinismLevel = null,
        bool? requiresPolicyPack = null,
        string? verificationMode = null,
        IEnumerable<string>? supportedDisciplines = null,
        IEnumerable<string>? issueKinds = null,
        string? commandFamily = null,
        string? executionMode = null,
        string? nativeCommandId = null,
        string? sourceKind = null,
        string? sourceRef = null,
        string? safetyClass = null,
        bool? canPreview = null,
        string? coverageTier = null,
        IEnumerable<string>? fallbackEntryIds = null,
        string? primaryPersona = null,
        string? userValueClass = null,
        string? repeatabilityClass = null,
        string? automationStage = null,
        bool? canTeachBack = null,
        IEnumerable<string>? fallbackArtifactKinds = null,
        string? commercialTier = null,
        string? cacheValueClass = null)
    {
        return new ToolManifestMetadata(
            requiredContext ?? RequiredContext,
            touchesActiveView ?? TouchesActiveView,
            batchMode ?? BatchMode,
            idempotency ?? Idempotency,
            previewArtifacts ?? PreviewArtifacts,
            riskTags ?? RiskTags,
            rulePackTags ?? RulePackTags,
            executionTimeoutMs ?? ExecutionTimeoutMs,
            capabilityPack ?? CapabilityPack,
            skillGroup ?? SkillGroup,
            audience ?? Audience,
            visibility ?? Visibility,
            riskTier ?? RiskTier,
            canAutoExecute ?? CanAutoExecute,
            latencyClass ?? LatencyClass,
            uiSurface ?? UiSurface,
            progressMode ?? ProgressMode,
            recommendedNextTools ?? RecommendedNextTools,
            domainGroup ?? DomainGroup,
            taskFamily ?? TaskFamily,
            packId ?? PackId,
            recommendedPlaybooks ?? RecommendedPlaybooks,
            capabilityDomain ?? CapabilityDomain,
            determinismLevel ?? DeterminismLevel,
            requiresPolicyPack ?? RequiresPolicyPack,
            verificationMode ?? VerificationMode,
            supportedDisciplines ?? SupportedDisciplines,
            issueKinds ?? IssueKinds,
            commandFamily ?? CommandFamily,
            executionMode ?? ExecutionMode,
            nativeCommandId ?? NativeCommandId,
            sourceKind ?? SourceKind,
            sourceRef ?? SourceRef,
            safetyClass ?? SafetyClass,
            canPreview ?? CanPreview,
            coverageTier ?? CoverageTier,
            fallbackEntryIds ?? FallbackEntryIds,
            primaryPersona ?? PrimaryPersona,
            userValueClass ?? UserValueClass,
            repeatabilityClass ?? RepeatabilityClass,
            automationStage ?? AutomationStage,
            canTeachBack ?? CanTeachBack,
            fallbackArtifactKinds ?? FallbackArtifactKinds,
            commercialTier ?? CommercialTier,
            cacheValueClass ?? CacheValueClass);
    }

    private static List<string> Normalize(IEnumerable<string>? values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values == null)
        {
            return result;
        }

        foreach (var value in values)
        {
            if (value == null)
            {
                continue;
            }

            var normalized = value.Trim();
            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }

    private static string NormalizeSingle(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : (value?.Trim() ?? string.Empty);
    }
}
