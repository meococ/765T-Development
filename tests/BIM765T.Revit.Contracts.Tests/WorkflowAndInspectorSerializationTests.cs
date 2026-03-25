using System.Collections.Generic;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class WorkflowAndInspectorSerializationTests
{
    [Fact]
    public void WorkflowDefinition_RoundTrips_With_Steps_And_Metadata()
    {
        var dto = new WorkflowDefinition
        {
            WorkflowName = "workflow.model_health",
            DisplayName = "Model Health",
            Description = "QC workflow",
            Category = "qc",
            SupportsApply = true,
            RequiresApproval = false,
            RiskTags = new List<string> { "qc" },
            RulePackTags = new List<string> { "document_health_v1" },
            InputSchemaJson = "{\"type\":\"object\"}",
            Steps = new List<WorkflowStep>
            {
                new WorkflowStep
                {
                    StepName = "task_context",
                    ToolName = "session.get_task_context",
                    Mode = "read",
                    Description = "Capture context",
                    IsCheckpoint = true
                }
            }
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<WorkflowDefinition>(json);

        Assert.Equal("workflow.model_health", result.WorkflowName);
        Assert.Single(result.Steps);
        Assert.Equal("task_context", result.Steps[0].StepName);
        Assert.Contains("document_health_v1", result.RulePackTags);
    }

    [Fact]
    public void WorkflowRun_RoundTrips_With_Evidence_And_Context()
    {
        var dto = new WorkflowRun
        {
            RunId = "run-123",
            WorkflowName = "workflow.parameter_rollout",
            Status = "awaiting_approval",
            DocumentKey = "path:c:\\test.rvt",
            Fingerprint = new ContextFingerprint
            {
                DocumentKey = "path:c:\\test.rvt",
                ViewKey = "view:99",
                SelectionCount = 2,
                SelectionHash = "hash-1",
                SelectedElementIds = new List<int> { 10, 11 },
                ActiveDocEpoch = 7
            },
            ApprovalToken = "token-1",
            PreviewRunId = "preview-1",
            MutationToolName = "parameter.batch_fill_safe",
            MutationPayloadJson = "{}",
            ExpectedContextJson = "{\"DocumentKey\":\"path:c:\\\\test.rvt\"}",
            Caller = "test-caller",
            SessionId = "test-session",
            Checkpoints = new List<WorkflowCheckpoint>
            {
                new WorkflowCheckpoint
                {
                    StepName = "dry_run",
                    Status = "completed",
                    ArtifactKeys = new List<string> { "dry_run:1" },
                    ChangedIds = new List<int> { 10 }
                }
            },
            Evidence = new WorkflowEvidenceBundle
            {
                RunId = "run-123",
                PlanSummary = "parameter rollout",
                ArtifactKeys = new List<string> { "dry_run:1" },
                SnapshotPayloads = new List<string> { "{\"snapshot\":true}" },
                ReviewPayloads = new List<string> { "{\"review\":true}" },
                ResultPayloads = new List<string> { "{\"result\":true}" }
            },
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("WARN", DiagnosticSeverity.Warning, "test warning")
            },
            ChangedIds = new List<int> { 10, 11 },
            DurationMs = 1500
        };

        var json = JsonUtil.Serialize(dto);
        var result = JsonUtil.DeserializeRequired<WorkflowRun>(json);

        Assert.Equal("run-123", result.RunId);
        Assert.Equal("preview-1", result.PreviewRunId);
        Assert.Equal("hash-1", result.Fingerprint.SelectionHash);
        Assert.Single(result.Checkpoints);
        Assert.Single(result.Evidence.SnapshotPayloads);
        Assert.Equal(2, result.ChangedIds.Count);
        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void InspectorDtos_RoundTrip()
    {
        var explain = new ElementExplainResponse
        {
            DocumentKey = "path:c:\\test.rvt",
            OwnerViewKey = "view:1",
            OwnerViewId = 1,
            HostElementId = 2,
            HostCategoryName = "Walls",
            DependentElementIds = new List<int> { 3, 4 },
            Explanations = new List<string> { "Selected in active view" }
        };

        var graph = new ElementGraphResponse
        {
            DocumentKey = "path:c:\\test.rvt",
            Nodes = new List<GraphNodeDto> { new GraphNodeDto { ElementId = 1, Label = "Wall 1", Kind = "element" } },
            Edges = new List<GraphEdgeDto> { new GraphEdgeDto { FromElementId = 1, ToElementId = 2, Relation = "host" } }
        };

        var explainJson = JsonUtil.Serialize(explain);
        var graphJson = JsonUtil.Serialize(graph);
        var explainResult = JsonUtil.DeserializeRequired<ElementExplainResponse>(explainJson);
        var graphResult = JsonUtil.DeserializeRequired<ElementGraphResponse>(graphJson);

        Assert.Equal("Walls", explainResult.HostCategoryName);
        Assert.Equal(2, explainResult.DependentElementIds.Count);
        Assert.Single(graphResult.Nodes);
        Assert.Single(graphResult.Edges);
        Assert.Equal("host", graphResult.Edges[0].Relation);
    }
}
