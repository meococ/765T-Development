using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BIM765T.Revit.Copilot.Core.Brain;

/// <summary>
/// A diagnostic-aware stub implementation of <see cref="ILlmClient"/> that reports
/// "LLM not configured" clearly instead of silently failing or throwing.
/// Register this when no real LLM provider is available so the system runs in
/// rule-only mode with AI response capability degraded but all other features intact.
/// </summary>
public sealed class NullLlmClient : ILlmClient
{
    /// <summary>
    /// The fixed diagnostic string returned by every <see cref="CompleteAsync"/> call.
    /// It surfaces both the root cause and the remediation hint to any downstream consumer.
    /// </summary>
    public const string DiagnosticResponse =
        "[LLM not configured] Running in rule-only mode. Configure ILlmClient to enable AI responses.";

    private const string StartupWarning =
        "BIM765T NullLlmClient: LLM client is not configured. WorkerHost is running in rule-only mode. "
        + "AI-generated responses are disabled. Register a real ILlmClient implementation to enable them.";

    // 0 = not yet logged; 1 = logged. CAS prevents duplicate emission even under concurrent first calls.
    private static int _startupLogged;

    /// <inheritdoc />
    /// <remarks>
    /// Always returns <see cref="DiagnosticResponse"/> — never throws, never blocks.
    /// On the very first call, emits a one-time <see cref="Trace.TraceWarning"/> so the
    /// condition is visible in any attached trace listener or debug output without requiring
    /// a logger dependency on this netstandard2.0 assembly.
    /// The <paramref name="systemPrompt"/> and <paramref name="userMessage"/> parameters
    /// are accepted but intentionally ignored because no model is available.
    /// </remarks>
    public Task<string> CompleteAsync(string systemPrompt, string userMessage, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _startupLogged, 1, 0) == 0)
        {
            Trace.TraceWarning(StartupWarning);
        }

        return Task.FromResult(DiagnosticResponse);
    }
}
