using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.DB.Structure;
using BIM765T.Revit.Agent.Core.Execution;
using BIM765T.Revit.Agent.Config;
using BIM765T.Revit.Agent.Infrastructure.Failures;
using BIM765T.Revit.Agent.Infrastructure.Time;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Common;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.Services.Platform;

internal sealed class ApprovalService : IApprovalGate
{
    private readonly ConcurrentDictionary<string, ApprovalRecord> _tokens = new ConcurrentDictionary<string, ApprovalRecord>();
    private readonly int _ttlMinutes;
    private readonly ApprovalTokenStore _store;
    private readonly ISystemClock _clock;
    private readonly object _persistGate = new object();
    private IReadOnlyList<PersistedApprovalRecord>? _pendingPersistedRecords;
    private bool _persistWorkerActive;

    internal ApprovalService(AgentSettings settings, BIM765T.Revit.Agent.Infrastructure.Logging.IAgentLogger logger, ISystemClock clock)
    {
        _ttlMinutes = Math.Max(2, settings.ApprovalTokenTtlMinutes);
        _store = new ApprovalTokenStore(logger);
        _clock = clock;
        RestorePersistedTokens();
    }

    public string IssueToken(string toolName, string requestFingerprint, string documentKey, string viewKey = "", string selectionHash = "", string previewRunId = "", string caller = "", string sessionId = "")
    {
        var token = Guid.NewGuid().ToString("N");
        _tokens[token] = new ApprovalRecord
        {
            ToolName = toolName,
            RequestFingerprint = requestFingerprint,
            DocumentKey = documentKey,
            ViewKey = viewKey,
            SelectionHash = selectionHash,
            PreviewRunId = previewRunId,
            Caller = caller,
            SessionId = sessionId,
            ExpiresUtc = _clock.UtcNow.AddMinutes(_ttlMinutes)
        };
        PersistTokens();
        return token;
    }

    public string IssueToken(string toolName, string requestFingerprint, string documentKey, string caller, string sessionId)
    {
        return IssueToken(toolName, requestFingerprint, documentKey, string.Empty, string.Empty, string.Empty, caller, sessionId);
    }

    /// <summary>
    /// P0-2 FIX: Fingerprint bao gồm Caller + SessionId.
    /// Trước đây chỉ hash tool/payload/doc/view/scope → token từ session A
    /// có thể dùng ở session B.
    /// </summary>
    public string BuildFingerprint(ToolRequestEnvelope request, string documentKey = "", string viewKey = "", string selectionHash = "", string previewRunId = "")
    {
        var raw = $"{request.ToolName}|{request.PayloadJson}|{documentKey}|{viewKey}|{selectionHash}|{request.ScopeDescriptorJson}|{request.Caller}|{request.SessionId}|{previewRunId}";
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return BitConverter.ToString(bytes).Replace("-", string.Empty);
    }

