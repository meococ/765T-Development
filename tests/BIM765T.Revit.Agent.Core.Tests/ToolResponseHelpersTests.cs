using System.Collections.Generic;
using System.Runtime.Serialization;
using BIM765T.Revit.Agent.Services.Bridge;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class ToolResponseHelpersTests
{
    [Fact]
    public void ToolResponses_Success_Propagates_CorrelationId()
    {
        var request = new ToolRequestEnvelope
        {
            RequestId = "req-001",
            CorrelationId = "corr-001",
            ToolName = "review.model_health"
        };

        var response = ToolResponses.Success(request, new ResponsePayload { Count = 3 });

        Assert.Equal("req-001", response.RequestId);
        Assert.Equal("corr-001", response.CorrelationId);
        Assert.Equal(StatusCodes.ReadSucceeded, response.StatusCode);
        Assert.True(response.Succeeded);
    }

    [Fact]
    public void ToolResponses_ConfirmationRequired_Propagates_CorrelationId_And_PreviewRun()
    {
        var request = new ToolRequestEnvelope
        {
            RequestId = "req-002",
            CorrelationId = "corr-002",
            ToolName = "element.move_safe"
        };
        var execution = new ExecutionResult
        {
            ApprovalToken = "approval-002",
            PreviewRunId = "preview-002",
            ChangedIds = new List<int> { 10, 11 }
        };

        var response = ToolResponses.ConfirmationRequired(request, execution);

        Assert.Equal("corr-002", response.CorrelationId);
        Assert.True(response.ConfirmationRequired);
        Assert.Equal("preview-002", response.PreviewRunId);
        Assert.Equal("approval-002", response.ApprovalToken);
        Assert.Equal(2, response.ChangedIds.Count);
    }

    [Fact]
    public void ToolResponses_Failure_From_DiagnosticsEnumerable_Propagates_CorrelationId()
    {
        var request = new ToolRequestEnvelope
        {
            RequestId = "req-003",
            CorrelationId = "corr-003",
            ToolName = "workflow.fix_loop_apply"
        };
        var diagnostics = new[]
        {
            DiagnosticRecord.Create("BLOCKED", DiagnosticSeverity.Error, "blocked")
        };

        var response = ToolResponses.Failure(request, StatusCodes.WorkflowApplyBlocked, diagnostics);

        Assert.Equal("corr-003", response.CorrelationId);
        Assert.False(response.Succeeded);
        Assert.Equal(StatusCodes.WorkflowApplyBlocked, response.StatusCode);
        Assert.Single(response.Diagnostics);
    }

    [DataContract]
    private sealed class ResponsePayload
    {
        [DataMember(Order = 1)]
        public int Count { get; set; }
    }
}
