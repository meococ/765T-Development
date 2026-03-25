using System;

namespace BIM765T.Revit.Agent.Infrastructure.Logging;

/// <summary>
/// Interface cho logging service — cho phép mock trong unit tests.
/// FileLogger là production implementation.
/// </summary>
internal interface IAgentLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
    IDisposable BeginScope(string? correlationId, string? toolName = null, string? source = null);
}