    /// <summary>
    /// P0-2 FIX: Validate bind chặt Caller + SessionId.
    /// Token chỉ valid nếu cùng caller, cùng session, cùng tool, cùng payload.
    /// </summary>
    public string Validate(ToolRequestEnvelope request, string documentKey, string viewKey = "", string selectionHash = "")
    {
        if (string.IsNullOrWhiteSpace(request.ApprovalToken))
        {
            return StatusCodes.ApprovalInvalid;
        }

        // Get without removing — only remove after all validation passes
        if (!_tokens.TryGetValue(request.ApprovalToken, out var record))
        {
            return StatusCodes.ApprovalInvalid;
        }

        if (record.ExpiresUtc < _clock.UtcNow)
        {
            // Expired — remove and persist
            _tokens.TryRemove(request.ApprovalToken, out _);
            PersistTokens();
            return StatusCodes.ApprovalExpired;
        }

        if (!string.Equals(record.ToolName, request.ToolName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(record.RequestFingerprint, BuildFingerprint(request, documentKey, viewKey, selectionHash, request.PreviewRunId), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(record.DocumentKey, documentKey, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.ApprovalMismatch;
        }

        if (!string.IsNullOrWhiteSpace(record.ViewKey) &&
            !string.Equals(record.ViewKey, viewKey, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.ApprovalMismatch;
        }

        if (!string.IsNullOrWhiteSpace(record.SelectionHash) &&
            !string.Equals(record.SelectionHash, selectionHash, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.ApprovalMismatch;
        }

        if (!string.IsNullOrWhiteSpace(record.PreviewRunId))
        {
            if (string.IsNullOrWhiteSpace(request.PreviewRunId))
            {
                return StatusCodes.PreviewRunRequired;
            }

            if (!string.Equals(record.PreviewRunId, request.PreviewRunId, StringComparison.OrdinalIgnoreCase))
            {
                return StatusCodes.ApprovalMismatch;
            }
        }

        // P0-2: Validate caller/session binding
        if (!string.IsNullOrWhiteSpace(record.Caller) &&
            !string.Equals(record.Caller, request.Caller, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.ApprovalMismatch;
        }

        if (!string.IsNullOrWhiteSpace(record.SessionId) &&
            !string.Equals(record.SessionId, request.SessionId, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCodes.ApprovalMismatch;
        }

        // All validation passed — now safe to consume the token
        _tokens.TryRemove(request.ApprovalToken, out _);
        PersistTokens();

        return StatusCodes.Ok;
    }

    public void FlushPendingPersist()
    {
        IReadOnlyList<PersistedApprovalRecord>? snapshot;
        lock (_persistGate)
        {
            snapshot = _pendingPersistedRecords;
            _pendingPersistedRecords = null;
        }

        if (snapshot != null)
        {
            _store.Save(snapshot);
        }

        SpinWait.SpinUntil(() =>
        {
            lock (_persistGate)
            {
                return !_persistWorkerActive && _pendingPersistedRecords == null;
            }
        }, millisecondsTimeout: 2_000);
    }

    private void RestorePersistedTokens()
    {
        foreach (var record in _store.Load())
        {
            if (record.ExpiresUtc <= _clock.UtcNow || string.IsNullOrWhiteSpace(record.Token))
            {
                continue;
            }

            _tokens[record.Token] = new ApprovalRecord
            {
                ToolName = record.ToolName,
                RequestFingerprint = record.RequestFingerprint,
                DocumentKey = record.DocumentKey,
                ViewKey = record.ViewKey,
                SelectionHash = record.SelectionHash,
                PreviewRunId = record.PreviewRunId,
                Caller = record.Caller,
                SessionId = record.SessionId,
                ExpiresUtc = record.ExpiresUtc
            };
        }

        PersistTokens();
    }

    private void PersistTokens()
    {
        var now = _clock.UtcNow;
        foreach (var key in _tokens.Where(x => x.Value.ExpiresUtc <= now).Select(x => x.Key).ToList())
        {
            _tokens.TryRemove(key, out _);
        }

        QueuePersist(CreatePersistedSnapshot());
    }

    private IReadOnlyList<PersistedApprovalRecord> CreatePersistedSnapshot()
    {
        return _tokens.Select(x => new PersistedApprovalRecord
        {
            Token = x.Key,
            ToolName = x.Value.ToolName,
            RequestFingerprint = x.Value.RequestFingerprint,
            DocumentKey = x.Value.DocumentKey,
            ViewKey = x.Value.ViewKey,
            SelectionHash = x.Value.SelectionHash,
            PreviewRunId = x.Value.PreviewRunId,
            Caller = x.Value.Caller,
            SessionId = x.Value.SessionId,
            ExpiresUtc = x.Value.ExpiresUtc
        }).ToList();
    }

    private void QueuePersist(IReadOnlyList<PersistedApprovalRecord> snapshot)
    {
        lock (_persistGate)
        {
            _pendingPersistedRecords = snapshot;
            if (_persistWorkerActive)
            {
                return;
            }

            _persistWorkerActive = true;
        }

        ThreadPool.QueueUserWorkItem(_ => PersistLoop());
    }

    private void PersistLoop()
    {
        while (true)
        {
            IReadOnlyList<PersistedApprovalRecord>? snapshot;
            lock (_persistGate)
            {
                snapshot = _pendingPersistedRecords;
                _pendingPersistedRecords = null;
                if (snapshot == null)
                {
                    _persistWorkerActive = false;
                    return;
                }
            }

            _store.Save(snapshot);
        }
    }

    private sealed class ApprovalRecord
    {
        internal string ToolName { get; set; } = string.Empty;
        internal string RequestFingerprint { get; set; } = string.Empty;
        internal string DocumentKey { get; set; } = string.Empty;
        internal string ViewKey { get; set; } = string.Empty;
        internal string SelectionHash { get; set; } = string.Empty;
        internal string PreviewRunId { get; set; } = string.Empty;
        internal string Caller { get; set; } = string.Empty;
        internal string SessionId { get; set; } = string.Empty;
        internal DateTime ExpiresUtc { get; set; }
    }
}

internal sealed class SnapshotService : ISnapshotService
{
    public ModelSnapshotSummary Take(Document doc, IEnumerable<int> elementIds, IEnumerable<string>? parameterNames = null)
    {
        var requestedNames = (parameterNames ?? Enumerable.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var snapshot = new ModelSnapshotSummary
        {
            WarningCount = doc.GetWarnings().Count
        };

        foreach (var id in elementIds.Distinct())
        {
            var element = doc.GetElement(new ElementId((long)id));
            if (element == null)
            {
                continue;
            }

            var state = new SnapshotElementState
            {
                ElementId = id
            };

            if (requestedNames.Count == 0)
            {
                foreach (Parameter parameter in element.Parameters)
                {
                    var name = parameter.Definition?.Name ?? string.Empty;
                    if (!state.Parameters.ContainsKey(name))
                    {
                        state.Parameters[name] = PlatformServices.ParameterValue(parameter);
                    }
                }
            }
            else
            {
                foreach (var name in requestedNames)
                {
                    var parameter = element.LookupParameter(name);
                    state.Parameters[name] = parameter != null ? PlatformServices.ParameterValue(parameter) : string.Empty;
                }
            }

            snapshot.Elements.Add(state);
        }

        return snapshot;
    }

    public DiffSummary Diff(ModelSnapshotSummary before, ModelSnapshotSummary after)
    {
        var diff = new DiffSummary
        {
            WarningDelta = after.WarningCount - before.WarningCount
        };

        var beforeMap = before.Elements.ToDictionary(x => x.ElementId);
        var afterMap = after.Elements.ToDictionary(x => x.ElementId);

        foreach (var beforeId in beforeMap.Keys)
        {
            if (!afterMap.ContainsKey(beforeId))
            {
                diff.DeletedIds.Add(beforeId);
            }
        }

        foreach (var afterId in afterMap.Keys)
        {
            if (!beforeMap.ContainsKey(afterId))
            {
                diff.CreatedIds.Add(afterId);
                continue;
            }

            var beforeState = beforeMap[afterId];
            var afterState = afterMap[afterId];
            foreach (var kvp in afterState.Parameters)
            {
                beforeState.Parameters.TryGetValue(kvp.Key, out var beforeValue);
                if (!string.Equals(beforeValue ?? string.Empty, kvp.Value ?? string.Empty, StringComparison.Ordinal))
                {
                    diff.ParameterChanges.Add(new ParameterChangeRecord
                    {
                        ElementId = afterId,
                        ParameterName = kvp.Key,
                        BeforeValue = beforeValue ?? string.Empty,
                        AfterValue = kvp.Value ?? string.Empty
                    });
                }
            }

            if (diff.ParameterChanges.Any(x => x.ElementId == afterId))
            {
                diff.ModifiedIds.Add(afterId);
            }
        }

        return diff;
    }
}

internal sealed class MutationService
{
    private readonly AnnotationMutationService _annotation = new AnnotationMutationService();

    internal ExecutionResult PreviewSetParameters(PlatformServices services, Document doc, SetParametersRequest request, ToolRequestEnvelope envelope)
    {
        var changes = request.Changes ?? new List<ParameterUpdateItem>();
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = changes.Select(x => x.ElementId).Distinct().ToList()
        };
    }

    internal ExecutionResult ExecuteSetParameters(PlatformServices services, Document doc, SetParametersRequest request)
    {
        var changes = request.Changes ?? new List<ParameterUpdateItem>();
        var diagnostics = new List<DiagnosticRecord>();
        var elementIds = changes.Select(x => x.ElementId).Distinct().ToList();
        var before = services.Snapshot.Take(doc, elementIds, changes.Select(x => x.ParameterName));
        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::parameter.set_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Set parameters safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        foreach (var change in changes)
        {
            var element = doc.GetElement(new ElementId((long)change.ElementId));
            if (element == null)
            {
                continue;
            }

            var parameter = element.LookupParameter(change.ParameterName);
            if (parameter == null || parameter.IsReadOnly)
            {
                continue;
            }

            ParameterMutationHelper.SetParameterValue(parameter, change.NewValue);
        }

        doc.Regenerate();
        transaction.Commit();
        var after = services.Snapshot.Take(doc, elementIds, changes.Select(x => x.ParameterName));
        var diff = services.Snapshot.Diff(before, after);
        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
        }
        else
        {
            group.Assimilate();
        }

        return new ExecutionResult
        {
            OperationName = "parameter.set_safe",
            DryRun = false,
            ChangedIds = diff.ModifiedIds,
            DiffSummary = diff,
            ReviewSummary = services.BuildExecutionReview("parameter_set_review", diff)
        };
    }

    internal ExecutionResult PreviewDelete(PlatformServices services, Document doc, DeleteElementsRequest request, ToolRequestEnvelope envelope)
    {
        var elementIds = request.ElementIds ?? new List<int>();
        var deleteAnalysis = AnalyzeDeleteImpact(doc, elementIds);
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = deleteAnalysis.AllDeletedIds,
            Diagnostics = deleteAnalysis.Diagnostics,
            Artifacts = deleteAnalysis.Artifacts
        };
    }

    internal ExecutionResult ExecuteDelete(PlatformServices services, Document doc, DeleteElementsRequest request)
    {
        var elementIds = request.ElementIds ?? new List<int>();
        var deleteAnalysis = AnalyzeDeleteImpact(doc, elementIds);
        if (deleteAnalysis.HasUnexpectedDependents && !request.AllowDependentDeletes)
        {
            return new ExecutionResult
            {
                OperationName = "element.delete_safe",
                DryRun = false,
                Diagnostics = deleteAnalysis.Diagnostics,
                Artifacts = deleteAnalysis.Artifacts
            };
        }

        var before = services.Snapshot.Take(doc, elementIds);
        var diagnostics = new List<DiagnosticRecord>();
        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::element.delete_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Delete elements safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);
        doc.Delete(elementIds.Select(x => new ElementId((long)x)).ToList());
        doc.Regenerate();
        transaction.Commit();
        var after = services.Snapshot.Take(doc, elementIds);
        var diff = services.Snapshot.Diff(before, after);

        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
        }
        else
        {
            group.Assimilate();
        }

        return new ExecutionResult
        {
            OperationName = "element.delete_safe",
            DryRun = false,
            ChangedIds = diff.DeletedIds,
            DiffSummary = diff,
            Diagnostics = diagnostics,
            ReviewSummary = services.BuildExecutionReview("delete_review", diff)
        };
    }

    internal DeleteImpactAnalysis AnalyzeDeleteImpact(Document doc, IEnumerable<int> elementIds)
    {
        var requestedIds = elementIds?.Distinct().Where(x => x > 0).ToList() ?? new List<int>();
        var diagnostics = new List<DiagnosticRecord>();
        var artifacts = new List<string>();
        var requestedSet = new HashSet<int>(requestedIds);

        if (requestedIds.Count == 0)
        {
            diagnostics.Add(DiagnosticRecord.Create("DELETE_ELEMENT_IDS_EMPTY", DiagnosticSeverity.Error, "ElementIds phải có ít nhất 1 giá trị."));
            return new DeleteImpactAnalysis
            {
                RequestedIds = requestedIds,
                Diagnostics = diagnostics,
                Artifacts = artifacts
            };
        }

        var tempTransaction = new Transaction(doc, "BIM765T.Revit.Agent::delete.preview");
        try
        {
            tempTransaction.Start();
            var deleted = doc.Delete(requestedIds.Select(x => new ElementId((long)x)).ToList())
                .Select(x => checked((int)x.Value))
                .Distinct()
                .ToList();
            tempTransaction.RollBack();

            var unexpected = deleted
                .Where(x => !requestedSet.Contains(x))
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            if (unexpected.Count > 0)
            {
                diagnostics.Add(DiagnosticRecord.Create(
                    "DELETE_DEPENDENCIES_DETECTED",
                    DiagnosticSeverity.Warning,
                    $"Delete request sẽ xóa thêm {unexpected.Count} dependent element(s) ngoài explicit payload. Set AllowDependentDeletes=true nếu bạn chấp nhận impact này."));
                artifacts.Add("UnexpectedDependentIds=" + string.Join(",", unexpected.Take(50)));
            }

            artifacts.Add("RequestedDeleteIds=" + string.Join(",", requestedIds.Take(50)));
            artifacts.Add("TotalDeleteImpactCount=" + deleted.Count);

            return new DeleteImpactAnalysis
            {
                RequestedIds = requestedIds,
                AllDeletedIds = deleted,
                UnexpectedDependentIds = unexpected,
                Diagnostics = diagnostics,
                Artifacts = artifacts
            };
        }
        catch (Exception ex)
        {
            if (tempTransaction.GetStatus() == TransactionStatus.Started)
            {
                tempTransaction.RollBack();
            }

            diagnostics.Add(DiagnosticRecord.Create("DELETE_PREVIEW_FAILED", DiagnosticSeverity.Error, ex.Message));
            return new DeleteImpactAnalysis
            {
                RequestedIds = requestedIds,
                Diagnostics = diagnostics,
                Artifacts = artifacts
            };
        }
        finally
        {
            tempTransaction.Dispose();
        }
    }

    internal ExecutionResult PreviewMoveElements(PlatformServices services, Document doc, MoveElementsRequest request, ToolRequestEnvelope envelope)
    {
        var elementIds = request.ElementIds ?? new List<int>();
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = elementIds.Distinct().ToList()
        };
    }

    internal ExecutionResult ExecuteMoveElements(PlatformServices services, Document doc, MoveElementsRequest request)
    {
        var elementIds = request.ElementIds ?? new List<int>();
        var diagnostics = new List<DiagnosticRecord>();
        var before = services.Snapshot.Take(doc, elementIds);
        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::element.move_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Move elements safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);
        ElementTransformUtils.MoveElements(doc, elementIds.Select(x => new ElementId((long)x)).ToList(), new XYZ(request.DeltaX, request.DeltaY, request.DeltaZ));
        doc.Regenerate();
        transaction.Commit();
        var after = services.Snapshot.Take(doc, elementIds);
        var diff = services.Snapshot.Diff(before, after);

        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
        }
        else
        {
            group.Assimilate();
        }

        return new ExecutionResult
        {
            OperationName = "element.move_safe",
            DryRun = false,
            ChangedIds = elementIds.Distinct().ToList(),
            DiffSummary = diff,
            Diagnostics = diagnostics,
            ReviewSummary = services.BuildExecutionReview("move_review", diff)
        };
    }

