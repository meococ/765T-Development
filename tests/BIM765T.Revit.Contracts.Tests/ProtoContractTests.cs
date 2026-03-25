using System.IO;
using BIM765T.Revit.Contracts.Proto;
using Google.Protobuf;

namespace BIM765T.Revit.Contracts.Tests;

public sealed class ProtoContractTests
{
    [Fact]
    public void MissionReply_RoundTrips_WithStatus_AndEvents()
    {
        var original = new MissionReply
        {
            MissionId = "mission-01",
            State = "AwaitingApproval",
            PayloadJson = """{"message":"preview"}""",
            ResponseText = "Preview đã sẵn sàng.",
            Status = new StatusEnvelope
            {
                Succeeded = true,
                StatusCode = "TASK_APPROVAL_REQUIRED",
                Message = "approval"
            }
        };
        original.Status.Diagnostics.Add("preview");
        original.Events.Add(new EventEnvelope
        {
            StreamId = "mission-01",
            Version = 1,
            EventType = "PreviewGenerated",
            PayloadJson = """{"tool":"worker.message"}""",
            OccurredUtc = "2026-03-20T10:00:00Z",
            CorrelationId = "corr-01",
            CausationId = "cause-01",
            ActorId = "tester",
            DocumentKey = "doc-01",
            Terminal = false
        });

        var clone = MissionReply.Parser.ParseFrom(original.ToByteArray());

        Assert.Equal(original.MissionId, clone.MissionId);
        Assert.Equal(original.State, clone.State);
        Assert.Equal(original.ResponseText, clone.ResponseText);
        Assert.True(clone.Status.Succeeded);
        Assert.Single(clone.Events);
        Assert.Equal("PreviewGenerated", clone.Events[0].EventType);
    }

    [Fact]
    public void KernelInvokeRequest_DelimitedRoundTrip_PreservesEnvelopeFields()
    {
        var original = new KernelInvokeRequest
        {
            CorrelationId = "corr-01",
            CausationId = "cause-01",
            MissionId = "mission-01",
            ActorId = "tester",
            DocumentKey = "doc-01",
            RequestedAtUtc = "2026-03-20T10:00:00Z",
            TimeoutMs = 120000,
            CancellationTokenId = "cancel-01",
            RequestId = "req-01",
            ToolName = "worker.message",
            PayloadJson = """{"Message":"approve"}""",
            Caller = "tests",
            SessionId = "session-01",
            DryRun = true,
            TargetDocument = "doc-01",
            TargetView = "view-01",
            ExpectedContextJson = """{"fingerprint":"abc"}""",
            ApprovalToken = "approval-01",
            ScopeDescriptorJson = """{"scope":"selection"}""",
            PreviewRunId = "preview-01"
        };

        using var stream = new MemoryStream();
        original.WriteDelimitedTo(stream);
        stream.Position = 0;

        var clone = KernelInvokeRequest.Parser.ParseDelimitedFrom(stream);

        Assert.NotNull(clone);
        Assert.Equal(original.CorrelationId, clone!.CorrelationId);
        Assert.Equal(original.MissionId, clone.MissionId);
        Assert.Equal(original.ToolName, clone.ToolName);
        Assert.Equal(original.PreviewRunId, clone.PreviewRunId);
        Assert.Equal(original.ExpectedContextJson, clone.ExpectedContextJson);
    }
}
