using System;
using System.Collections.Generic;
using System.IO;
using BIM765T.Revit.Agent.Core.Execution;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Validation;
using Xunit;

namespace BIM765T.Revit.Agent.Core.Tests;

public sealed class ToolExecutionCoreTests
{
    [Fact]
    public void Execute_Returns_UnsupportedTool_When_Manifest_Missing()
    {
        var clock = new FakeClock(new DateTime(2026, 3, 19, 8, 0, 0, DateTimeKind.Utc));
        var engine = new ToolExecutionCore(() => clock.UtcNow);

        var outcome = engine.Execute(
            new ToolRequestEnvelope
            {
                RequestId = "req-1",
                ToolName = "tool.missing",
                CorrelationId = "corr-1"
            },
            null,
            () => throw new InvalidOperationException("Should not run."));

        Assert.False(outcome.Response.Succeeded);
        Assert.Equal(StatusCodes.UnsupportedTool, outcome.Response.StatusCode);
        Assert.Equal(StatusCodes.UnsupportedTool, outcome.JournalEntry.StatusCode);
        Assert.Equal("corr-1", outcome.Response.CorrelationId);
        Assert.Equal("tool.missing", outcome.JournalEntry.ToolName);
    }

    [Fact]
    public void Execute_Fails_HighRisk_Tool_Without_Approval_Context_And_Preview()
    {
        var clock = new FakeClock(new DateTime(2026, 3, 19, 8, 0, 0, DateTimeKind.Utc));
        var engine = new ToolExecutionCore(() => clock.UtcNow);

        var outcome = engine.Execute(
            new ToolRequestEnvelope
            {
                RequestId = "req-2",
                ToolName = "element.delete_safe",
                DryRun = false
            },
            new ToolExecutionManifestInfo
            {
                ToolName = "element.delete_safe",
                Enabled = true,
                ApprovalRequirement = ApprovalRequirement.HighRiskToken
            },
            () => throw new InvalidOperationException("Should not run."));

        Assert.False(outcome.Response.Succeeded);
        Assert.Equal(StatusCodes.ApprovalInvalid, outcome.Response.StatusCode);
        Assert.Contains(outcome.Response.Diagnostics, x => x.Code == "HIGH_RISK_REQUIRES_APPROVAL");
    }

    [Fact]
    public void Execute_Fails_HighRisk_Tool_When_PreviewRunId_Is_Missing()
    {
        var engine = new ToolExecutionCore(() => new DateTime(2026, 3, 19, 8, 0, 0, DateTimeKind.Utc));

        var outcome = engine.Execute(
            new ToolRequestEnvelope
            {
                RequestId = "req-2b",
                ToolName = "element.delete_safe",
                DryRun = false,
                ApprovalToken = "token-1",
                ExpectedContextJson = "{\"DocumentKey\":\"path:a\"}"
            },
            new ToolExecutionManifestInfo
            {
                ToolName = "element.delete_safe",
                Enabled = true,
                ApprovalRequirement = ApprovalRequirement.HighRiskToken
            },
            () => throw new InvalidOperationException("Should not run."));

        Assert.Equal(StatusCodes.PreviewRunRequired, outcome.Response.StatusCode);
        Assert.Contains(outcome.Response.Diagnostics, x => x.Code == "HIGH_RISK_REQUIRES_PREVIEW_RUN");
    }

    [Fact]
    public void Execute_Returns_PolicyBlocked_When_Tool_Disabled()
    {
        var engine = new ToolExecutionCore(() => new DateTime(2026, 3, 19, 8, 0, 0, DateTimeKind.Utc));

        var outcome = engine.Execute(
            new ToolRequestEnvelope { RequestId = "req-disabled", ToolName = "file.save_document" },
            new ToolExecutionManifestInfo { ToolName = "file.save_document", Enabled = false },
            () => throw new InvalidOperationException("Should not run."));

        Assert.Equal(StatusCodes.PolicyBlocked, outcome.Response.StatusCode);
        Assert.Contains(outcome.Response.Diagnostics, x => x.Code == "TOOL_DISABLED_BY_POLICY");
    }

