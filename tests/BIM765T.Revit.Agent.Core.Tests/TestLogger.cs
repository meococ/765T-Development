using System;
using System.Collections.Generic;
using BIM765T.Revit.Agent.Infrastructure.Logging;

namespace BIM765T.Revit.Agent.Core.Tests;

internal sealed class TestLogger : IAgentLogger
{
    internal List<string> Messages { get; } = new List<string>();

    public void Info(string message)
    {
        Messages.Add("INFO:" + message);
    }

    public void Warn(string message)
    {
        Messages.Add("WARN:" + message);
    }

    public void Error(string message, Exception? ex = null)
    {
        Messages.Add("ERROR:" + message + (ex != null ? "|" + ex.GetType().Name : string.Empty));
    }

    public IDisposable BeginScope(string? correlationId, string? toolName = null, string? source = null)
    {
        return NoopDisposable.Instance;
    }

    private sealed class NoopDisposable : IDisposable
    {
        internal static readonly NoopDisposable Instance = new NoopDisposable();

        public void Dispose()
        {
        }
    }
}
