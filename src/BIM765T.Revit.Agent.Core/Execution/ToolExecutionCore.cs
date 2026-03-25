using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;
using BIM765T.Revit.Contracts.Validation;

namespace BIM765T.Revit.Agent.Core.Execution;

public sealed class ToolExecutionManifestInfo
{
    public string ToolName { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public ApprovalRequirement ApprovalRequirement { get; set; } = ApprovalRequirement.None;
}

public sealed class ToolExecutionOutcome
{
    public ToolExecutionOutcome(ToolResponseEnvelope response, OperationJournalEntry journalEntry)
    {
        Response = response ?? throw new ArgumentNullException(nameof(response));
        JournalEntry = journalEntry ?? throw new ArgumentNullException(nameof(journalEntry));
    }

    public ToolResponseEnvelope Response { get; }

    public OperationJournalEntry JournalEntry { get; }
}

public sealed class ToolExecutionCore
{
    private static readonly Regex CountRegex = new Regex("\"(?:Warning|Total|Element|Error|Issue|Item)Count\"\\s*:\\s*(\\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ScoreRegex = new Regex("\"(?:Health|Completeness)?Score\"\\s*:\\s*([\\d.]+)", RegexOptions.CultureInvariant);
    private readonly Func<DateTime> _utcNow;
    private readonly Action<string, Exception>? _logError;

    public ToolExecutionCore(Func<DateTime>? utcNow = null, Action<string, Exception>? logError = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _logError = logError;
    }

    public ToolExecutionOutcome Execute(ToolRequestEnvelope request, ToolExecutionManifestInfo? manifest, Func<ToolResponseEnvelope> invokeHandler)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (invokeHandler == null)
        {
            throw new ArgumentNullException(nameof(invokeHandler));
        }

        var started = _utcNow();
        var journalEntry = CreateJournalEntry(request, started);
        ToolResponseEnvelope response;

        try
        {
            if (manifest == null)
            {
                response = CreateFailure(request, StatusCodes.UnsupportedTool);
                return Finalize(request, response, journalEntry);
            }

            if (!manifest.Enabled)
            {
                response = CreateFailure(
                    request,
                    StatusCodes.PolicyBlocked,
                    DiagnosticRecord.Create("TOOL_DISABLED_BY_POLICY", DiagnosticSeverity.Warning, "Tool đang bị policy chặn."));
                return Finalize(request, response, journalEntry);
            }

            if (TryCreateHighRiskPreconditionFailure(manifest, request, out response))
            {
                return Finalize(request, response, journalEntry);
            }

            response = invokeHandler();
        }
        catch (NotSupportedException ex)
        {
            _logError?.Invoke("Tool not implemented: " + request.ToolName, ex);
            response = CreateFailure(request, StatusCodes.NotImplemented, DiagnosticRecord.Create("TOOL_NOT_IMPLEMENTED", DiagnosticSeverity.Error, ex.Message));
        }
        catch (RevitContextException ex)
        {
            _logError?.Invoke("Tool context error: " + request.ToolName, ex);
            response = CreateFailure(request, StatusCodes.RevitContextMissing, DiagnosticRecord.Create("REVIT_CONTEXT_MISSING", DiagnosticSeverity.Error, ex.Message));
        }
        catch (ToolPayloadValidationException ex)
        {
            _logError?.Invoke("Tool payload validation failed: " + request.ToolName, ex);
            response = CreateFailure(request, StatusCodes.InvalidRequest, ex.Diagnostics);
        }
        catch (InvalidDataException ex)
        {
            _logError?.Invoke("Tool payload parse error: " + request.ToolName, ex);
            response = CreateFailure(request, StatusCodes.InvalidRequest, DiagnosticRecord.Create("INVALID_PAYLOAD_JSON", DiagnosticSeverity.Error, ex.Message));
        }
        catch (Exception ex)
        {
            _logError?.Invoke("ToolExecutor crashed: " + request.ToolName, ex);
            response = CreateFailure(request, StatusCodes.InternalError, DiagnosticRecord.Create("TOOL_CRASH", DiagnosticSeverity.Error, ex.Message));
        }

        return Finalize(request, response, journalEntry);
    }

    private static OperationJournalEntry CreateJournalEntry(ToolRequestEnvelope request, DateTime startedUtc)
    {
        return new OperationJournalEntry
        {
            JournalId = Guid.NewGuid().ToString("N"),
            RequestId = request.RequestId,
            CorrelationId = request.CorrelationId,
            ToolName = request.ToolName,
            Caller = request.Caller,
            SessionId = request.SessionId,
            PreviewRunId = request.PreviewRunId,
            StartedUtc = startedUtc
        };
    }

