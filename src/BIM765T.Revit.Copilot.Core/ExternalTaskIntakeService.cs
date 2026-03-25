using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Copilot.Core;

public sealed class ExternalTaskIntakeService
{
    public TaskPlanRequest Normalize(ExternalTaskIntakeRequest? request, WorkerProfile? defaultWorkerProfile = null)
    {
        request ??= new ExternalTaskIntakeRequest();
        var envelope = request.Envelope ?? new ConnectorTaskEnvelope();
        var normalizedWorker = MergeWorkerProfile(request.WorkerProfile, defaultWorkerProfile);
        var taskSpec = BuildTaskSpec(request, envelope);

        return new TaskPlanRequest
        {
            DocumentKey = request.DocumentKey ?? string.Empty,
            TaskKind = string.IsNullOrWhiteSpace(request.TaskKind) ? "workflow" : request.TaskKind.Trim(),
            TaskName = ResolveTaskName(request, envelope),
            IntentSummary = ResolveIntentSummary(request, envelope),
            InputJson = string.IsNullOrWhiteSpace(request.InputJson) ? "{}" : request.InputJson,
            Tags = BuildTags(request.Tags, envelope),
            TaskSpec = taskSpec,
            WorkerProfile = normalizedWorker,
            PreferredCapabilityPack = request.PreferredCapabilityPack ?? string.Empty,
            ConnectorTask = CloneEnvelope(envelope)
        };
    }

    public string BuildSummary(ConnectorTaskEnvelope? envelope)
    {
        envelope ??= new ConnectorTaskEnvelope();
        var system = string.IsNullOrWhiteSpace(envelope.ExternalSystem) ? "connector" : envelope.ExternalSystem;
        var reference = string.IsNullOrWhiteSpace(envelope.ExternalTaskRef) ? "<unassigned>" : envelope.ExternalTaskRef;
        var title = string.IsNullOrWhiteSpace(envelope.Title) ? reference : envelope.Title;
        return $"{system}:{reference} -> {title}";
    }

