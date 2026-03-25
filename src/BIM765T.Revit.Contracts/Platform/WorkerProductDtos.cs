using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Contracts.Bridge;

namespace BIM765T.Revit.Contracts.Platform;

public static class WorkerCapabilityPacks
{
    public const string CoreWorker = "core_worker";
    public const string MemoryAndSoul = "memory_and_soul";
    public const string Connector = "connector";
    public const string AutomationLab = "automation_lab";
}

public static class WorkerSkillGroups
{
    public const string Documentation = "documentation";
    public const string QualityControl = "quality_control";
    public const string Orchestration = "orchestration";
    public const string RevitOps = "revit_ops";
    public const string Automation = "automation";
    public const string Governance = "governance";
    public const string Annotation = "annotation";
    public const string Coordination = "coordination";
    public const string Systems = "systems";
    public const string Intent = "intent";
    public const string Integration = "integration";
}

public static class WorkerPersonas
{
    public const string RevitWorker = "revit_worker";
    public const string QaReviewer = "qa_reviewer";
    public const string Helper = "helper";
    public const string FreelancerDefault = "freelancer_default";
    public const string StrictQaFirm = "strict_qa_firm";
    public const string ProductionSpeedStudio = "production_speed_studio";
}

public static class WorkerAudience
{
    public const string Commercial = "commercial";
    public const string Internal = "internal";
    public const string Connector = "connector";
}

public static class WorkerVisibility
{
    public const string Visible = "visible";
    public const string BetaInternal = "beta_internal";
    public const string Hidden = "hidden";
}

public static class WorkerShellModes
{
    public const string Worker = "worker";
    public const string InternalWorkbench = "internal_workbench";
}