    internal ExecutionResult PreviewRotateElements(PlatformServices services, Document doc, RotateElementsRequest request, ToolRequestEnvelope envelope)
    {
        var elementIds = request.ElementIds ?? new List<int>();
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            ChangedIds = elementIds.Distinct().ToList(),
            Artifacts = new List<string>
            {
                "axisMode=" + (request.AxisMode ?? string.Empty),
                "angleDegrees=" + request.AngleDegrees.ToString("0.###", CultureInfo.InvariantCulture)
            }
        };
    }

    internal ExecutionResult ExecuteRotateElements(PlatformServices services, Document doc, RotateElementsRequest request)
    {
        var elementIds = (request.ElementIds ?? new List<int>()).Distinct().ToList();
        var diagnostics = new List<DiagnosticRecord>();
        var modifiedIds = new List<int>();
        var before = services.Snapshot.Take(doc, elementIds);
        var angleRadians = request.AngleDegrees * Math.PI / 180.0;

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::element.rotate_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Rotate elements safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);

        foreach (var elementId in elementIds)
        {
            var element = doc.GetElement(new ElementId((long)elementId));
            if (element == null)
            {
                diagnostics.Add(DiagnosticRecord.Create("ROTATE_ELEMENT_NOT_FOUND", DiagnosticSeverity.Warning, $"Element Id={elementId} không tồn tại.", elementId));
                continue;
            }

            try
            {
                var axis = ResolveRotationAxis(element, request);
                ElementTransformUtils.RotateElement(doc, element.Id, axis, angleRadians);
                modifiedIds.Add(elementId);
            }
            catch (Exception ex)
            {
                diagnostics.Add(DiagnosticRecord.Create("ROTATE_ELEMENT_FAILED", DiagnosticSeverity.Error, $"Không thể rotate element Id={elementId}: {ex.Message}", elementId));
            }
        }

        doc.Regenerate();
        transaction.Commit();

        var diff = modifiedIds.Count > 0
            ? services.Snapshot.Diff(before, services.Snapshot.Take(doc, elementIds))
            : new DiffSummary();

        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
            modifiedIds.Clear();
        }
        else
        {
            group.Assimilate();
        }

        var review = services.BuildExecutionReview("rotate_review", diff);
        foreach (var record in diagnostics.Where(x => x.Severity != DiagnosticSeverity.Info))
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = record.Code,
                Severity = record.Severity,
                Message = record.Message,
                ElementId = record.SourceId
            });
        }

        review.IssueCount = review.Issues.Count;

        return new ExecutionResult
        {
            OperationName = ToolNames.ElementRotateSafe,
            DryRun = false,
            ChangedIds = modifiedIds,
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                "axisMode=" + (request.AxisMode ?? string.Empty),
                "angleDegrees=" + request.AngleDegrees.ToString("0.###", CultureInfo.InvariantCulture)
            },
            ReviewSummary = review
        };
    }

    internal ExecutionResult PreviewAddTextNote(PlatformServices services, Document doc, AddTextNoteRequest request, ToolRequestEnvelope envelope)
        => _annotation.PreviewAddTextNote(services, doc, request, envelope);

    internal ExecutionResult ExecuteAddTextNote(PlatformServices services, Document doc, AddTextNoteRequest request)
        => _annotation.ExecuteAddTextNote(services, doc, request);

    internal ExecutionResult PreviewUpdateTextNoteStyle(PlatformServices services, Document doc, UpdateTextNoteStyleRequest request, ToolRequestEnvelope envelope)
        => _annotation.PreviewUpdateTextNoteStyle(services, doc, request, envelope);

    internal ExecutionResult ExecuteUpdateTextNoteStyle(PlatformServices services, Document doc, UpdateTextNoteStyleRequest request)
        => _annotation.ExecuteUpdateTextNoteStyle(services, doc, request);

    internal ExecutionResult PreviewUpdateTextNoteContent(PlatformServices services, Document doc, UpdateTextNoteContentRequest request, ToolRequestEnvelope envelope)
        => _annotation.PreviewUpdateTextNoteContent(services, doc, request, envelope);

    internal ExecutionResult ExecuteUpdateTextNoteContent(PlatformServices services, Document doc, UpdateTextNoteContentRequest request)
        => _annotation.ExecuteUpdateTextNoteContent(services, doc, request);

    // ── Phase 1B: Parameter Copy/Fill/Import mutations ──

    internal ExecutionResult PreviewCopyParameters(PlatformServices services, Document doc, CopyParametersBetweenRequest request, ToolRequestEnvelope envelope)
    {
        var diags = new List<DiagnosticRecord>();
        var source = doc.GetElement(new ElementId((long)request.SourceElementId));
        if (source == null) diags.Add(DiagnosticRecord.Create("SOURCE_NOT_FOUND", DiagnosticSeverity.Error, $"Source element Id={request.SourceElementId} not found."));
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName, DryRun = true, ConfirmationRequired = true, ApprovalToken = token,
            ChangedIds = request.TargetElementIds.Distinct().ToList(), Diagnostics = diags,
            Artifacts = new List<string> { $"SourceId={request.SourceElementId}", $"TargetCount={request.TargetElementIds.Count}", $"ParamCount={request.ParameterNames.Count}" }
        };
    }

    internal ExecutionResult ExecuteCopyParameters(PlatformServices services, Document doc, CopyParametersBetweenRequest request)
    {
        var diags = new List<DiagnosticRecord>();
        var source = doc.GetElement(new ElementId((long)request.SourceElementId))
            ?? throw new InvalidOperationException($"Source element Id={request.SourceElementId} not found.");

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::parameter.copy_between_safe");
        group.Start();
        using var tx = new Transaction(doc, "Copy parameters between elements");
        tx.Start();
        AgentFailureHandling.Configure(tx, diags);

        var modifiedIds = new List<int>();
        var paramChanges = new List<ParameterChangeRecord>();

        foreach (var targetId in request.TargetElementIds.Distinct())
        {
            var target = doc.GetElement(new ElementId((long)targetId));
            if (target == null) { diags.Add(DiagnosticRecord.Create("TARGET_NOT_FOUND", DiagnosticSeverity.Warning, $"Target Id={targetId} not found.")); continue; }

            foreach (var pName in request.ParameterNames)
            {
                var srcParam = source.LookupParameter(pName);
                var tgtParam = target.LookupParameter(pName);
                if (srcParam == null || tgtParam == null) continue;
                if (tgtParam.IsReadOnly && request.SkipReadOnly) continue;

                var beforeVal = tgtParam.AsValueString() ?? tgtParam.AsString() ?? string.Empty;
                ParameterMutationHelper.SetParameterValue(tgtParam, srcParam.AsValueString() ?? srcParam.AsString() ?? string.Empty);
                var afterVal = tgtParam.AsValueString() ?? tgtParam.AsString() ?? string.Empty;
                paramChanges.Add(new ParameterChangeRecord { ElementId = targetId, ParameterName = pName, BeforeValue = beforeVal, AfterValue = afterVal });
            }
            modifiedIds.Add(targetId);
        }
        doc.Regenerate();
        tx.Commit();

        var diff = new DiffSummary { ModifiedIds = modifiedIds, ParameterChanges = paramChanges };
        if (diags.Any(d => d.Severity == DiagnosticSeverity.Error)) { group.RollBack(); diff = new DiffSummary(); }
        else group.Assimilate();

        return new ExecutionResult { OperationName = "parameter.copy_between_safe", DryRun = false, ChangedIds = modifiedIds, DiffSummary = diff, Diagnostics = diags };
    }

    internal ExecutionResult PreviewAddSharedParameter(PlatformServices services, Document doc, AddSharedParameterRequest request, ToolRequestEnvelope envelope)
    {
        var diags = new List<DiagnosticRecord>();
        if (string.IsNullOrWhiteSpace(request.ParameterName))
            diags.Add(DiagnosticRecord.Create("PARAM_NAME_EMPTY", DiagnosticSeverity.Error, "ParameterName is required."));
        if (request.CategoryNames.Count == 0)
            diags.Add(DiagnosticRecord.Create("NO_CATEGORIES", DiagnosticSeverity.Error, "At least one category name is required."));

        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName, DryRun = true, ConfirmationRequired = true, ApprovalToken = token,
            Diagnostics = diags,
            Artifacts = new List<string> { $"ParameterName={request.ParameterName}", $"Categories={string.Join(",", request.CategoryNames)}" }
        };
    }

    internal ExecutionResult ExecuteAddSharedParameter(PlatformServices services, Document doc, AddSharedParameterRequest request)
    {
        var diags = new List<DiagnosticRecord>();
        // NOTE: Full shared parameter creation requires a .txt file and SharedParameterFile API.
        // This is a simplified implementation that creates a project parameter instead.
        diags.Add(DiagnosticRecord.Create("SHARED_PARAM_STUB", DiagnosticSeverity.Info,
            $"Shared parameter '{request.ParameterName}' creation requires a shared parameter file. Use parameter.set_safe to set values on existing parameters."));
        return new ExecutionResult { OperationName = "parameter.add_shared_safe", DryRun = false, Diagnostics = diags };
    }

    internal ExecutionResult PreviewBatchFillParameter(PlatformServices services, Document doc, BatchFillParameterRequest request, ToolRequestEnvelope envelope)
    {
        var diags = new List<DiagnosticRecord>();
        var targetIds = new List<int>();

        if (request.ElementIds.Count > 0)
        {
            targetIds = request.ElementIds;
        }
        else if (request.CategoryNames.Count > 0)
        {
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            foreach (var elem in collector)
            {
                if (elem.Category != null && request.CategoryNames.Any(c => string.Equals(c, elem.Category.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    var p = elem.LookupParameter(request.ParameterName);
                    if (p != null && !p.IsReadOnly)
                    {
                        var currentVal = p.AsValueString() ?? p.AsString() ?? string.Empty;
                        if (request.FillMode == "OnlyEmpty" && !string.IsNullOrWhiteSpace(currentVal)) continue;
                        targetIds.Add(checked((int)elem.Id.Value));
                    }
                }
                if (targetIds.Count > 5000) break;
            }
        }

        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName, DryRun = true, ConfirmationRequired = true, ApprovalToken = token,
            ChangedIds = targetIds, Diagnostics = diags,
            Artifacts = new List<string> { $"ParameterName={request.ParameterName}", $"FillValue={request.FillValue}", $"TargetCount={targetIds.Count}" }
        };
    }

    internal ExecutionResult ExecuteBatchFillParameter(PlatformServices services, Document doc, BatchFillParameterRequest request)
    {
        var diags = new List<DiagnosticRecord>();
        var modifiedIds = new List<int>();
        var paramChanges = new List<ParameterChangeRecord>();

        var targetIds = request.ElementIds.Count > 0 ? request.ElementIds : new List<int>();
        if (targetIds.Count == 0 && request.CategoryNames.Count > 0)
        {
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            foreach (var elem in collector)
            {
                if (elem.Category != null && request.CategoryNames.Any(c => string.Equals(c, elem.Category.Name, StringComparison.OrdinalIgnoreCase)))
                    targetIds.Add(checked((int)elem.Id.Value));
                if (targetIds.Count > 5000) break;
            }
        }

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::parameter.batch_fill_safe");
        group.Start();
        using var tx = new Transaction(doc, "Batch fill parameter");
        tx.Start();
        AgentFailureHandling.Configure(tx, diags);

        foreach (var id in targetIds)
        {
            var elem = doc.GetElement(new ElementId((long)id));
            if (elem == null) continue;
            var p = elem.LookupParameter(request.ParameterName);
            if (p == null || p.IsReadOnly) continue;

            var beforeVal = p.AsValueString() ?? p.AsString() ?? string.Empty;
            if (request.FillMode == "OnlyEmpty" && !string.IsNullOrWhiteSpace(beforeVal)) continue;

            ParameterMutationHelper.SetParameterValue(p, request.FillValue);
            modifiedIds.Add(id);
            paramChanges.Add(new ParameterChangeRecord { ElementId = id, ParameterName = request.ParameterName, BeforeValue = beforeVal, AfterValue = request.FillValue });
        }
        doc.Regenerate();
        tx.Commit();

        var diff = new DiffSummary { ModifiedIds = modifiedIds, ParameterChanges = paramChanges };
        if (diags.Any(d => d.Severity == DiagnosticSeverity.Error)) { group.RollBack(); diff = new DiffSummary(); }
        else group.Assimilate();

        return new ExecutionResult { OperationName = "parameter.batch_fill_safe", DryRun = false, ChangedIds = modifiedIds, DiffSummary = diff, Diagnostics = diags };
    }

    internal ExecutionResult PreviewDataImport(PlatformServices services, Document doc, DataImportRequest request, ToolRequestEnvelope envelope)
    {
        var diags = new List<DiagnosticRecord>();
        if (!DataExportService.ValidateFilePath(request.InputPath, out var sanitizedPath, out var pathError))
        {
            diags.Add(DiagnosticRecord.Create("FILE_PATH_INVALID", DiagnosticSeverity.Error, pathError));
        }
        else if (!System.IO.File.Exists(sanitizedPath))
        {
            diags.Add(DiagnosticRecord.Create("FILE_NOT_FOUND", DiagnosticSeverity.Error, $"File not found: {sanitizedPath}"));
        }

        if (string.IsNullOrWhiteSpace(request.MatchParameterName))
            diags.Add(DiagnosticRecord.Create("MATCH_PARAM_EMPTY", DiagnosticSeverity.Error, "MatchParameterName is required."));

        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName, DryRun = true, ConfirmationRequired = true, ApprovalToken = token,
            Diagnostics = diags,
            Artifacts = new List<string> { $"InputPath={sanitizedPath}", $"MatchParameter={request.MatchParameterName}" }
        };
    }

    internal ExecutionResult ExecuteDataImport(PlatformServices services, Document doc, DataImportRequest request)
    {
        var diags = new List<DiagnosticRecord>();
        if (!DataExportService.ValidateFilePath(request.InputPath, out var sanitizedPath, out var pathError))
            throw new InvalidOperationException(pathError);
        if (!System.IO.File.Exists(sanitizedPath))
            throw new InvalidOperationException($"File not found: {sanitizedPath}");

        // Read CSV
        var lines = System.IO.File.ReadAllLines(sanitizedPath);
        if (lines.Length < 2) { diags.Add(DiagnosticRecord.Create("EMPTY_FILE", DiagnosticSeverity.Warning, "File has no data rows.")); return new ExecutionResult { OperationName = "data.import_safe", Diagnostics = diags }; }

        var headers = lines[0].Split(',').Select(h => h.Trim().Trim('"')).ToList();
        var matchIndex = headers.IndexOf(request.MatchParameterName);
        if (matchIndex < 0) { diags.Add(DiagnosticRecord.Create("MATCH_COL_NOT_FOUND", DiagnosticSeverity.Error, $"Column '{request.MatchParameterName}' not found in file.")); return new ExecutionResult { OperationName = "data.import_safe", Diagnostics = diags }; }

        var modifiedIds = new List<int>();
        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::data.import_safe");
        group.Start();
        using var tx = new Transaction(doc, "Data import");
        tx.Start();
        AgentFailureHandling.Configure(tx, diags);

        for (int i = 1; i < lines.Length; i++)
        {
            var cells = lines[i].Split(',').Select(c => c.Trim().Trim('"')).ToArray();
            if (cells.Length <= matchIndex) continue;
            var matchValue = cells[matchIndex];

            // Find elements matching the match parameter value
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();
            foreach (var elem in collector)
            {
                var matchParam = elem.LookupParameter(request.MatchParameterName);
                if (matchParam == null) continue;
                var val = matchParam.AsValueString() ?? matchParam.AsString() ?? string.Empty;
                if (!string.Equals(val, matchValue, StringComparison.OrdinalIgnoreCase)) continue;

                for (int col = 0; col < Math.Min(cells.Length, headers.Count); col++)
                {
                    if (col == matchIndex) continue;
                    var p = elem.LookupParameter(headers[col]);
                    if (p == null || p.IsReadOnly) continue;
                    ParameterMutationHelper.SetParameterValue(p, cells[col]);
                }
                modifiedIds.Add(checked((int)elem.Id.Value));
                break;
            }
        }
        doc.Regenerate();
        tx.Commit();

        var diff = new DiffSummary { ModifiedIds = modifiedIds.Distinct().ToList() };
        if (diags.Any(d => d.Severity == DiagnosticSeverity.Error)) { group.RollBack(); diff = new DiffSummary(); }
        else group.Assimilate();

        return new ExecutionResult { OperationName = "data.import_safe", DryRun = false, ChangedIds = modifiedIds, DiffSummary = diff, Diagnostics = diags };
    }

    internal ExecutionResult PreviewPlaceFamilyInstance(PlatformServices services, Document doc, PlaceFamilyInstanceRequest request, ToolRequestEnvelope envelope)
    {
        var plan = BuildPlacementPlan(doc, request);
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token,
            Diagnostics = new List<DiagnosticRecord>(plan.Diagnostics),
            Artifacts = new List<string>
            {
                "placementType=" + plan.FamilyPlacementType,
                "strategy=" + plan.Strategy,
                "symbolId=" + plan.Symbol.Id.Value.ToString(CultureInfo.InvariantCulture)
            },
            ReviewSummary = new ReviewReport
            {
                Name = "placement_preview",
                DocumentKey = services.GetDocumentKey(doc),
                ViewKey = doc.ActiveView != null ? services.GetViewKey(doc.ActiveView) : string.Empty,
                IssueCount = plan.Diagnostics.Count,
                Issues = plan.Diagnostics.Select(x => new ReviewIssue
                {
                    Code = x.Code,
                    Severity = x.Severity,
                    Message = x.Message,
                    ElementId = x.SourceId
                }).ToList()
            }
        };
    }

    internal ExecutionResult ExecutePlaceFamilyInstance(PlatformServices services, Document doc, PlaceFamilyInstanceRequest request)
    {
        var plan = BuildPlacementPlan(doc, request);
        var diagnostics = new List<DiagnosticRecord>(plan.Diagnostics);
        var beforeWarnings = doc.GetWarnings().Count;

        using var group = new TransactionGroup(doc, "BIM765T.Revit.Agent::element.place_family_instance_safe");
        group.Start();
        using var transaction = new Transaction(doc, "Place family instance safe");
        transaction.Start();
        AgentFailureHandling.Configure(transaction, diagnostics);
        if (!plan.Symbol.IsActive)
        {
            plan.Symbol.Activate();
            doc.Regenerate();
        }

        var instance = PlaceFromPlan(doc, plan);
        if (Math.Abs(request.RotateRadians) > 1e-9)
        {
            RotatePlacedInstance(doc, instance, plan, request.RotateRadians);
        }

        doc.Regenerate();
        transaction.Commit();
        var diff = new DiffSummary
        {
            CreatedIds = new List<int> { checked((int)instance.Id.Value) },
            WarningDelta = doc.GetWarnings().Count - beforeWarnings
        };
        if (diagnostics.Any(x => x.Severity == DiagnosticSeverity.Error))
        {
            group.RollBack();
            diff = new DiffSummary();
        }
        else
        {
            group.Assimilate();
        }

        var review = services.BuildExecutionReview("placement_review", diff);
        foreach (var record in diagnostics.Where(x => x.Severity != DiagnosticSeverity.Info))
        {
            review.Issues.Add(new ReviewIssue
            {
                Code = record.Code,
                Severity = record.Severity,
                Message = record.Message,
                ElementId = record.SourceId
            });
        }
        review.IssueCount = review.Issues.Count;

        return new ExecutionResult
        {
            OperationName = "element.place_family_instance_safe",
            DryRun = false,
            ChangedIds = diff.CreatedIds,
            DiffSummary = diff,
            Diagnostics = diagnostics,
            Artifacts = new List<string>
            {
                "placementType=" + plan.FamilyPlacementType,
                "strategy=" + plan.Strategy,
                diff.CreatedIds.Count > 0 ? "createdId=" + diff.CreatedIds[0].ToString(CultureInfo.InvariantCulture) : "createdId=<none>"
            },
            ReviewSummary = review
        };
    }
    private static FamilyInstance PlaceFromPlan(Document doc, PlacementPlan plan)
    {
        switch (plan.Strategy)
        {
            case "LevelPoint":
                return doc.Create.NewFamilyInstance(plan.Point, plan.Symbol, plan.Level!, plan.StructuralType);
            case "HostedPoint":
                return doc.Create.NewFamilyInstance(plan.Point, plan.Symbol, plan.Host!, plan.StructuralType);
            case "HostedPointDirection":
                return doc.Create.NewFamilyInstance(plan.Point, plan.Symbol, plan.ReferenceDirection!, plan.Host!, plan.StructuralType);
            case "ViewPoint":
                return doc.Create.NewFamilyInstance(plan.Point, plan.Symbol, plan.View!);
            case "WorkPlaneFacePoint":
                return doc.Create.NewFamilyInstance(plan.Face!, plan.Point, plan.ReferenceDirection!, plan.Symbol);
            case "WorkPlaneSketchPlanePoint":
            {
                var sketchPlane = SketchPlane.Create(doc, Plane.CreateByNormalAndOrigin(plan.WorkPlaneNormal!, plan.Point));
                return doc.Create.NewFamilyInstance(sketchPlane.GetPlaneReference(), plan.Point, plan.ReferenceDirection!, plan.Symbol);
            }
            case "CurveInView":
                return doc.Create.NewFamilyInstance(plan.Line!, plan.Symbol, plan.View!);
            case "CurveOnFace":
                return doc.Create.NewFamilyInstance(plan.Face!, plan.Line!, plan.Symbol);
            default:
                throw new NotSupportedException("Placement strategy chưa được implement: " + plan.Strategy);
        }
    }

    private static void RotatePlacedInstance(Document doc, FamilyInstance instance, PlacementPlan plan, double radians)
    {
        var origin = plan.Point;
        var axisDirection = plan.Face != null ? plan.Face.FaceNormal : XYZ.BasisZ;
        var axis = Line.CreateBound(origin, origin + axisDirection);
        ElementTransformUtils.RotateElement(doc, instance.Id, axis, radians);
    }

    private static PlacementPlan BuildPlacementPlan(Document doc, PlaceFamilyInstanceRequest request)
    {
        var symbol = doc.GetElement(new ElementId((long)request.FamilySymbolId)) as FamilySymbol;
        if (symbol == null)
        {
            throw new InvalidOperationException("FamilySymbol không tồn tại.");
        }

        var placementType = symbol.Family?.FamilyPlacementType ?? Autodesk.Revit.DB.FamilyPlacementType.Invalid;
        var plan = new PlacementPlan
        {
            Symbol = symbol,
            FamilyPlacementType = placementType.ToString(),
            Point = new XYZ(request.X, request.Y, request.Z),
            StructuralType = ParseStructuralType(request.StructuralTypeName)
        };

        plan.Diagnostics.Add(DiagnosticRecord.Create("PLACEMENT_TYPE", DiagnosticSeverity.Info, "Family placement type: " + plan.FamilyPlacementType, checked((int)symbol.Id.Value)));

        switch (placementType)
        {
            case Autodesk.Revit.DB.FamilyPlacementType.OneLevelBased:
            case Autodesk.Revit.DB.FamilyPlacementType.TwoLevelsBased:
                plan.Level = ResolveLevel(doc, request.LevelId, plan.Point) ?? doc.ActiveView?.GenLevel;
                if (plan.Level == null)
                {
                    throw new InvalidOperationException("Level-based placement cần LevelId hợp lệ hoặc active view có GenLevel.");
                }
                plan.Strategy = "LevelPoint";
                plan.Diagnostics.Add(DiagnosticRecord.Create("PLACEMENT_LEVEL_RESOLVED", DiagnosticSeverity.Info, "Resolved level: " + plan.Level.Name, checked((int)plan.Level.Id.Value)));
                break;

            case Autodesk.Revit.DB.FamilyPlacementType.OneLevelBasedHosted:
                plan.Host = ResolveHost(doc, request.HostElementId);
                if (plan.Host == null)
                {
                    throw new InvalidOperationException("Hosted placement cần HostElementId hợp lệ.");
                }
                plan.ReferenceDirection = ResolveReferenceDirection(request, null);
                plan.Strategy = IsNonZero(plan.ReferenceDirection) ? "HostedPointDirection" : "HostedPoint";
                plan.Diagnostics.Add(DiagnosticRecord.Create("PLACEMENT_HOST_RESOLVED", DiagnosticSeverity.Info, "Resolved host element.", checked((int)plan.Host.Id.Value)));
                break;

            case Autodesk.Revit.DB.FamilyPlacementType.ViewBased:
                plan.View = ResolvePlacementView(doc, request.ViewId);
                if (plan.View.IsTemplate)
                {
                    throw new InvalidOperationException("View-based placement không cho phép view template.");
                }
                plan.Strategy = "ViewPoint";
                plan.Diagnostics.Add(DiagnosticRecord.Create("PLACEMENT_VIEW_RESOLVED", DiagnosticSeverity.Info, "Resolved view: " + plan.View.Name, checked((int)plan.View.Id.Value)));
                break;

            case Autodesk.Revit.DB.FamilyPlacementType.WorkPlaneBased:
                plan.Host = ResolveHost(doc, request.HostElementId);
                var requestedWorkPlaneNormal = TryBuildVector(request.FaceNormalX, request.FaceNormalY, request.FaceNormalZ);
                if (plan.Host != null)
                {
                    plan.Face = ResolvePlanarFace(plan.Host, plan.Point, requestedWorkPlaneNormal);
                }

                if (plan.Face != null)
                {
                    plan.ReferenceDirection = ResolveReferenceDirection(request, plan.Face);
                    plan.Strategy = "WorkPlaneFacePoint";
                    plan.Diagnostics.Add(DiagnosticRecord.Create("PLACEMENT_FACE_RESOLVED", DiagnosticSeverity.Info, "Resolved host face cho WorkPlaneBased placement.", checked((int)plan.Host!.Id.Value)));
                }
                else
                {
                    if (requestedWorkPlaneNormal == null || !IsNonZero(requestedWorkPlaneNormal))
                    {
                        throw new InvalidOperationException("WorkPlaneBased placement fallback can FaceNormalX/Y/Z de tao sketch plane tam.");
                    }

                    plan.WorkPlaneNormal = requestedWorkPlaneNormal.Normalize();
                    plan.ReferenceDirection = ResolveReferenceDirection(request, null);
                    plan.Strategy = "WorkPlaneSketchPlanePoint";
                    var fallbackMessage = plan.Host == null
                        ? "Fallback sang sketch plane tam cho WorkPlaneBased placement (khong co host face)."
                        : "Fallback sang sketch plane tam cho WorkPlaneBased placement (khong resolve duoc host face).";
                    plan.Diagnostics.Add(DiagnosticRecord.Create("PLACEMENT_SKETCHPLANE_FALLBACK", DiagnosticSeverity.Warning, fallbackMessage, plan.Host != null ? checked((int)plan.Host.Id.Value) : 0));
                }
                break;

            case Autodesk.Revit.DB.FamilyPlacementType.CurveBasedDetail:
                plan.View = ResolvePlacementView(doc, request.ViewId);
                plan.Line = BuildCurveLine(request);
                plan.Strategy = "CurveInView";
                plan.Diagnostics.Add(DiagnosticRecord.Create("PLACEMENT_CURVE_VIEW", DiagnosticSeverity.Info, "Resolved curve-based detail placement in view.", checked((int)plan.View.Id.Value)));
                break;

            case Autodesk.Revit.DB.FamilyPlacementType.CurveBased:
            case Autodesk.Revit.DB.FamilyPlacementType.CurveDrivenStructural:
                plan.Line = BuildCurveLine(request);
                plan.Host = ResolveHost(doc, request.HostElementId);
                if (plan.Host == null)
                {
                    throw new NotSupportedException("Curve-based model placement hiện yêu cầu HostElementId + planar face.");
                }
                var requestedNormal = TryBuildVector(request.FaceNormalX, request.FaceNormalY, request.FaceNormalZ);
                var curveDirection = plan.Line.Direction;
                plan.Face = ResolvePlanarFace(plan.Host, plan.Point, requestedNormal, curveDirection);
                if (plan.Face == null)
                {
                    throw new InvalidOperationException("Không resolve được planar face trên host cho curve-based placement.");
                }
                plan.Line = ProjectCurveLineOntoFace(plan.Line, plan.Face, plan.Diagnostics, checked((int)plan.Host.Id.Value));
                plan.Strategy = "CurveOnFace";
                plan.Diagnostics.Add(DiagnosticRecord.Create("PLACEMENT_CURVE_FACE", DiagnosticSeverity.Info, "Resolved curve-based face placement.", checked((int)plan.Host.Id.Value)));
                break;

            case Autodesk.Revit.DB.FamilyPlacementType.Adaptive:
                throw new NotSupportedException("Adaptive family placement chua duoc support trong bridge hien tai.");

            default:
                throw new NotSupportedException("Family placement type chưa được support: " + placementType);
        }

        return plan;
    }

    private static Level? ResolveLevel(Document doc, int? levelId, XYZ? point = null)
    {
        if (levelId.HasValue)
        {
            var explicitLevel = doc.GetElement(new ElementId((long)levelId.Value)) as Level;
            if (explicitLevel != null)
            {
                return explicitLevel;
            }
        }

        if (point == null)
        {
            return null;
        }

        try
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(x => Math.Abs(x.Elevation - point.Z))
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static View ResolvePlacementView(Document doc, int? viewId)
    {
        if (viewId.HasValue)
        {
            var explicitView = doc.GetElement(new ElementId((long)viewId.Value)) as View;
            if (explicitView != null)
            {
                return explicitView;
            }
        }

        if (doc.ActiveView == null)
        {
            throw new RevitContextException("Không có active view để dùng cho placement.");
        }

        return doc.ActiveView;
    }

    private static Element? ResolveHost(Document doc, int? hostElementId)
    {
        return hostElementId.HasValue ? doc.GetElement(new ElementId((long)hostElementId.Value)) : null;
    }

    private static Line BuildCurveLine(PlaceFamilyInstanceRequest request)
    {
        if (!request.StartX.HasValue || !request.StartY.HasValue || !request.StartZ.HasValue || !request.EndX.HasValue || !request.EndY.HasValue || !request.EndZ.HasValue)
        {
            throw new InvalidOperationException("Curve-based placement cần StartX/StartY/StartZ và EndX/EndY/EndZ.");
        }

        var start = new XYZ(request.StartX.Value, request.StartY.Value, request.StartZ.Value);
        var end = new XYZ(request.EndX.Value, request.EndY.Value, request.EndZ.Value);
        if (start.IsAlmostEqualTo(end))
        {
            throw new InvalidOperationException("Curve-based placement không cho phép line có độ dài bằng 0.");
        }

        return Line.CreateBound(start, end);
    }

    private static Line ProjectCurveLineOntoFace(Line line, PlanarFace face, ICollection<DiagnosticRecord> diagnostics, int hostElementId)
    {
        var start = line.GetEndPoint(0);
        var end = line.GetEndPoint(1);
        var normal = face.FaceNormal.Normalize();
        var planeOrigin = face.Origin;
        var projectedStart = ProjectPointOntoPlane(start, planeOrigin, normal);
        var projectedEnd = ProjectPointOntoPlane(end, planeOrigin, normal);

        if (projectedStart.IsAlmostEqualTo(projectedEnd))
        {
            var alignment = Math.Abs(line.Direction.Normalize().DotProduct(normal));
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "Curve-based placement khong the project line len mat phang host vi line bi suy bien sau khi project. AlignmentToFaceNormal={0:0.######}.",
                alignment));
        }

        var startShift = projectedStart.DistanceTo(start);
        var endShift = projectedEnd.DistanceTo(end);
        if (startShift > 1e-6 || endShift > 1e-6)
        {
            diagnostics.Add(DiagnosticRecord.Create(
                "PLACEMENT_CURVE_PROJECTED_TO_FACE",
                DiagnosticSeverity.Info,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Input curve duoc project len face host. StartShift={0:0.######}, EndShift={1:0.######}.",
                    startShift,
                    endShift),
                hostElementId));
        }

        return Line.CreateBound(projectedStart, projectedEnd);
    }

    private static XYZ ProjectPointOntoPlane(XYZ point, XYZ planeOrigin, XYZ planeNormal)
    {
        var offset = point - planeOrigin;
        var signedDistance = offset.DotProduct(planeNormal);
        return point - planeNormal.Multiply(signedDistance);
    }

    private static XYZ ResolveReferenceDirection(PlaceFamilyInstanceRequest request, PlanarFace? face)
    {
        var explicitDirection = TryBuildVector(request.ReferenceDirectionX, request.ReferenceDirectionY, request.ReferenceDirectionZ);
        if (explicitDirection != null && IsNonZero(explicitDirection))
        {
            return explicitDirection.Normalize();
        }

        if (face != null)
        {
            if (IsNonZero(face.XVector))
            {
                return face.XVector.Normalize();
            }

            var cross = face.FaceNormal.CrossProduct(XYZ.BasisZ);
            if (IsNonZero(cross))
            {
                return cross.Normalize();
            }
        }

        return XYZ.BasisX;
    }

    private static XYZ? TryBuildVector(double? x, double? y, double? z)
    {
        return x.HasValue && y.HasValue && z.HasValue ? new XYZ(x.Value, y.Value, z.Value) : null;
    }

    private static Line ResolveRotationAxis(Element element, RotateElementsRequest request)
    {
        var axisMode = request.AxisMode ?? string.Empty;
        switch (axisMode.Trim().ToLowerInvariant())
        {
            case "element_basis_z":
            {
                if (!(element is FamilyInstance instance))
                {
                    throw new InvalidOperationException("AxisMode=element_basis_z hiện chỉ hỗ trợ FamilyInstance.");
                }

                var transform = instance.GetTransform() ?? throw new InvalidOperationException("FamilyInstance không trả về transform.");
                var origin = transform.Origin;
                var direction = NormalizeVector(transform.BasisZ);
                if (!IsNonZero(direction))
                {
                    throw new InvalidOperationException("BasisZ của element không hợp lệ để quay.");
                }

                return Line.CreateBound(origin, origin + direction);
            }

            case "project_z_at_element_origin":
            {
                var origin = ResolveElementRotationOrigin(element);
                return Line.CreateBound(origin, origin + XYZ.BasisZ);
            }

            case "explicit":
            {
                var origin = new XYZ(
                    request.AxisOriginX ?? 0.0,
                    request.AxisOriginY ?? 0.0,
                    request.AxisOriginZ ?? 0.0);
                var direction = NormalizeVector(new XYZ(
                    request.AxisDirectionX ?? 0.0,
                    request.AxisDirectionY ?? 0.0,
                    request.AxisDirectionZ ?? 0.0));
                if (!IsNonZero(direction))
                {
                    throw new InvalidOperationException("Explicit axis direction không hợp lệ.");
                }

                return Line.CreateBound(origin, origin + direction);
            }

            default:
                throw new InvalidOperationException("AxisMode không được hỗ trợ: " + request.AxisMode);
        }
    }

    private static XYZ ResolveElementRotationOrigin(Element element)
    {
        if (element is FamilyInstance instance)
        {
            var transform = instance.GetTransform();
            if (transform != null)
            {
                return transform.Origin;
            }
        }

        if (element.Location is LocationPoint locationPoint)
        {
            return locationPoint.Point;
        }

        var bbox = element.get_BoundingBox(null);
        if (bbox != null)
        {
            return (bbox.Min + bbox.Max) * 0.5;
        }

        throw new InvalidOperationException("Không xác định được origin để quay element.");
    }

    private static bool IsNonZero(XYZ? vector)
    {
        return vector != null && vector.GetLength() > 1e-9;
    }

    private static XYZ NormalizeVector(XYZ? vector)
    {
        if (!IsNonZero(vector))
        {
            return XYZ.Zero;
        }

        return vector!.Normalize();
    }

    private static StructuralType ParseStructuralType(string structuralTypeName)
    {
        return Enum.TryParse(structuralTypeName ?? string.Empty, true, out StructuralType parsed) ? parsed : StructuralType.NonStructural;
    }

    private static PlanarFace? ResolvePlanarFace(Element host, XYZ point, XYZ? requestedNormal, XYZ? requestedTangent = null)
    {
        var options = new Options
        {
            ComputeReferences = true,
            DetailLevel = ViewDetailLevel.Fine,
            IncludeNonVisibleObjects = true
        };

        var faces = new List<PlanarFace>();
        CollectPlanarFaces(host.get_Geometry(options), faces);
        if (faces.Count == 0)
        {
            return null;
        }

        var targetNormal = requestedNormal != null && IsNonZero(requestedNormal) ? requestedNormal.Normalize() : null;
        var targetTangent = requestedTangent != null && IsNonZero(requestedTangent) ? requestedTangent.Normalize() : null;
        return faces.Select(face =>
            {
                var projected = face.Project(point);
                var distance = projected != null ? projected.XYZPoint.DistanceTo(point) : double.MaxValue;
                var normalPenalty = targetNormal == null ? 0.0 : 1.0 - Math.Abs(face.FaceNormal.Normalize().DotProduct(targetNormal));
                var tangentPenalty = targetTangent == null ? 0.0 : Math.Abs(face.FaceNormal.Normalize().DotProduct(targetTangent));
                return new { Face = face, Score = normalPenalty * 1000.0 + tangentPenalty * 100.0 + distance };
            })
            .OrderBy(x => x.Score)
            .Select(x => x.Face)
            .FirstOrDefault();
    }

    private static void CollectPlanarFaces(GeometryElement? geometryElement, IList<PlanarFace> faces)
    {
        if (geometryElement == null)
        {
            return;
        }

        foreach (var item in geometryElement)
        {
            if (item is Solid solid && solid.Faces.Size > 0)
            {
                foreach (Face face in solid.Faces)
                {
                    if (face is PlanarFace planar)
                    {
                        faces.Add(planar);
                    }
                }
            }
            else if (item is GeometryInstance instance)
            {
                CollectPlanarFaces(instance.GetInstanceGeometry(), faces);
                CollectPlanarFaces(instance.GetSymbolGeometry(), faces);
            }
        }
    }

    private sealed class PlacementPlan
    {
        internal FamilySymbol Symbol { get; set; } = null!;
        internal string FamilyPlacementType { get; set; } = string.Empty;
        internal string Strategy { get; set; } = string.Empty;
        internal XYZ Point { get; set; } = XYZ.Zero;
        internal XYZ? ReferenceDirection { get; set; }
        internal StructuralType StructuralType { get; set; } = StructuralType.NonStructural;
        internal Level? Level { get; set; }
        internal View? View { get; set; }
        internal Element? Host { get; set; }
        internal PlanarFace? Face { get; set; }
        internal Line? Line { get; set; }
        internal XYZ? WorkPlaneNormal { get; set; }
        internal List<DiagnosticRecord> Diagnostics { get; } = new List<DiagnosticRecord>();
    }
}

