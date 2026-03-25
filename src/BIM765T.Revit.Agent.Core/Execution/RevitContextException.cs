using System;

namespace BIM765T.Revit.Agent.Core.Execution;

/// <summary>
/// Raised only for genuine Revit context availability problems
/// (for example: no active document/view).
/// Business-rule and implementation bugs should use other exception types.
/// </summary>
public sealed class RevitContextException : InvalidOperationException
{
    public RevitContextException(string message)
        : base(message)
    {
    }

    public RevitContextException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
