using System;
using System.Diagnostics.CodeAnalysis;

namespace BIM765T.Revit.Copilot.Core.Brain;

/// <summary>
/// Lightweight logging abstraction for Copilot.Core (netstandard2.0).
/// Agent-side injects IAgentLogger adapter; WorkerHost injects ILogger adapter.
/// Falls back to Trace when no implementation is provided.
/// </summary>
[SuppressMessage("Microsoft.Naming", "CA1716:IdentifiersShouldNotMatchKeywords",
    Justification = "Error() aligns with IAgentLogger.Error() convention across the codebase.")]
public interface ICopilotLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}

/// <summary>
/// Default fallback that writes to System.Diagnostics.Trace.
/// Used when no real logger is injected — same behavior as before,
/// but callers now have a seam to replace it.
/// </summary>
public sealed class TraceCopilotLogger : ICopilotLogger
{
    public static readonly TraceCopilotLogger Instance = new TraceCopilotLogger();

    public void Info(string message) => System.Diagnostics.Trace.TraceInformation(message);
    public void Warn(string message) => System.Diagnostics.Trace.TraceWarning(message);
    public void Error(string message, Exception? ex = null) =>
        System.Diagnostics.Trace.TraceWarning(ex != null ? $"{message} {ex}" : message);
}