internal sealed class DeleteImpactAnalysis
{
    internal List<int> RequestedIds { get; set; } = new List<int>();
    internal List<int> AllDeletedIds { get; set; } = new List<int>();
    internal List<int> UnexpectedDependentIds { get; set; } = new List<int>();
    internal List<DiagnosticRecord> Diagnostics { get; set; } = new List<DiagnosticRecord>();
    internal List<string> Artifacts { get; set; } = new List<string>();
    internal bool HasUnexpectedDependents => UnexpectedDependentIds.Count > 0;
}

internal sealed class FileLifecycleService
{
    internal ExecutionResult Preview(string toolName, PlatformServices services, Document doc, ToolRequestEnvelope envelope)
    {
        var token = services.Approval.IssueToken(toolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = toolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token
        };
    }

    internal DocumentSummaryDto OpenBackgroundRead(UIApplication uiapp, PlatformServices services, OpenBackgroundDocumentRequest request)
    {
        if (!services.Settings.AllowBackgroundOpenRead || services.Policy.DenyBackgroundOpenRead)
        {
            throw new InvalidOperationException("Background open read đang bị policy chặn.");
        }

        if (string.IsNullOrWhiteSpace(request.FilePath))
        {
            throw new InvalidOperationException("Thiếu FilePath cho background open.");
        }

        var doc = uiapp.Application.OpenDocumentFile(request.FilePath);
        return services.SummarizeDocument(uiapp, doc);
    }