    private bool TryCreateHighRiskPreconditionFailure(ToolExecutionManifestInfo manifest, ToolRequestEnvelope request, out ToolResponseEnvelope response)
    {
        response = new ToolResponseEnvelope();
        if (manifest.ApprovalRequirement != ApprovalRequirement.HighRiskToken || request.DryRun)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.ApprovalToken))
        {
            response = CreateFailure(
                request,
                StatusCodes.ApprovalInvalid,
                DiagnosticRecord.Create("HIGH_RISK_REQUIRES_APPROVAL", DiagnosticSeverity.Error, "Tool high-risk yêu cầu dry-run trước để lấy approval_token."));
            return true;
        }

        if (string.IsNullOrWhiteSpace(request.ExpectedContextJson))
        {
            response = CreateFailure(
                request,
                StatusCodes.ContextMismatch,
                DiagnosticRecord.Create("HIGH_RISK_REQUIRES_CONTEXT", DiagnosticSeverity.Error, "Tool high-risk yêu cầu expected_context khi execute."));
            return true;
        }

        if (string.IsNullOrWhiteSpace(request.PreviewRunId))
        {
            response = CreateFailure(
                request,
                StatusCodes.PreviewRunRequired,
                DiagnosticRecord.Create("HIGH_RISK_REQUIRES_PREVIEW_RUN", DiagnosticSeverity.Error, "Tool high-risk yêu cầu preview_run_id từ lần dry-run trước."));
            return true;
        }

        return false;
    }

    private ToolExecutionOutcome Finalize(ToolRequestEnvelope request, ToolResponseEnvelope response, OperationJournalEntry journalEntry)
    {
        var ended = _utcNow();
        journalEntry.EndedUtc = ended;
        journalEntry.Succeeded = response.Succeeded;
        journalEntry.StatusCode = response.StatusCode;
        journalEntry.ChangedIds = response.ChangedIds ?? new List<int>();
        PopulateResultSummary(journalEntry, response);

        response.DurationMs = (long)(ended - journalEntry.StartedUtc).TotalMilliseconds;
        response.RequestId = string.IsNullOrWhiteSpace(response.RequestId) ? request.RequestId : response.RequestId;
        response.ToolName = string.IsNullOrWhiteSpace(response.ToolName) ? request.ToolName : response.ToolName;
        response.CorrelationId = string.IsNullOrWhiteSpace(response.CorrelationId) ? request.CorrelationId : response.CorrelationId;
        response.ProtocolVersion = string.IsNullOrWhiteSpace(response.ProtocolVersion)
            ? BridgeProtocol.NormalizeOrDefault(request.ProtocolVersion)
            : BridgeProtocol.NormalizeOrDefault(response.ProtocolVersion);
        if (response.ExecutedAtUtc == default)
        {
            response.ExecutedAtUtc = ended;
        }

        return new ToolExecutionOutcome(response, journalEntry);
    }

    private static void PopulateResultSummary(OperationJournalEntry entry, ToolResponseEnvelope response)
    {
        foreach (var diag in response.Diagnostics ?? new List<DiagnosticRecord>())
        {
            switch (diag.Severity)
            {
                case DiagnosticSeverity.Error:
                    entry.DiagnosticsErrorCount++;
                    break;
                case DiagnosticSeverity.Warning:
                    entry.DiagnosticsWarningCount++;
                    break;
                case DiagnosticSeverity.Info:
                    entry.DiagnosticsInfoCount++;
                    break;
            }
        }

        var diagParts = new List<string>();
        var shown = 0;
        foreach (var diag in response.Diagnostics ?? new List<DiagnosticRecord>())
        {
            if (shown >= 3)
            {
                break;
            }

            diagParts.Add($"{diag.Severity}: {diag.Message}");
            shown++;
        }

        if ((response.Diagnostics?.Count ?? 0) > 3)
        {
            diagParts.Add($"... +{response.Diagnostics!.Count - 3} more");
        }

        entry.DiagnosticsSummary = string.Join("\n", diagParts);

        if (!string.IsNullOrWhiteSpace(response.PayloadJson) && response.PayloadJson.Length <= 2_000)
        {
            entry.ResultSummary = ExtractResultSummary(response);
            return;
        }

        if ((response.ChangedIds?.Count ?? 0) > 0)
        {
            entry.ResultSummary = $"{response.ChangedIds!.Count} element(s) affected.";
            return;
        }

        entry.ResultSummary = response.Succeeded
            ? "Completed successfully."
            : (response.Diagnostics?.Count ?? 0) > 0 ? response.Diagnostics![0].Message : response.StatusCode;
    }

    private static string ExtractResultSummary(ToolResponseEnvelope response)
    {
        var json = response.PayloadJson ?? string.Empty;
        var parts = new List<string>();
        var countMatch = CountRegex.Match(json);
        if (countMatch.Success)
        {
            var key = countMatch.Groups[0].Value.Split(':')[0].Trim().Trim('"');
            parts.Add(key + ": " + countMatch.Groups[1].Value);
        }

        var scoreMatch = ScoreRegex.Match(json);
        if (scoreMatch.Success)
        {
            parts.Add("Score: " + scoreMatch.Groups[1].Value);
        }

        if ((response.ChangedIds?.Count ?? 0) > 0)
        {
            parts.Add(response.ChangedIds!.Count + " element(s) changed");
        }

        if (response.ConfirmationRequired)
        {
            parts.Add("Awaiting approval");
        }

        if (parts.Count > 0)
        {
            return string.Join(" | ", parts);
        }

        return json.Length > 120 ? json.Substring(0, 120) + "..." : json;
    }

    private ToolResponseEnvelope CreateFailure(ToolRequestEnvelope request, string statusCode, params DiagnosticRecord[] diagnostics)
    {
        return CreateFailure(request, statusCode, (IEnumerable<DiagnosticRecord>)diagnostics);
    }

    private ToolResponseEnvelope CreateFailure(ToolRequestEnvelope request, string statusCode, IEnumerable<DiagnosticRecord> diagnostics)
    {
        return new ToolResponseEnvelope
        {
            RequestId = request.RequestId,
            ToolName = request.ToolName,
            CorrelationId = request.CorrelationId,
            ProtocolVersion = BridgeProtocol.NormalizeOrDefault(request.ProtocolVersion),
            ExecutedAtUtc = _utcNow(),
            Succeeded = false,
            StatusCode = statusCode,
            Diagnostics = diagnostics?.ToList() ?? new List<DiagnosticRecord>()
        };
    }
}
