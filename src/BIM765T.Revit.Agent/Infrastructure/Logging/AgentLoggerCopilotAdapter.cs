using System;
using BIM765T.Revit.Copilot.Core.Brain;

namespace BIM765T.Revit.Agent.Infrastructure.Logging;

/// <summary>
/// Adapts the Agent-side <see cref="IAgentLogger"/> to the Copilot.Core
/// <see cref="ICopilotLogger"/> interface so that LLM clients, the response
/// enhancer and other Copilot.Core services log through the same FileLogger
/// pipeline as the rest of the Agent process.
/// </summary>
internal sealed class AgentLoggerCopilotAdapter : ICopilotLogger
{
    private readonly IAgentLogger _inner;

    public AgentLoggerCopilotAdapter(IAgentLogger inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public void Info(string message) => _inner.Info(message);
    public void Warn(string message) => _inner.Warn(message);
    public void Error(string message, Exception? ex) => _inner.Error(message, ex);
}