    [Fact]
    public void Execute_Maps_Exception_Types_To_Status_Codes()
    {
        var messages = new List<string>();
        var engine = new ToolExecutionCore(
            () => new DateTime(2026, 3, 19, 8, 0, 0, DateTimeKind.Utc),
            (message, _) => messages.Add(message));

        var outcome = engine.Execute(
            new ToolRequestEnvelope { RequestId = "req-3", ToolName = "review.model_health" },
            new ToolExecutionManifestInfo { ToolName = "review.model_health", Enabled = true },
            () => throw new InvalidDataException("broken payload"));

        Assert.False(outcome.Response.Succeeded);
        Assert.Equal(StatusCodes.InvalidRequest, outcome.Response.StatusCode);
        Assert.Contains(outcome.Response.Diagnostics, x => x.Code == "INVALID_PAYLOAD_JSON");
        Assert.Contains(messages, x => x.Contains("payload parse error", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Execute_Maps_RevitContextException_Only_For_True_Context_Failures()
    {
        var engine = new ToolExecutionCore(() => new DateTime(2026, 3, 19, 8, 0, 0, DateTimeKind.Utc));

        var contextFailure = engine.Execute(
            new ToolRequestEnvelope { RequestId = "req-context", ToolName = "document.get_active" },
            new ToolExecutionManifestInfo { ToolName = "document.get_active", Enabled = true },
            () => throw new RevitContextException("No active document."));

        Assert.Equal(StatusCodes.RevitContextMissing, contextFailure.Response.StatusCode);
        Assert.Contains(contextFailure.Response.Diagnostics, x => x.Code == "REVIT_CONTEXT_MISSING");

        var genericInvalidOperation = engine.Execute(
            new ToolRequestEnvelope { RequestId = "req-invalid-op", ToolName = "review.model_health" },
            new ToolExecutionManifestInfo { ToolName = "review.model_health", Enabled = true },
            () => throw new InvalidOperationException("Sequence contains no elements."));

        Assert.Equal(StatusCodes.InternalError, genericInvalidOperation.Response.StatusCode);
        Assert.Contains(genericInvalidOperation.Response.Diagnostics, x => x.Code == "TOOL_CRASH");
    }

    [Fact]
    public void Execute_Populates_Journal_Summary_For_Payload_And_Diagnostics()
    {
        var now = new DateTime(2026, 3, 19, 8, 0, 0, DateTimeKind.Utc);
        var engine = new ToolExecutionCore(() => now);
        var response = new ToolResponseEnvelope
        {
            RequestId = "req-4",
            ToolName = "review.model_health",
            Succeeded = true,
            StatusCode = StatusCodes.ReadSucceeded,
            PayloadJson = "{\"IssueCount\":2,\"Score\":97.5}",
            ConfirmationRequired = true,
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("WARN", DiagnosticSeverity.Warning, "First warning"),
                DiagnosticRecord.Create("INFO", DiagnosticSeverity.Info, "FYI")
            }
        };

        var outcome = engine.Execute(
            new ToolRequestEnvelope
            {
                RequestId = "req-4",
                ToolName = "review.model_health",
                CorrelationId = "corr-4",
                ProtocolVersion = ""
            },
            new ToolExecutionManifestInfo { ToolName = "review.model_health", Enabled = true },
            () => response);

        Assert.Equal("corr-4", outcome.Response.CorrelationId);
        Assert.Equal(BridgeProtocol.PipeV1, outcome.Response.ProtocolVersion);
        Assert.Equal("IssueCount: 2 | Score: 97.5 | Awaiting approval", outcome.JournalEntry.ResultSummary);
        Assert.Equal(1, outcome.JournalEntry.DiagnosticsWarningCount);
        Assert.Equal(1, outcome.JournalEntry.DiagnosticsInfoCount);
        Assert.Contains("First warning", outcome.JournalEntry.DiagnosticsSummary);
    }

    [Fact]
    public void Execute_Maps_Validation_Exception_Batch_To_InvalidRequest()
    {
        var engine = new ToolExecutionCore(() => new DateTime(2026, 3, 19, 8, 0, 0, DateTimeKind.Utc));
        var ex = new ToolPayloadValidationException(
            "bad payload",
            new[]
            {
                DiagnosticRecord.Create("BAD_FIELD", DiagnosticSeverity.Error, "Field invalid")
            });

        var outcome = engine.Execute(
            new ToolRequestEnvelope { RequestId = "req-5", ToolName = "tool.with.validation" },
            new ToolExecutionManifestInfo { ToolName = "tool.with.validation", Enabled = true },
            () => throw ex);

        Assert.Equal(StatusCodes.InvalidRequest, outcome.Response.StatusCode);
        Assert.Contains(outcome.Response.Diagnostics, x => x.Code == "BAD_FIELD");
        Assert.Equal("Field invalid", outcome.JournalEntry.ResultSummary);
    }

    [Fact]
    public void Execute_Maps_NotSupported_Exception_And_ChangedIds_Summary()
    {
        var engine = new ToolExecutionCore(() => new DateTime(2026, 3, 19, 8, 0, 0, DateTimeKind.Utc));

        var notImplemented = engine.Execute(
            new ToolRequestEnvelope { RequestId = "req-6", ToolName = "tool.not_implemented" },
            new ToolExecutionManifestInfo { ToolName = "tool.not_implemented", Enabled = true },
            () => throw new NotSupportedException("Not here yet."));

        Assert.Equal(StatusCodes.NotImplemented, notImplemented.Response.StatusCode);
        Assert.Contains(notImplemented.Response.Diagnostics, x => x.Code == "TOOL_NOT_IMPLEMENTED");

        var changedSummary = engine.Execute(
            new ToolRequestEnvelope { RequestId = "req-7", ToolName = "element.move_safe" },
            new ToolExecutionManifestInfo { ToolName = "element.move_safe", Enabled = true },
            () => new ToolResponseEnvelope
            {
                Succeeded = true,
                StatusCode = StatusCodes.ExecuteSucceeded,
                ChangedIds = new List<int> { 1, 2, 3 }
            });

        Assert.Equal("3 element(s) affected.", changedSummary.JournalEntry.ResultSummary);
        Assert.Equal("req-7", changedSummary.Response.RequestId);
        Assert.Equal(BridgeProtocol.PipeV1, changedSummary.Response.ProtocolVersion);
    }
}
