using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using BIM765T.Revit.McpHost;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class McpToolCatalogTests
{
    [Fact]
    public void McpToolCatalogLoader_BridgeUnavailable_Throws_Clear_Error()
    {
        var response = new ToolResponseEnvelope
        {
            Succeeded = false,
            StatusCode = StatusCodes.BridgeUnavailable,
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("MCP_BRIDGE_CONNECT_FAILED", DiagnosticSeverity.Error, "Pipe connect failed.")
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => McpToolCatalogLoader.ParseOrThrow(response));

        Assert.Contains(StatusCodes.BridgeUnavailable, ex.Message);
        Assert.Contains("Pipe connect failed.", ex.Message);
    }

    [Fact]
    public void McpToolCatalogLoader_EmptyCatalog_FailsClosed()
    {
        var response = new ToolResponseEnvelope
        {
            Succeeded = true,
            StatusCode = StatusCodes.ReadSucceeded,
            PayloadJson = JsonUtil.Serialize(new ToolCatalogResponse())
        };

        var ex = Assert.Throws<InvalidOperationException>(() => McpToolCatalogLoader.ParseOrThrow(response));

        Assert.Contains("empty tool catalog", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void McpToolCatalogLoader_ValidCatalog_RoundTrips()
    {
        var catalog = new ToolCatalogResponse
        {
            Tools = new List<ToolManifest>
            {
                new ToolManifest
                {
                    ToolName = ToolNames.DocumentGetActive,
                    Description = "Get active document summary.",
                    PermissionLevel = PermissionLevel.Read,
                    Enabled = true
                }
            }
        };

        var response = new ToolResponseEnvelope
        {
            Succeeded = true,
            StatusCode = StatusCodes.ReadSucceeded,
            PayloadJson = JsonUtil.Serialize(catalog)
        };

        var parsed = McpToolCatalogLoader.ParseOrThrow(response);

        Assert.Single(parsed.Tools);
        Assert.Equal(ToolNames.DocumentGetActive, parsed.Tools[0].ToolName);
    }

    [Fact]
    public void ToolCatalog_Build_Uses_Manifest_Metadata_Without_HeuristicFallback()
    {
        var manifests = new[]
        {
            new ToolManifest
            {
                ToolName = ToolNames.WorkflowApply,
                Description = "Apply workflow.",
                PermissionLevel = PermissionLevel.Mutate,
                ApprovalRequirement = ApprovalRequirement.ConfirmToken,
                Enabled = true,
                SupportsDryRun = false,
                RequiredContext = new List<string> { "document", "workflow_run" },
                MutatesModel = true,
                TouchesActiveView = false,
                RequiresExpectedContext = true,
                BatchMode = "chunked",
                Idempotency = "checkpointed",
                PreviewArtifacts = new List<string> { "workflow_evidence" },
                RiskTags = new List<string> { "workflow", "mutation" },
                RulePackTags = new List<string> { "workflow_apply" },
                InputSchemaJson = "{\"type\":\"object\"}",
                RiskTier = ToolRiskTiers.Tier1,
                CanAutoExecute = false,
                LatencyClass = ToolLatencyClasses.Batch,
                UiSurface = ToolUiSurfaces.Queue,
                ProgressMode = ToolProgressModes.Heartbeat,
                RecommendedNextTools = new List<string> { ToolNames.SessionGetQueueState, ToolNames.ContextGetDeltaSummary },
                DomainGroup = "workflow",
                TaskFamily = "orchestration",
                PackId = "bim765t.agents.orchestrator",
                RecommendedPlaybooks = new List<string> { "sheet_create_arch_package.v1" }
            }
        };

        var tools = ToolCatalog.Build(manifests);
        var json = JsonSerializer.Serialize(tools[0]);
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.Equal(ToolNames.WorkflowApply, node["name"]!.GetValue<string>());
        Assert.Equal("chunked", node["annotations"]!["batchMode"]!.GetValue<string>());
        Assert.Equal("checkpointed", manifests[0].Idempotency);
        Assert.False(node["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.True(node["annotations"]!["idempotentHint"]!.GetValue<bool>());
        Assert.Equal("workflow_run", node["annotations"]!["requiredContext"]![1]!.GetValue<string>());
        Assert.Equal(ToolRiskTiers.Tier1, node["annotations"]!["riskTier"]!.GetValue<string>());
        Assert.Equal(ToolUiSurfaces.Queue, node["annotations"]!["uiSurface"]!.GetValue<string>());
        Assert.Equal(ToolNames.SessionGetQueueState, node["annotations"]!["recommendedNextTools"]![0]!.GetValue<string>());
        Assert.Equal("workflow", node["annotations"]!["domainGroup"]!.GetValue<string>());
        Assert.Equal("orchestration", node["annotations"]!["taskFamily"]!.GetValue<string>());
        Assert.Equal("bim765t.agents.orchestrator", node["annotations"]!["packId"]!.GetValue<string>());
        Assert.Equal("sheet_create_arch_package.v1", node["annotations"]!["recommendedPlaybooks"]![0]!.GetValue<string>());
    }
}
