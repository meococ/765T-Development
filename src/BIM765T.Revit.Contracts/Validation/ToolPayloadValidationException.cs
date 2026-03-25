using System;
using System.Collections.Generic;
using System.Linq;
using BIM765T.Revit.Contracts.Common;

namespace BIM765T.Revit.Contracts.Validation;

public sealed class ToolPayloadValidationException : Exception
{
    public ToolPayloadValidationException(string message, IEnumerable<DiagnosticRecord>? diagnostics = null)
        : base(message)
    {
        Diagnostics = diagnostics?.ToList() ?? new List<DiagnosticRecord>();
    }

    public IReadOnlyList<DiagnosticRecord> Diagnostics { get; }
}
