using System;
using BIM765T.Revit.Copilot.Core.Brain;
using Microsoft.Extensions.Logging;

namespace BIM765T.Revit.WorkerHost;

/// <summary>
/// Adapts an ASP.NET Core <see cref="ILogger"/> to the Copilot.Core
/// <see cref="ICopilotLogger"/> interface so that shared Copilot.Core services
/// (LLM clients, SmartQc, etc.) log through the WorkerHost logging pipeline.
/// </summary>
internal sealed class MsLoggerCopilotAdapter : ICopilotLogger
{
    private static readonly Action<ILogger, string, Exception?> LogInfoMsg =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1, "CopilotInfo"), "{Message}");

    private static readonly Action<ILogger, string, Exception?> LogWarnMsg =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(2, "CopilotWarn"), "{Message}");

    private static readonly Action<ILogger, string, Exception?> LogErrorMsg =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(3, "CopilotError"), "{Message}");

    private readonly ILogger _inner;

    public MsLoggerCopilotAdapter(ILogger inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public void Info(string message) => LogInfoMsg(_inner, message, null);
    public void Warn(string message) => LogWarnMsg(_inner, message, null);
    public void Error(string message, Exception? ex) => LogErrorMsg(_inner, message, ex);
}
