using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Copilot.Core.Brain;

namespace BIM765T.Revit.Agent.Config;

public static class UiThemeModes
{
    public const string Dark = "dark";
    public const string Light = "light";
}

[DataContract]
public sealed class AgentSettings
{
    [DataMember(Order = 1)]
    public string PipeName { get; set; } = BridgeConstants.DefaultPipeName;

    [DataMember(Order = 2)]
    public bool EnablePipeServer { get; set; } = true;

    [DataMember(Order = 3)]
    public bool AllowWriteTools { get; set; } = false;

    [DataMember(Order = 4)]
    public int RequestTimeoutSeconds { get; set; } = BridgeConstants.DefaultRequestTimeoutSeconds;

    [DataMember(Order = 5)]
    public bool VerboseLogging { get; set; } = true;

    [DataMember(Order = 6)]
    public int ApprovalTokenTtlMinutes { get; set; } = BridgeConstants.DefaultApprovalTokenTtlMinutes;

    [DataMember(Order = 7)]
    public int MaxRecentOperations { get; set; } = BridgeConstants.DefaultMaxRecentOperations;

    [DataMember(Order = 8)]
    public int MaxRecentEvents { get; set; } = BridgeConstants.DefaultMaxRecentEvents;

    [DataMember(Order = 9)]
    public bool EnableOperationJournal { get; set; } = true;

    [DataMember(Order = 10)]
    public bool EnableEventIndex { get; set; } = true;

    [DataMember(Order = 11)]
    public bool AllowDeleteTools { get; set; } = false;

    [DataMember(Order = 12)]
    public bool AllowSaveTools { get; set; } = true;

    [DataMember(Order = 13)]
    public bool AllowSyncTools { get; set; } = true;

    [DataMember(Order = 14)]
    public bool AllowBackgroundOpenRead { get; set; } = false;

    [DataMember(Order = 15)]
    public int MaxRequestsPerMinute { get; set; } = 240;

    [DataMember(Order = 16)]
    public int MaxHighRiskRequestsPerMinute { get; set; } = 30;

    [DataMember(Order = 17)]
    public int RequestRateLimitWindowSeconds { get; set; } = 60;

    [DataMember(Order = 18)]
    public bool JsonLogFormat { get; set; } = true;

    [DataMember(Order = 19)]
    public List<string> EnabledCapabilityPacks { get; set; } = new List<string>
    {
        WorkerCapabilityPacks.CoreWorker,
        WorkerCapabilityPacks.MemoryAndSoul
    };

    [DataMember(Order = 20)]
    public List<string> EnabledSkillGroups { get; set; } = new List<string>();

    [DataMember(Order = 21)]
    public string VisibleShellMode { get; set; } = WorkerShellModes.Worker;

    [DataMember(Order = 22)]
    public WorkerProfile DefaultWorkerProfile { get; set; } = new WorkerProfile
    {
        PersonaId = WorkerPersonas.FreelancerDefault,
        Tone = "pragmatic",
        QaStrictness = "standard",
        RiskTolerance = "guarded",
        EscalationStyle = "checkpoint_first"
    };

    [DataMember(Order = 23)]
    public string KernelPipeName { get; set; } = BridgeConstants.DefaultKernelPipeName;

    [DataMember(Order = 24)]
    public bool EnableKernelPipeServer { get; set; } = true;

    [DataMember(Order = 25)]
    public string ProjectWorkspaceRootPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        BridgeConstants.AppDataFolderName,
        "workspaces");

    [DataMember(Order = 26)]
    public string UiThemeMode { get; set; } = UiThemeModes.Dark;

    /// <summary>
    /// Returns true if any supported external AI gateway is configured via environment variables.
    /// Current supported priority:
    /// 0. BIM765T_LLM_PROVIDER (optional hard pin: OPENROUTER / MINIMAX / OPENAI / ANTHROPIC / RULE_FIRST)
    /// 1. OPENROUTER_API_KEY
    /// 2. MINIMAX_API_KEY / MINIMAX_AUTH_TOKEN
    /// 3. OPENAI_API_KEY / OPENAI_AUTH_TOKEN
    /// 4. ANTHROPIC_AUTH_TOKEN / ANTHROPIC_API_KEY
    /// Used by UI to display "AI Connected" vs "Rule-Only" badge.
    /// Not serialized — computed at runtime from env var.
    /// </summary>
    [IgnoreDataMember]
    public bool LlmConfigured => ResolveLlmProfile().IsConfigured;

    [IgnoreDataMember]
    public string LlmProviderLabel => ResolveLlmProfile().ConfiguredProvider;

    [IgnoreDataMember]
    public string LlmPlannerModel => ResolveLlmProfile().PlannerPrimaryModel;

    [IgnoreDataMember]
    public string LlmResponseModel => ResolveLlmProfile().ResponseModel;

    [IgnoreDataMember]
    public string LlmSecretSourceKind => ResolveLlmProfile().SecretSourceKind;

    [IgnoreDataMember]
    public string LlmProfileLabel => ResolveLlmProfile().DisplayLabel;

    private static LlmProviderConfiguration ResolveLlmProfile()
    {
        return new OpenRouterFirstLlmProviderConfigResolver(new EnvSecretProvider()).Resolve();
    }
}