    private static TaskSpec BuildTaskSpec(ExternalTaskIntakeRequest request, ConnectorTaskEnvelope envelope)
    {
        var spec = request.TaskSpec ?? new TaskSpec();
        spec.Source = string.IsNullOrWhiteSpace(spec.Source)
            ? (string.IsNullOrWhiteSpace(envelope.ExternalSystem) ? "connector" : envelope.ExternalSystem)
            : spec.Source;
        spec.Goal = string.IsNullOrWhiteSpace(spec.Goal)
            ? ResolveIntentSummary(request, envelope)
            : spec.Goal;
        spec.DocumentScope = string.IsNullOrWhiteSpace(spec.DocumentScope) ? request.DocumentKey ?? string.Empty : spec.DocumentScope;
        spec.ProjectScope = string.IsNullOrWhiteSpace(spec.ProjectScope) ? envelope.ProjectRef ?? string.Empty : spec.ProjectScope;
        spec.Constraints ??= new List<string>();
        spec.ApprovalPolicy ??= new TaskApprovalPolicy();
        if (spec.ApprovalPolicy.MaxBatchSize <= 0)
        {
            spec.ApprovalPolicy.MaxBatchSize = 100;
        }

        var callback = spec.CallbackTarget ?? new TaskCallbackTarget();
        callback.System = string.IsNullOrWhiteSpace(callback.System) ? envelope.ExternalSystem ?? string.Empty : callback.System;
        callback.Reference = string.IsNullOrWhiteSpace(callback.Reference) ? envelope.ExternalTaskRef ?? string.Empty : callback.Reference;
        callback.Mode = string.IsNullOrWhiteSpace(callback.Mode) ? envelope.CallbackMode ?? "panel_only" : callback.Mode;
        callback.Destination = string.IsNullOrWhiteSpace(callback.Destination) ? envelope.ProjectRef ?? string.Empty : callback.Destination;
        spec.CallbackTarget = callback;

        var callbackMode = envelope.CallbackMode ?? string.Empty;
        if (!spec.ApprovalPolicy.AllowQueuedExecution
            && !string.IsNullOrWhiteSpace(callbackMode)
            && (callbackMode.IndexOf("queue", StringComparison.OrdinalIgnoreCase) >= 0
                || callbackMode.IndexOf("async", StringComparison.OrdinalIgnoreCase) >= 0
                || callbackMode.IndexOf("off", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            spec.ApprovalPolicy.AllowQueuedExecution = true;
        }

        spec.Deliverables ??= new List<TaskDeliverableSpec>();
        return spec;
    }

    private static WorkerProfile MergeWorkerProfile(WorkerProfile? requested, WorkerProfile? defaults)
    {
        defaults ??= new WorkerProfile();
        if (requested == null || LooksImplicitDefaultProfile(requested))
        {
            return CloneProfile(defaults);
        }

        requested.PersonaId = string.IsNullOrWhiteSpace(requested.PersonaId) ? defaults.PersonaId : requested.PersonaId;
        requested.Tone = string.IsNullOrWhiteSpace(requested.Tone) ? defaults.Tone : requested.Tone;
        requested.QaStrictness = string.IsNullOrWhiteSpace(requested.QaStrictness) ? defaults.QaStrictness : requested.QaStrictness;
        requested.RiskTolerance = string.IsNullOrWhiteSpace(requested.RiskTolerance) ? defaults.RiskTolerance : requested.RiskTolerance;
        requested.EscalationStyle = string.IsNullOrWhiteSpace(requested.EscalationStyle) ? defaults.EscalationStyle : requested.EscalationStyle;
        requested.AllowedSkillGroups ??= new List<string>();
        if (requested.AllowedSkillGroups.Count == 0 && defaults.AllowedSkillGroups != null && defaults.AllowedSkillGroups.Count > 0)
        {
            requested.AllowedSkillGroups = defaults.AllowedSkillGroups.ToList();
        }

        return requested;
    }

    private static WorkerProfile CloneProfile(WorkerProfile profile)
    {
        return new WorkerProfile
        {
            PersonaId = profile.PersonaId ?? WorkerPersonas.FreelancerDefault,
            Tone = profile.Tone ?? "pragmatic",
            QaStrictness = profile.QaStrictness ?? "standard",
            RiskTolerance = profile.RiskTolerance ?? "guarded",
            EscalationStyle = profile.EscalationStyle ?? "checkpoint_first",
            AllowedSkillGroups = profile.AllowedSkillGroups != null
                ? profile.AllowedSkillGroups.ToList()
                : new List<string>()
        };
    }

    private static bool LooksImplicitDefaultProfile(WorkerProfile profile)
    {
        return string.Equals(profile.PersonaId, WorkerPersonas.FreelancerDefault, StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.Tone, "pragmatic", StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.QaStrictness, "standard", StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.RiskTolerance, "guarded", StringComparison.OrdinalIgnoreCase)
            && string.Equals(profile.EscalationStyle, "checkpoint_first", StringComparison.OrdinalIgnoreCase)
            && (profile.AllowedSkillGroups == null || profile.AllowedSkillGroups.Count == 0);
    }

    private static List<string> BuildTags(IEnumerable<string>? requestTags, ConnectorTaskEnvelope envelope)
    {
        var tags = new List<string>();
        if (requestTags != null)
        {
            tags.AddRange(requestTags.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));
        }

        AddTag(tags, "external_task");
        if (!string.IsNullOrWhiteSpace(envelope.ExternalSystem))
        {
            AddTag(tags, "source:" + envelope.ExternalSystem.Trim());
        }

        if (!string.IsNullOrWhiteSpace(envelope.ProjectRef))
        {
            AddTag(tags, "project:" + envelope.ProjectRef.Trim());
        }

        return tags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveTaskName(ExternalTaskIntakeRequest request, ConnectorTaskEnvelope envelope)
    {
        if (!string.IsNullOrWhiteSpace(request.TaskName))
        {
            return request.TaskName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(envelope.Title))
        {
            return envelope.Title.Trim();
        }

        if (!string.IsNullOrWhiteSpace(envelope.ExternalTaskRef))
        {
            return envelope.ExternalTaskRef.Trim();
        }

        return "external_task";
    }

    private static string ResolveIntentSummary(ExternalTaskIntakeRequest request, ConnectorTaskEnvelope envelope)
    {
        if (!string.IsNullOrWhiteSpace(request.IntentSummary))
        {
            return request.IntentSummary.Trim();
        }

        var goal = request.TaskSpec?.Goal;
        if (!string.IsNullOrWhiteSpace(goal))
        {
            return goal?.Trim() ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(envelope.Description))
        {
            return envelope.Description.Trim();
        }

        return ResolveTaskName(request, envelope);
    }

    private static ConnectorTaskEnvelope CloneEnvelope(ConnectorTaskEnvelope envelope)
    {
        return new ConnectorTaskEnvelope
        {
            ExternalSystem = envelope.ExternalSystem ?? string.Empty,
            ExternalTaskRef = envelope.ExternalTaskRef ?? string.Empty,
            ProjectRef = envelope.ProjectRef ?? string.Empty,
            AuthContext = envelope.AuthContext ?? string.Empty,
            CallbackMode = envelope.CallbackMode ?? "panel_only",
            StatusMapping = envelope.StatusMapping != null
                ? new Dictionary<string, string>(envelope.StatusMapping, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Title = envelope.Title ?? string.Empty,
            Description = envelope.Description ?? string.Empty,
            DocumentHint = envelope.DocumentHint ?? string.Empty,
            Metadata = envelope.Metadata != null
                ? new Dictionary<string, string>(envelope.Metadata, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
    }

    private static void AddTag(ICollection<string> tags, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            tags.Add(value.Trim());
        }
    }
}