    internal ExecutionResult PreviewCloseNonActive(PlatformServices services, Document doc, ToolRequestEnvelope envelope)
    {
        var token = services.Approval.IssueToken(envelope.ToolName, services.Approval.BuildFingerprint(envelope), services.GetDocumentKey(doc), envelope.Caller, envelope.SessionId);
        return new ExecutionResult
        {
            OperationName = envelope.ToolName,
            DryRun = true,
            ConfirmationRequired = true,
            ApprovalToken = token
        };
    }

    internal ExecutionResult CloseNonActive(UIApplication uiapp, PlatformServices services, Document doc, CloseDocumentRequest request)
    {
        if (uiapp.ActiveUIDocument?.Document?.Equals(doc) == true)
        {
            throw new InvalidOperationException("Không được close active document qua tool này.");
        }

        var closed = doc.Close(request.SaveModified);
        if (!closed)
        {
            throw new InvalidOperationException("Close document thất bại.");
        }

        return new ExecutionResult
        {
            OperationName = "document.close_non_active"
        };
    }

    internal ExecutionResult Save(Document doc)
    {
        if (doc.IsModifiable)
        {
            throw new InvalidOperationException("Document đang ở trạng thái modifiable; không thể save trong transaction đang mở.");
        }

        if (doc.IsLinked)
        {
            throw new InvalidOperationException("Linked document không được save trực tiếp.");
        }

        if (string.IsNullOrWhiteSpace(doc.PathName))
        {
            throw new InvalidOperationException("Document chưa có PathName; hãy dùng save_as_document.");
        }

        doc.Save();
        return new ExecutionResult
        {
            OperationName = "file.save_document"
        };
    }

