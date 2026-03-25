using System;
using BIM765T.Revit.Agent.Services.Platform;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;

namespace BIM765T.Revit.Agent.Services.Bridge;

/// <summary>
/// Registers script orchestration tools: list, validate, run, get_run, compose.
///
/// Provides an automation layer above individual family authoring tools,
/// allowing multi-step scripted operations with validation, tracking, and
/// composed execution.
/// </summary>
internal sealed class ScriptOrchestrationToolModule : IToolModule
{
    private readonly ToolModuleContext _context;
    private readonly ScriptOrchestrationService _scripts;

    internal ScriptOrchestrationToolModule(ToolModuleContext context)
    {
        _context = context;
        _scripts = new ScriptOrchestrationService();
    }

    public void Register(ToolRegistry registry)
    {
        var platform = _context.Platform;
        var readNoContext = ToolManifestPresets.Read()
            .WithCapabilityPack(WorkerCapabilityPacks.AutomationLab)
            .WithSkillGroup(WorkerSkillGroups.Automation)
            .WithAudience(WorkerAudience.Internal)
            .WithVisibility(WorkerVisibility.BetaInternal);
        var readDocument = ToolManifestPresets.Read("document")
            .WithCapabilityPack(WorkerCapabilityPacks.AutomationLab)
            .WithSkillGroup(WorkerSkillGroups.Automation)
            .WithAudience(WorkerAudience.Internal)
            .WithVisibility(WorkerVisibility.BetaInternal);
        var mutationDocument = ToolManifestPresets.Mutation("document")
            .WithCapabilityPack(WorkerCapabilityPacks.AutomationLab)
            .WithSkillGroup(WorkerSkillGroups.Automation)
            .WithAudience(WorkerAudience.Internal)
            .WithVisibility(WorkerVisibility.BetaInternal);

        // ── READ TOOLS ──

        registry.Register(
            ToolNames.ScriptList,
            "List available scripts (built-in and user-defined) for family authoring and automation.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) => ToolResponses.Success(request, _scripts.ListScripts()));

        registry.Register(
            ToolNames.ScriptValidate,
            "Validate a script (by ScriptId or inline code) for safety and syntax before execution.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ScriptValidateRequest>(request);
                return ToolResponses.Success(request, _scripts.Validate(payload));
            },
            "{\"ScriptId\":\"\",\"InlineCode\":\"\"}");

        registry.Register(
            ToolNames.ScriptGetRun,
            "Get the result of a previously executed script by RunId.",
            PermissionLevel.Read,
            ApprovalRequirement.None,
            false,
            readNoContext,
            (uiapp, request) =>
            {
                var payload = ToolPayloads.Read<ScriptGetRunRequest>(request);
                return ToolResponses.Success(request, _scripts.GetRun(payload.RunId));
            },
            "{\"RunId\":\"\"}");

        // ── MUTATION TOOLS ──

        registry.RegisterMutationTool<ScriptRunRequest>(
            ToolNames.ScriptRunSafe,
            "Execute a validated script (built-in or user) on the current document with dry-run and approval.",
            ApprovalRequirement.ConfirmToken,
            "{\"ScriptId\":\"\",\"InlineCode\":\"\",\"Parameters\":{},\"TimeoutMs\":30000}",
            mutationDocument,
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (uiapp, services, doc, payload, request) => _scripts.PreviewRun(services, doc, payload, request),
            (uiapp, services, doc, payload) => _scripts.ExecuteRun(services, doc, uiapp, payload));

        registry.RegisterMutationTool<ScriptComposeRequest>(
            ToolNames.ScriptComposeSafe,
            "Execute a composed multi-step script sequence with dry-run preview and abort-on-failure control.",
            ApprovalRequirement.HighRiskToken,
            "{\"Steps\":[{\"ScriptId\":\"\",\"Parameters\":{},\"ContinueOnError\":false}],\"TimeoutMs\":60000}",
            mutationDocument.WithRiskTags("composed", "multi-step"),
            () => platform.Settings.AllowWriteTools,
            StatusCodes.WriteDisabled,
            (uiapp, request, payload) => platform.ResolveDocument(uiapp, request.TargetDocument),
            null,
            (uiapp, services, doc, payload, request) => _scripts.PreviewCompose(services, doc, payload, request),
            (uiapp, services, doc, payload) => _scripts.ExecuteCompose(services, doc, uiapp, payload));
    }
}
