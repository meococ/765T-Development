using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Serialization;
using Xunit;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class ToolMessagesSerializationTests
{
    [Fact]
    public void ToolRequestEnvelope_RoundTrips()
    {
        var request = new ToolRequestEnvelope
        {
            ToolName = "element.query",
            PayloadJson = "{\"MaxResults\":10}",
            Caller = "test",
            DryRun = false,
            TargetDocument = "path:c:\\test.rvt",
            PreviewRunId = "preview-123",
            CorrelationId = "corr-request-001",
            ProtocolVersion = BridgeProtocol.PipeV1,
            RequestedPriority = ToolQueuePriorities.High
        };

        var json = JsonUtil.Serialize(request);
        var deserialized = JsonUtil.DeserializeRequired<ToolRequestEnvelope>(json);

        Assert.Equal("element.query", deserialized.ToolName);
        Assert.Equal("{\"MaxResults\":10}", deserialized.PayloadJson);
        Assert.Equal("test", deserialized.Caller);
        Assert.False(deserialized.DryRun);
        Assert.Equal("path:c:\\test.rvt", deserialized.TargetDocument);
        Assert.Equal("preview-123", deserialized.PreviewRunId);
        Assert.Equal("corr-request-001", deserialized.CorrelationId);
        Assert.Equal(BridgeProtocol.PipeV1, deserialized.ProtocolVersion);
        Assert.Equal(ToolQueuePriorities.High, deserialized.RequestedPriority);
    }

    [Fact]
    public void ToolResponseEnvelope_RoundTrips()
    {
        var response = new ToolResponseEnvelope
        {
            RequestId = "abc123",
            ToolName = "session.list_tools",
            Succeeded = true,
            StatusCode = "OK",
            DurationMs = 42,
            PreviewRunId = "preview-456",
            CorrelationId = "corr-response-001",
            ProtocolVersion = BridgeProtocol.PipeV1,
            Stage = WorkerStages.Verification,
            Progress = 100,
            ExecutionTier = WorkerExecutionTiers.Tier1
        };

        var json = JsonUtil.Serialize(response);
        var deserialized = JsonUtil.DeserializeRequired<ToolResponseEnvelope>(json);

        Assert.Equal("abc123", deserialized.RequestId);
        Assert.True(deserialized.Succeeded);
        Assert.Equal("OK", deserialized.StatusCode);
        Assert.Equal(42, deserialized.DurationMs);
        Assert.Equal("preview-456", deserialized.PreviewRunId);
        Assert.Equal("corr-response-001", deserialized.CorrelationId);
        Assert.Equal(BridgeProtocol.PipeV1, deserialized.ProtocolVersion);
        Assert.Equal(WorkerStages.Verification, deserialized.Stage);
        Assert.Equal(100, deserialized.Progress);
        Assert.Equal(WorkerExecutionTiers.Tier1, deserialized.ExecutionTier);
    }

    [Fact]
    public void ToolRequestEnvelope_Defaults_DryRun_To_True()
    {
        var request = new ToolRequestEnvelope();
        Assert.True(request.DryRun);
    }

    [Fact]
    public void ToolRequestEnvelope_Defaults_Caller_To_Unknown()
    {
        var request = new ToolRequestEnvelope();
        Assert.Equal("unknown", request.Caller);
    }

    [Fact]
    public void ToolResponseEnvelope_Defaults_To_Empty_Collections()
    {
        var response = new ToolResponseEnvelope();
        Assert.NotNull(response.Diagnostics);
        Assert.Empty(response.Diagnostics);
        Assert.NotNull(response.ChangedIds);
        Assert.Empty(response.ChangedIds);
        Assert.NotNull(response.Artifacts);
        Assert.Empty(response.Artifacts);
    }

    [Fact]
    public void ToolRequestEnvelope_Defaults_CorrelationId_To_Empty()
    {
        var request = new ToolRequestEnvelope();
        Assert.Equal(string.Empty, request.CorrelationId);
        Assert.Equal(ToolQueuePriorities.Normal, request.RequestedPriority);
    }

    [Fact]
    public void ToolResponseEnvelope_Defaults_CorrelationId_To_Empty()
    {
        var response = new ToolResponseEnvelope();
        Assert.Equal(string.Empty, response.CorrelationId);
    }

    [Fact]
    public void ToolRequestEnvelope_Defaults_ProtocolVersion_To_PipeV1()
    {
        var request = new ToolRequestEnvelope();

        Assert.Equal(BridgeProtocol.PipeV1, request.ProtocolVersion);
    }

    [Fact]
    public void ToolResponseEnvelope_Defaults_ProtocolVersion_To_PipeV1()
    {
        var response = new ToolResponseEnvelope();

        Assert.Equal(BridgeProtocol.PipeV1, response.ProtocolVersion);
    }
}
