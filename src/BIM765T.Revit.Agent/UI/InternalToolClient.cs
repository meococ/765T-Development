using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using Autodesk.Revit.UI;
using BIM765T.Revit.Agent.Infrastructure;
using BIM765T.Revit.Agent.Infrastructure.Bridge;
using BIM765T.Revit.Contracts.Bridge;
using BIM765T.Revit.Contracts.Platform;

namespace BIM765T.Revit.Agent.UI;

/// <summary>
/// Cho phép UI gọi tool qua ExternalEvent pipeline — CÙNG đường đi với CLI/MCP.
/// UI KHÔNG gọi Revit API trực tiếp. Mọi call đều đi qua policy check + audit journal.
///
/// Flow: ActionButton click → CallAsync() → Queue.Enqueue → ExternalEvent.Raise()
///       → ToolExecutor.Execute (UI thread) → callback trả về → Dispatcher.Invoke update UI.
/// </summary>
internal sealed class InternalToolClient
{
    private readonly Dispatcher _dispatcher;
    private static InternalToolClient? _instance;

    /// <summary>Fired when a tool call starts (for progress bar).</summary>
    internal event Action? ToolStarted;

    /// <summary>Fired when a tool call completes (for progress bar).</summary>
    internal event Action? ToolCompleted;

    internal static InternalToolClient Instance =>
        _instance ?? throw new InvalidOperationException("InternalToolClient chưa được khởi tạo.");

    internal static void Initialize(Dispatcher dispatcher)
    {
        _instance = new InternalToolClient(dispatcher);
    }

    private InternalToolClient(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// Gọi tool và nhận kết quả. Có thể gọi từ bất kỳ thread nào — kết quả sẽ
    /// được trả về trên calling thread (hoặc qua callback trên UI thread).
    /// </summary>
    internal Task<ToolResponseEnvelope> CallAsync(string toolName, string? payloadJson = null, bool dryRun = true, string? approvalToken = null, string? previewRunId = null)
    {
        if (!AgentHost.TryGetCurrent(out var runtime) || runtime == null)
        {
            return Task.FromResult(CreateErrorResponse(toolName, "Agent host chưa khởi tạo."));
        }

        var correlationId = Guid.NewGuid().ToString("N");
        runtime.Registry.TryGet(toolName, out var registration);
        var request = new ToolRequestEnvelope
        {
            RequestId = Guid.NewGuid().ToString("N"),
            ToolName = toolName,
            PayloadJson = payloadJson ?? "{}",
            Caller = "AgentPane",
            SessionId = "agent-pane-session",
            DryRun = dryRun,
            ApprovalToken = approvalToken ?? string.Empty,
            PreviewRunId = previewRunId ?? string.Empty,
            CorrelationId = correlationId,
            RequestedPriority = registration != null
                ? ToolQueuePriorityResolver.Resolve(new ToolRequestEnvelope { ToolName = toolName, DryRun = dryRun }, registration.Manifest).Priority
                : ToolQueuePriorities.Normal
        };

        var invocation = new PendingToolInvocation(request, registration?.Manifest);
        runtime.Queue.Enqueue(invocation);

        var raiseResult = runtime.ExternalEvent.Raise();
        if (raiseResult != ExternalEventRequest.Accepted)
        {
            invocation.TryCancelBeforeExecution();
            return Task.FromResult(CreateErrorResponse(toolName,
                $"ExternalEvent.Raise() trả về {raiseResult}. Revit có thể đang bận hoặc hộp thoại modal đang mở."));
        }

        ToolStarted?.Invoke();

        return invocation.Completion.Task.ContinueWith(task =>
        {
            ToolCompleted?.Invoke();
            return task.Result;
        });
    }

    /// <summary>
    /// Gọi tool rồi tự động chạy callback trên UI thread khi xong.
    /// Pattern phổ biến cho UI buttons.
    /// </summary>
    internal void CallWithCallback(string toolName, string? payloadJson, bool dryRun, Action<ToolResponseEnvelope> onComplete, string? approvalToken = null, string? previewRunId = null)
    {
        CallAsync(toolName, payloadJson, dryRun, approvalToken, previewRunId).ContinueWith(task =>
        {
            var response = task.IsFaulted
                ? CreateErrorResponse(toolName, task.Exception?.InnerException?.Message ?? "Unknown error")
                : task.Result;

            _dispatcher.Invoke(() => onComplete(response));
        });
    }

    private static ToolResponseEnvelope CreateErrorResponse(string toolName, string message)
    {
        var requestId = Guid.NewGuid().ToString("N");
        return new ToolResponseEnvelope
        {
            RequestId = requestId,
            CorrelationId = requestId,
            ToolName = toolName,
            Succeeded = false,
            StatusCode = "INTERNAL_ERROR",
            Diagnostics = new System.Collections.Generic.List<Contracts.Common.DiagnosticRecord>
            {
                Contracts.Common.DiagnosticRecord.Create("UI_ERROR", Contracts.Common.DiagnosticSeverity.Error, message)
            }
        };
    }
}