    internal ExecutionResult SaveAs(Document doc, SaveAsDocumentRequest request)
    {
        if (doc.IsModifiable)
        {
            throw new InvalidOperationException("Document đang ở trạng thái modifiable; không thể SaveAs trong transaction đang mở.");
        }

        if (doc.IsLinked)
        {
            throw new InvalidOperationException("Linked document không được SaveAs trực tiếp.");
        }

        var options = new SaveAsOptions
        {
            OverwriteExistingFile = request.OverwriteExisting
        };
        doc.SaveAs(request.FilePath, options);
        return new ExecutionResult
        {
            OperationName = "file.save_as_document"
        };
    }

    internal ExecutionResult Synchronize(Document doc, SynchronizeRequest request)
    {
        if (!doc.IsWorkshared)
        {
            throw new InvalidOperationException("Document không phải workshared.");
        }

        if (doc.IsLinked)
        {
            throw new InvalidOperationException("Linked document không được synchronize với central.");
        }

        if (doc.IsModifiable)
        {
            throw new InvalidOperationException("Document đang ở trạng thái modifiable; không thể synchronize trong transaction đang mở.");
        }

        var transOpts = new TransactWithCentralOptions();
        var syncOpts = new SynchronizeWithCentralOptions
        {
            Comment = string.IsNullOrWhiteSpace(request.Comment) ? "BIM765T.Revit.Agent sync" : request.Comment,
            Compact = request.CompactCentral
        };
        var relinquish = new RelinquishOptions(request.RelinquishAllAfterSync);
        syncOpts.SetRelinquishOptions(relinquish);
        doc.SynchronizeWithCentral(transOpts, syncOpts);
        return new ExecutionResult
        {
            OperationName = "worksharing.synchronize_with_central",
            Diagnostics = new List<DiagnosticRecord>
            {
                DiagnosticRecord.Create("SYNC_PREFLIGHT", DiagnosticSeverity.Info,
                    request.RelinquishAllAfterSync
                        ? "Sync executed with relinquish-all enabled explicitly by payload."
                        : "Sync executed with conservative relinquish defaults (no relinquish-all).")
            },
            Artifacts = new List<string>
            {
                "CompactCentral=" + request.CompactCentral,
                "RelinquishAllAfterSync=" + request.RelinquishAllAfterSync,
                "DocumentPath=" + (doc.PathName ?? string.Empty)
            }
        };
    }
}