[DataContract]
public sealed class TaskSpec
{
    [DataMember(Order = 1)]
    public string Source { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Goal { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string DocumentScope { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string ProjectScope { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<string> Constraints { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public TaskApprovalPolicy ApprovalPolicy { get; set; } = new TaskApprovalPolicy();

    [DataMember(Order = 7)]
    public List<TaskDeliverableSpec> Deliverables { get; set; } = new List<TaskDeliverableSpec>();

    [DataMember(Order = 8)]
    public TaskCallbackTarget CallbackTarget { get; set; } = new TaskCallbackTarget();
}

[DataContract]
public sealed class TaskApprovalPolicy
{
    [DataMember(Order = 1)]
    public string ReviewMode { get; set; } = "checkpointed";

    [DataMember(Order = 2)]
    public bool RequiresOperatorApproval { get; set; } = true;

    [DataMember(Order = 3)]
    public bool AllowQueuedExecution { get; set; }

    [DataMember(Order = 4)]
    public int MaxBatchSize { get; set; } = 100;
}

[DataContract]
public sealed class TaskDeliverableSpec
{
    [DataMember(Order = 1)]
    public string Name { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Format { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public bool Required { get; set; } = true;
}

[DataContract]
public sealed class TaskCallbackTarget
{
    [DataMember(Order = 1)]
    public string System { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string Reference { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string Mode { get; set; } = "panel_only";

    [DataMember(Order = 4)]
    public string Destination { get; set; } = string.Empty;
}

[DataContract]
public sealed class WorkerProfile
{
    [DataMember(Order = 1)]
    public string PersonaId { get; set; } = WorkerPersonas.FreelancerDefault;

    [DataMember(Order = 2)]
    public string Tone { get; set; } = "pragmatic";

    [DataMember(Order = 3)]
    public string QaStrictness { get; set; } = "standard";

    [DataMember(Order = 4)]
    public List<string> AllowedSkillGroups { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public string RiskTolerance { get; set; } = "guarded";

    [DataMember(Order = 6)]
    public string EscalationStyle { get; set; } = "checkpoint_first";
}

[DataContract]
public sealed class MemoryRecord
{
    [DataMember(Order = 1)]
    public string Scope { get; set; } = "project";

    [DataMember(Order = 2)]
    public string Kind { get; set; } = "lesson";

    [DataMember(Order = 3)]
    public string Source { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string Summary { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public List<string> Tags { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public double Confidence { get; set; } = 0.5d;

    [DataMember(Order = 7)]
    public string PromotionStatus { get; set; } = "captured";

    [DataMember(Order = 8)]
    public DateTime? LastVerifiedUtc { get; set; }

    [DataMember(Order = 9)]
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

[DataContract]
public sealed class RunReport
{
    [DataMember(Order = 1)]
    public string TaskSummary { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public List<string> PlanExecuted { get; set; } = new List<string>();

    [DataMember(Order = 3)]
    public List<string> ApprovalCheckpoints { get; set; } = new List<string>();

    [DataMember(Order = 4)]
    public List<string> ActionsPerformed { get; set; } = new List<string>();

    [DataMember(Order = 5)]
    public List<string> ArtifactsGenerated { get; set; } = new List<string>();

    [DataMember(Order = 6)]
    public List<string> ResidualRisks { get; set; } = new List<string>();

    [DataMember(Order = 7)]
    public string NextRecommendedAction { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public DateTime GeneratedUtc { get; set; } = DateTime.UtcNow;
}

[DataContract]
public sealed class ConnectorTaskEnvelope
{
    [DataMember(Order = 1)]
    public string ExternalSystem { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string ExternalTaskRef { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ProjectRef { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string AuthContext { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string CallbackMode { get; set; } = "panel_only";

    [DataMember(Order = 6)]
    public Dictionary<string, string> StatusMapping { get; set; } = new Dictionary<string, string>();

    [DataMember(Order = 7)]
    public string Title { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string Description { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public string DocumentHint { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}

public static class WorkerProductClassifier
{
    public static WorkerProductDescriptor Classify(string toolName, PermissionLevel permissionLevel, string capabilityPack = "", string skillGroup = "", string audience = "", string visibility = "")
    {
        var resolvedCapabilityPack = !string.IsNullOrWhiteSpace(capabilityPack)
            ? capabilityPack
            : InferCapabilityPack(toolName);
        var resolvedSkillGroup = !string.IsNullOrWhiteSpace(skillGroup)
            ? skillGroup
            : InferSkillGroup(toolName, permissionLevel);
        var resolvedAudience = !string.IsNullOrWhiteSpace(audience)
            ? audience
            : InferAudience(resolvedCapabilityPack);
        var resolvedVisibility = !string.IsNullOrWhiteSpace(visibility)
            ? visibility
            : InferVisibility(resolvedCapabilityPack);

        return new WorkerProductDescriptor(resolvedCapabilityPack, resolvedSkillGroup, resolvedAudience, resolvedVisibility);
    }

    private static string InferCapabilityPack(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return WorkerCapabilityPacks.CoreWorker;
        }

        if (toolName.StartsWith("family.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("script.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerCapabilityPacks.AutomationLab;
        }

        if (toolName.StartsWith("integration.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerCapabilityPacks.Connector;
        }

        if (toolName.StartsWith("task.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("context.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("memory.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("tool.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("intent.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("policy.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("specialist.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, ToolNames.SessionGetRuntimeHealth, StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, ToolNames.SessionGetQueueState, StringComparison.OrdinalIgnoreCase))
        {
            return WorkerCapabilityPacks.MemoryAndSoul;
        }

        return WorkerCapabilityPacks.CoreWorker;
    }

    private static string InferSkillGroup(string toolName, PermissionLevel permissionLevel)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return WorkerSkillGroups.RevitOps;
        }

        if (toolName.StartsWith("family.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("script.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerSkillGroups.Automation;
        }

        if (toolName.StartsWith("annotation.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerSkillGroups.Annotation;
        }

        if (toolName.StartsWith("spatial.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerSkillGroups.Coordination;
        }

        if (toolName.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerSkillGroups.Systems;
        }

        if (toolName.StartsWith("integration.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerSkillGroups.Integration;
        }

        if (toolName.StartsWith("intent.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("policy.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("specialist.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerSkillGroups.Intent;
        }

        if (toolName.StartsWith("task.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("context.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("memory.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("tool.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("workflow.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerSkillGroups.Orchestration;
        }

        if (toolName.StartsWith("review.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("audit.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("report.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerSkillGroups.QualityControl;
        }

        if (toolName.StartsWith("sheet.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("view.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("schedule.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("export.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("data.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerSkillGroups.Documentation;
        }

        if (toolName.StartsWith("workset.", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("parameter.", StringComparison.OrdinalIgnoreCase))
        {
            return WorkerSkillGroups.Governance;
        }

        return permissionLevel == PermissionLevel.Read && toolName.StartsWith("session.", StringComparison.OrdinalIgnoreCase)
            ? WorkerSkillGroups.Orchestration
            : WorkerSkillGroups.RevitOps;
    }

    private static string InferAudience(string capabilityPack)
    {
        if (string.Equals(capabilityPack, WorkerCapabilityPacks.AutomationLab, StringComparison.OrdinalIgnoreCase))
        {
            return WorkerAudience.Internal;
        }

        if (string.Equals(capabilityPack, WorkerCapabilityPacks.Connector, StringComparison.OrdinalIgnoreCase))
        {
            return WorkerAudience.Connector;
        }

        return WorkerAudience.Commercial;
    }

    private static string InferVisibility(string capabilityPack)
    {
        return string.Equals(capabilityPack, WorkerCapabilityPacks.AutomationLab, StringComparison.OrdinalIgnoreCase)
            ? WorkerVisibility.BetaInternal
            : WorkerVisibility.Visible;
    }
}

[DataContract]
public readonly struct WorkerProductDescriptor
{
    public WorkerProductDescriptor(string capabilityPack, string skillGroup, string audience, string visibility)
    {
        CapabilityPack = string.IsNullOrWhiteSpace(capabilityPack) ? WorkerCapabilityPacks.CoreWorker : capabilityPack;
        SkillGroup = string.IsNullOrWhiteSpace(skillGroup) ? WorkerSkillGroups.RevitOps : skillGroup;
        Audience = string.IsNullOrWhiteSpace(audience) ? WorkerAudience.Commercial : audience;
        Visibility = string.IsNullOrWhiteSpace(visibility) ? WorkerVisibility.Visible : visibility;
    }

    [DataMember(Order = 1)]
    public string CapabilityPack { get; }

    [DataMember(Order = 2)]
    public string SkillGroup { get; }

    [DataMember(Order = 3)]
    public string Audience { get; }

    [DataMember(Order = 4)]
    public string Visibility { get; }
}
